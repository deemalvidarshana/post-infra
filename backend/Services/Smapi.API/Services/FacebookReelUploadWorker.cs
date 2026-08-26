using System.Diagnostics;
using System.Globalization;
using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public class FacebookReelUploadWorker : BackgroundService
    {
        private static readonly TimeSpan DueJobPollInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PublishedVideoRetention = TimeSpan.FromMinutes(5);
        private const double StorySegmentSeconds = 30d;
        private readonly IFacebookReelUploadQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FacebookReelUploadWorker> _logger;

        public FacebookReelUploadWorker(
            IFacebookReelUploadQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<FacebookReelUploadWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var dequeueTask = _queue.DequeueAsync(stoppingToken).AsTask();
                    var delayTask = Task.Delay(DueJobPollInterval, stoppingToken);
                    var completedTask = await Task.WhenAny(dequeueTask, delayTask);

                    if (completedTask == dequeueTask)
                    {
                        var workItem = await dequeueTask;
                        await ProcessAsync(workItem, stoppingToken);
                        continue;
                    }

                    var dueJobId = await GetNextDueQueuedJobIdAsync(stoppingToken);
                    if (dueJobId.HasValue)
                    {
                        await ProcessAsync(new FacebookReelUploadWorkItem(dueJobId.Value, new()), stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected Facebook Reel upload worker loop error.");
                }
            }
        }

        private async Task ProcessAsync(FacebookReelUploadWorkItem workItem, CancellationToken cancellationToken)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "smapi-reel-uploads", workItem.JobId.ToString());

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
                var downloader = scope.ServiceProvider.GetRequiredService<IYtDlpVideoDownloader>();
                var storage = scope.ServiceProvider.GetRequiredService<ILocalVideoStorageService>();
                var publisher = scope.ServiceProvider.GetRequiredService<IFacebookReelsPublisher>();

                var job = await dbContext.FacebookReelUploadJobs
                    .FirstAsync(item => item.Id == workItem.JobId, cancellationToken);

                if (job.Status != FacebookReelUploadJobStatus.Queued)
                {
                    return;
                }

                if (job.ScheduledFor.HasValue && job.ScheduledFor.Value > DateTime.UtcNow)
                {
                    return;
                }

                var page = await dbContext.FacebookPages
                    .FirstOrDefaultAsync(item => item.UserId == job.UserId && item.PageId == job.PageId, cancellationToken);

                if (page is null)
                {
                    throw new InvalidOperationException("Connected Facebook page was not found for this user.");
                }

                job.Status = string.IsNullOrWhiteSpace(job.S3Key)
                    ? FacebookReelUploadJobStatus.Downloading
                    : FacebookReelUploadJobStatus.Publishing;
                job.StartedAt ??= DateTime.UtcNow;
                job.Attempts += 1;
                job.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                string uploadVideoPath;
                if (!string.IsNullOrWhiteSpace(job.S3Key))
                {
                    uploadVideoPath = storage.GetAbsolutePath(job.S3Key);
                    if (!File.Exists(uploadVideoPath))
                    {
                        _logger.LogWarning(
                            "Reel upload job {JobId} could not find scraper download at {VideoPath}. Downloading the source again before publishing.",
                            job.Id,
                            uploadVideoPath);

                        job.Status = FacebookReelUploadJobStatus.Downloading;
                        job.S3Key = null;
                        job.S3Bucket = null;
                        job.S3Region = null;
                        job.S3EndpointUrl = null;
                        job.UpdatedAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync(cancellationToken);

                        var recoveredVideoPath = await downloader.DownloadAsync(
                            job.VideoSourceUrl,
                            tempDirectory,
                            cancellationToken);
                        var recoveredStorageKey = storage.BuildStorageKey(
                            page.PageName,
                            job.PageId,
                            "publishing-queue",
                            job.Id);
                        var recoveredStorageResult = await storage.StoreAsync(
                            recoveredVideoPath,
                            recoveredStorageKey,
                            cancellationToken);

                        job.S3Key = recoveredStorageResult.Key;
                        job.Status = FacebookReelUploadJobStatus.Publishing;
                        job.UpdatedAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync(cancellationToken);

                        uploadVideoPath = recoveredStorageResult.AbsolutePath;
                        _logger.LogInformation(
                            "Reel upload job {JobId} recovered its missing download at {VideoPath}.",
                            job.Id,
                            uploadVideoPath);
                    }
                    else
                    {
                        _logger.LogInformation("Reel upload job {JobId} using existing scraper download at {VideoPath}.", job.Id, uploadVideoPath);
                    }
                }
                else
                {
                    var localVideoPath = await downloader.DownloadAsync(job.VideoSourceUrl, tempDirectory, cancellationToken);
                    await UpdateStatusAsync(workItem.JobId, FacebookReelUploadJobStatus.Downloaded, cancellationToken);

                    var storageKey = storage.BuildStorageKey(page.PageName, job.PageId, "publishing-queue", job.Id);
                    var storageResult = await storage.StoreAsync(
                        localVideoPath,
                        storageKey,
                        cancellationToken);

                    job.S3Key = storageResult.Key;
                    job.S3Bucket = null;
                    job.S3Region = null;
                    job.S3EndpointUrl = null;
                    job.UpdatedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);

                    uploadVideoPath = storageResult.AbsolutePath;
                    _logger.LogInformation("Reel upload job {JobId} stored locally at {VideoPath}. Starting direct binary upload to Facebook.", job.Id, uploadVideoPath);

                    await UpdateStatusAsync(workItem.JobId, FacebookReelUploadJobStatus.Publishing, cancellationToken);
                }

                var publishResult = await publisher.PublishAsync(
                    job.PageId,
                    page.AccessToken,
                    uploadVideoPath,
                    job.Caption,
                    job.GraphApiVersion,
                    cancellationToken);

                job.FacebookVideoId = publishResult.VideoId;
                job.FacebookPostId = publishResult.PostId;

                if (job.PublishAsStory)
                {
                    try
                    {
                        var storyVideoPaths = await PrepareStoryVideoPathsAsync(uploadVideoPath, tempDirectory, cancellationToken);
                        var storyIds = new List<string>();

                        for (var storyIndex = 0; storyIndex < storyVideoPaths.Count; storyIndex++)
                        {
                            var storyVideoPath = storyVideoPaths[storyIndex];
                            _logger.LogInformation(
                                "Facebook Reel upload job {JobId} publishing Page Story segment {Segment}/{Total}. Path: {Path}",
                                job.Id,
                                storyIndex + 1,
                                storyVideoPaths.Count,
                                storyVideoPath);

                            var storyResult = await publisher.PublishStoryAsync(
                                job.PageId,
                                page.AccessToken,
                                storyVideoPath,
                                job.GraphApiVersion,
                                cancellationToken);

                            if (!string.IsNullOrWhiteSpace(storyResult.StoryId))
                            {
                                storyIds.Add(storyResult.StoryId);
                            }
                        }

                        job.FacebookStoryId = storyIds.LastOrDefault();
                        job.StoryPublishedAt = DateTime.UtcNow;
                        job.StoryErrorMessage = null;

                        _logger.LogInformation(
                            "Facebook Reel upload job {JobId} published {StoryCount} Page Story segment(s).",
                            job.Id,
                            storyVideoPaths.Count);
                    }
                    catch (Exception storyEx)
                    {
                        _logger.LogError(storyEx, "Facebook Reel upload job {JobId} published the video but failed to publish the Page Story.", job.Id);
                        job.FacebookStoryId = null;
                        job.StoryPublishedAt = null;
                        job.StoryErrorMessage = TrimForLog(storyEx.Message);
                    }
                }

                job.Status = FacebookReelUploadJobStatus.Published;
                job.CompletedAt = DateTime.UtcNow;
                job.UpdatedAt = DateTime.UtcNow;
                job.RetainUntil = DateTime.UtcNow.Add(PublishedVideoRetention);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Facebook Reel upload job {JobId} failed.", workItem.JobId);
                await MarkFailedAsync(workItem.JobId, ex.Message, cancellationToken);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        private async Task<int?> GetNextDueQueuedJobIdAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
            var now = DateTime.UtcNow;

            return await dbContext.FacebookReelUploadJobs
                .AsNoTracking()
                .Where(job => job.Status == FacebookReelUploadJobStatus.Queued
                    && (!job.ScheduledFor.HasValue || job.ScheduledFor <= now))
                .OrderBy(job => job.ScheduledFor ?? job.CreatedAt)
                .Select(job => (int?)job.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task UpdateStatusAsync(int jobId, string status, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
            var job = await dbContext.FacebookReelUploadJobs.FirstAsync(item => item.Id == jobId, cancellationToken);

            job.Status = status;
            job.UpdatedAt = DateTime.UtcNow;

            if (status == FacebookReelUploadJobStatus.Downloading)
            {
                job.StartedAt ??= DateTime.UtcNow;
                job.Attempts += 1;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkFailedAsync(int jobId, string errorMessage, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
            var job = await dbContext.FacebookReelUploadJobs.FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);

            if (job is null)
            {
                return;
            }

            job.Status = FacebookReelUploadJobStatus.Failed;
            job.ErrorMessage = TrimForLog(errorMessage);
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<string>> PrepareStoryVideoPathsAsync(
            string sourceVideoPath,
            string tempDirectory,
            CancellationToken cancellationToken)
        {
            var durationSeconds = await GetVideoDurationSecondsAsync(sourceVideoPath, cancellationToken);
            if (durationSeconds <= StorySegmentSeconds + 0.5d)
            {
                _logger.LogInformation(
                    "Story video is {Duration:N1}s; using the original file without splitting.",
                    durationSeconds);
                return new[] { sourceVideoPath };
            }

            var storyDirectory = Path.Combine(tempDirectory, "story-segments");
            Directory.CreateDirectory(storyDirectory);

            var segmentPaths = new List<string>();
            for (var startSeconds = 0d; startSeconds < durationSeconds; startSeconds += StorySegmentSeconds)
            {
                var remainingSeconds = durationSeconds - startSeconds;
                if (remainingSeconds < 1d)
                {
                    break;
                }

                var segmentSeconds = Math.Min(StorySegmentSeconds, remainingSeconds);
                var segmentPath = Path.Combine(storyDirectory, $"story-part-{segmentPaths.Count + 1:000}.mp4");
                await CreateStorySegmentAsync(
                    sourceVideoPath,
                    segmentPath,
                    startSeconds,
                    segmentSeconds,
                    cancellationToken);
                segmentPaths.Add(segmentPath);
            }

            if (segmentPaths.Count == 0)
            {
                throw new InvalidOperationException("No Facebook Page Story video segments could be created.");
            }

            _logger.LogInformation(
                "Prepared {SegmentCount} Facebook Page Story segment(s) from a {Duration:N1}s video.",
                segmentPaths.Count,
                durationSeconds);

            return segmentPaths;
        }

        private async Task<double> GetVideoDurationSecondsAsync(string sourceVideoPath, CancellationToken cancellationToken)
        {
            var result = await RunProcessAsync(
                "ffprobe",
                new[]
                {
                    "-v", "error",
                    "-show_entries", "format=duration",
                    "-of", "default=noprint_wrappers=1:nokey=1",
                    sourceVideoPath
                },
                cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffprobe failed while reading Story video duration: {TrimForLog(result.Stderr)}");
            }

            if (!double.TryParse(result.Stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds)
                || durationSeconds <= 0d)
            {
                throw new InvalidOperationException($"ffprobe returned an invalid Story video duration: {TrimForLog(result.Stdout)}");
            }

            return durationSeconds;
        }

        private async Task CreateStorySegmentAsync(
            string sourceVideoPath,
            string segmentPath,
            double startSeconds,
            double segmentSeconds,
            CancellationToken cancellationToken)
        {
            var result = await RunProcessAsync(
                "ffmpeg",
                new[]
                {
                    "-y",
                    "-hide_banner",
                    "-loglevel", "error",
                    "-ss", startSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    "-i", sourceVideoPath,
                    "-t", segmentSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    "-map", "0:v:0",
                    "-map", "0:a?",
                    "-sn",
                    "-dn",
                    "-vf", "scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,setsar=1",
                    "-c:v", "libx264",
                    "-preset", "veryfast",
                    "-crf", "23",
                    "-pix_fmt", "yuv420p",
                    "-c:a", "aac",
                    "-b:a", "128k",
                    "-ar", "44100",
                    "-ac", "2",
                    "-movflags", "+faststart",
                    segmentPath
                },
                cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffmpeg failed while creating a Story segment: {TrimForLog(result.Stderr)}");
            }

            var segmentInfo = new FileInfo(segmentPath);
            if (!segmentInfo.Exists || segmentInfo.Length == 0)
            {
                throw new InvalidOperationException("ffmpeg created an empty Facebook Page Story segment.");
            }
        }

        private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            using var process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{fileName} could not be started.", ex);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return (process.ExitCode, await stdoutTask, await stderrTask);
        }

        private static void TryDeleteDirectory(string tempDirectory)
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Temporary files are cleaned best-effort; failed cleanup should not hide upload errors.
            }
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }
    }
}
