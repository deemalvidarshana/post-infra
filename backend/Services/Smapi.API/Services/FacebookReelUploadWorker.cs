using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public class FacebookReelUploadWorker : BackgroundService
    {
        private static readonly TimeSpan DueJobPollInterval = TimeSpan.FromSeconds(10);
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

                job.Status = FacebookReelUploadJobStatus.Downloading;
                job.StartedAt ??= DateTime.UtcNow;
                job.Attempts += 1;
                job.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

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

                _logger.LogInformation("Reel upload job {JobId} stored locally at {VideoPath}. Starting direct binary upload to Facebook.", job.Id, storageResult.AbsolutePath);

                await UpdateStatusAsync(workItem.JobId, FacebookReelUploadJobStatus.Publishing, cancellationToken);

                var publishResult = await publisher.PublishAsync(
                    job.PageId,
                    page.AccessToken,
                    storageResult.AbsolutePath,
                    job.Caption,
                    job.GraphApiVersion,
                    cancellationToken);

                job.FacebookVideoId = publishResult.VideoId;
                job.FacebookPostId = publishResult.PostId;
                job.Status = FacebookReelUploadJobStatus.Published;
                job.CompletedAt = DateTime.UtcNow;
                job.UpdatedAt = DateTime.UtcNow;
                job.RetainUntil = DateTime.UtcNow.AddDays(7);
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
