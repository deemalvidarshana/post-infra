using Smapi.API.Data;
using Smapi.API.Models;
using Smapi.API.Models.DTOs;
using Smapi.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FacebookReelUploadsController : ControllerBase
    {
        private readonly SmapiDbContext _context;
        private readonly IFacebookReelUploadQueue _queue;
        private readonly ILocalVideoStorageService _storage;
        private readonly ILogger<FacebookReelUploadsController> _logger;

        public FacebookReelUploadsController(
            SmapiDbContext context, 
            IFacebookReelUploadQueue queue,
            ILocalVideoStorageService storage,
            ILogger<FacebookReelUploadsController> logger)
        {
            _context = context;
            _queue = queue;
            _storage = storage;
            _logger = logger;
        }

        [HttpGet("{userId}")]
        public async Task<ActionResult<IEnumerable<FacebookReelUploadJobResponse>>> GetJobs(
            string userId,
            [FromQuery] string? pageId,
            [FromQuery] string? platform,
            CancellationToken cancellationToken)
        {
            var query = _context.FacebookReelUploadJobs
                .AsNoTracking()
                .Where(job => job.UserId == userId);

            if (!string.IsNullOrWhiteSpace(pageId))
            {
                query = query.Where(job => job.PageId == pageId);
            }

            var normalizedPlatform = NormalizeSourcePlatform(platform);
            if (!string.IsNullOrWhiteSpace(normalizedPlatform))
            {
                query = query.Where(job => job.FacebookPostUrl != null
                    && job.FacebookPostUrl.Platform == normalizedPlatform);
            }

            var jobs = await query
                .OrderByDescending(job => job.CreatedAt)
                .Take(200)
                .Select(job => ToResponse(job))
                .ToListAsync(cancellationToken);

            return Ok(jobs);
        }

        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryJob(int id, CancellationToken cancellationToken)
        {
            var job = await _context.FacebookReelUploadJobs.FindAsync(new object[] { id }, cancellationToken);
            if (job == null)
            {
                return NotFound(new { success = false, message = "Upload job not found." });
            }

            if (job.Status == FacebookReelUploadJobStatus.Published)
            {
                return BadRequest(new { success = false, message = "This job has already been published successfully." });
            }

            job.Status = FacebookReelUploadJobStatus.Queued;
            job.ErrorMessage = null;
            job.Attempts = 0;
            job.ScheduledFor = DateTime.UtcNow; // Trigger immediately
            job.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            
            await _queue.QueueAsync(new FacebookReelUploadWorkItem(job.Id, new CreateFacebookReelUploadJobRequest()), cancellationToken);

            return Ok(new { success = true, message = "Job has been queued for re-upload." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteJob(int id, CancellationToken cancellationToken)
        {
            var job = await _context.FacebookReelUploadJobs.FindAsync(new object[] { id }, cancellationToken);
            if (job == null)
            {
                return NotFound(new { success = false, message = "Upload job not found." });
            }

            _context.FacebookReelUploadJobs.Remove(job);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { success = true, message = $"Deleted upload job #{id}." });
        }

        [HttpPost("{id}/pause")]
        public async Task<IActionResult> PauseJob(int id, CancellationToken cancellationToken)
        {
            var job = await _context.FacebookReelUploadJobs.FindAsync(new object[] { id }, cancellationToken);
            if (job == null)
            {
                return NotFound(new { success = false, message = "Upload job not found." });
            }

            if (job.Status != FacebookReelUploadJobStatus.Queued)
            {
                return BadRequest(new { success = false, message = "Only queued jobs can be paused." });
            }

            job.Status = FacebookReelUploadJobStatus.Paused;
            job.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = $"Paused upload job #{job.Id}.",
                job = ToResponse(job)
            });
        }

        [HttpPost("{id}/resume")]
        public async Task<IActionResult> ResumeJob(
            int id,
            [FromBody] ResumeFacebookReelUploadJobRequest request,
            CancellationToken cancellationToken)
        {
            var job = await _context.FacebookReelUploadJobs.FindAsync(new object[] { id }, cancellationToken);
            if (job == null)
            {
                return NotFound(new { success = false, message = "Upload job not found." });
            }

            if (job.Status != FacebookReelUploadJobStatus.Paused)
            {
                return BadRequest(new { success = false, message = "Only paused jobs can be started again." });
            }

            var scheduledFor = NormalizeUtc(request.ScheduledFor);
            if (scheduledFor < DateTime.UtcNow.AddMinutes(-1))
            {
                return BadRequest(new { success = false, message = "Select a future publish time before starting this job." });
            }

            job.Status = FacebookReelUploadJobStatus.Queued;
            job.ScheduledFor = scheduledFor;
            job.GraphApiVersion = NormalizeGraphApiVersion(request.GraphApiVersion);
            job.ErrorMessage = null;
            job.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = $"Started upload job #{job.Id} for {job.ScheduledFor.Value.ToLocalTime():g}.",
                job = ToResponse(job)
            });
        }

        [HttpPost]
        public async Task<ActionResult<FacebookReelUploadJobResponse>> CreateJob(
            [FromBody] CreateFacebookReelUploadJobRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.PageId = request.PageId.Trim();
            request.GraphApiVersion = NormalizeGraphApiVersion(request.GraphApiVersion);
            request.Platform = NormalizeSourcePlatform(request.Platform) ?? SocialPostPlatform.Facebook;

            if (string.IsNullOrWhiteSpace(request.UserId)
                || string.IsNullOrWhiteSpace(request.PageId))
            {
                return BadRequest(new { success = false, message = "User ID and Facebook Page are required." });
            }

            var page = await _context.FacebookPages
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == request.UserId && item.PageId == request.PageId, cancellationToken);

            if (page is null)
            {
                return BadRequest(new { success = false, message = "Connect this Facebook Page before creating Reel upload jobs." });
            }

            FacebookPostUrl? scrapedPost = null;
            if (request.FacebookPostUrlId.HasValue)
            {
                scrapedPost = await _context.FacebookPostUrls
                    .FirstOrDefaultAsync(
                        post => post.UserId == request.UserId
                            && post.Id == request.FacebookPostUrlId.Value
                            && post.PageId == request.PageId,
                        cancellationToken);

                if (scrapedPost is null)
                {
                    return BadRequest(new { success = false, message = "Selected scraped reel was not found for this user." });
                }

                if (scrapedPost.Platform != request.Platform
                    || scrapedPost.S3UploadStatus is not (FacebookPostS3UploadStatus.Downloaded or FacebookPostS3UploadStatus.Uploaded)
                    || string.IsNullOrWhiteSpace(scrapedPost.S3Key)
                    || !HasExistingLocalDownload(scrapedPost))
                {
                    if (scrapedPost.Platform == request.Platform)
                    {
                        MarkLocalDownloadMissing(scrapedPost);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    return BadRequest(new { success = false, message = $"Only locally downloaded {request.Platform} videos can be queued for publishing." });
                }
            }

            var videoSourceUrl = FirstNonEmpty(
                request.VideoUrl,
                scrapedPost?.VideoUrl,
                scrapedPost?.PermalinkUrl);

            if (string.IsNullOrWhiteSpace(videoSourceUrl)
                || !Uri.TryCreate(videoSourceUrl, UriKind.Absolute, out var parsedVideoUrl)
                || (parsedVideoUrl.Scheme != Uri.UriSchemeHttp && parsedVideoUrl.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest(new { success = false, message = "A valid direct video URL or scraped reel URL is required." });
            }

            var caption = FirstNonEmpty(request.Caption, scrapedPost?.Caption);
            var job = new FacebookReelUploadJob
            {
                UserId = request.UserId,
                PageId = request.PageId,
                PageName = page.PageName,
                FacebookPostUrlId = scrapedPost?.Id,
                VideoSourceUrl = videoSourceUrl,
                Caption = caption,
                S3Key = scrapedPost?.S3Key,
                S3Bucket = scrapedPost?.S3Bucket,
                S3Region = scrapedPost?.S3Region,
                Status = FacebookReelUploadJobStatus.Queued,
                GraphApiVersion = request.GraphApiVersion,
                ScheduledFor = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RetainUntil = DateTime.UtcNow.AddDays(7)
            };

            _context.FacebookReelUploadJobs.Add(job);
            await _context.SaveChangesAsync(cancellationToken);

            await _queue.QueueAsync(new FacebookReelUploadWorkItem(job.Id, request), cancellationToken);

            return Accepted(ToResponse(job));
        }

        [HttpPost("batch")]
        public async Task<ActionResult<FacebookReelUploadBatchResponse>> CreateBatch(
            [FromBody] CreateFacebookReelUploadBatchRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.PageId = request.PageId.Trim();
            request.GraphApiVersion = NormalizeGraphApiVersion(request.GraphApiVersion);
            request.Platform = NormalizeSourcePlatform(request.Platform) ?? SocialPostPlatform.Facebook;

            _logger.LogInformation("CreateBatch request received. UserId: {UserId}, PageId: {PageId}, Platform: {Platform}, DailyPostCount: {DailyPostCount}, StartAt: {StartAt}, IncludeQueued: {IncludeQueued}",
                request.UserId, request.PageId, request.Platform, request.DailyPostCount, request.StartAt, request.IncludeQueued);

            if (string.IsNullOrWhiteSpace(request.UserId)
                || string.IsNullOrWhiteSpace(request.PageId))
            {
                _logger.LogWarning("CreateBatch: User ID or Page ID is missing.");
                return BadRequest(new { success = false, message = "User ID and Facebook Page are required." });
            }

            request.DailyPostCount = Math.Clamp(request.DailyPostCount, 1, 48);
            var startAt = NormalizeUtc(request.StartAt ?? DateTime.UtcNow);
            var interval = TimeSpan.FromHours(24d / request.DailyPostCount);
            var dailyTimes = ParseDailyTimes(request.DailyTimes);

            if (interval < TimeSpan.FromMinutes(5))
            {
                _logger.LogWarning("CreateBatch: Interval {Interval} is too short.", interval);
                return BadRequest(new { success = false, message = "Daily post count is too high for a safe publishing interval." });
            }

            if (dailyTimes.Count > 0 && dailyTimes.Count != request.DailyPostCount)
            {
                return BadRequest(new { success = false, message = $"Enter exactly {request.DailyPostCount} daily publish time(s)." });
            }

            var page = await _context.FacebookPages
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == request.UserId && item.PageId == request.PageId, cancellationToken);

            if (page is null)
            {
                _logger.LogWarning("CreateBatch: FacebookPage not found for UserId: {UserId} and PageId: {PageId}", request.UserId, request.PageId);
                return BadRequest(new { success = false, message = "Connect this Facebook Page before creating Reel upload jobs." });
            }
            
            _logger.LogInformation("CreateBatch: Found page {PageName}. Starting job creation.", page.PageName);

            var matchedPosts = await _context.FacebookPostUrls
                .Where(post => post.UserId == request.UserId)
                .Where(post => post.PageId == request.PageId)
                .Where(post => post.Platform == request.Platform)
                .Where(post => (post.S3UploadStatus == FacebookPostS3UploadStatus.Downloaded
                        || post.S3UploadStatus == FacebookPostS3UploadStatus.Uploaded)
                    && post.S3Key != null
                    && post.S3Key != "")
                .OrderByDescending(post => post.PostCreatedAt ?? post.ScrapedAt)
                .Take(500)
                .ToListAsync(cancellationToken);

            var missingLocalFileCount = 0;
            var availablePosts = new List<FacebookPostUrl>();
            foreach (var post in matchedPosts)
            {
                if (HasExistingLocalDownload(post))
                {
                    availablePosts.Add(post);
                    continue;
                }

                MarkLocalDownloadMissing(post);
                missingLocalFileCount++;
            }

            if (missingLocalFileCount > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            matchedPosts = availablePosts;

            var matchedPostIds = matchedPosts.Select(post => post.Id).ToList();
            var existingJobs = matchedPostIds.Count == 0
                ? new List<FacebookReelUploadJob>()
                : await _context.FacebookReelUploadJobs
                    .Where(job => job.UserId == request.UserId
                        && job.PageId == request.PageId
                        && job.FacebookPostUrlId.HasValue
                        && matchedPostIds.Contains(job.FacebookPostUrlId.Value)
                        && job.Status != FacebookReelUploadJobStatus.Failed)
                    .OrderByDescending(job => job.CreatedAt)
                    .ToListAsync(cancellationToken);

            var existingJobsByPostId = existingJobs
                .GroupBy(job => job.FacebookPostUrlId!.Value)
                .ToDictionary(group => group.Key, group => group.First());

            var jobs = new List<FacebookReelUploadJob>();
            var rescheduledJobs = new List<FacebookReelUploadJob>();
            var skippedCount = 0;
            var scheduleIndex = 0;
            foreach (var post in matchedPosts)
            {
                if (existingJobsByPostId.TryGetValue(post.Id, out var existingJob))
                {
                    if (request.IncludeQueued && existingJob.Status == FacebookReelUploadJobStatus.Queued)
                    {
                        existingJob.ScheduledFor = BuildScheduledFor(
                            startAt,
                            interval,
                            scheduleIndex,
                            dailyTimes,
                            request.TimezoneOffsetMinutes);
                        existingJob.GraphApiVersion = request.GraphApiVersion;
                        existingJob.ErrorMessage = null;
                        existingJob.UpdatedAt = DateTime.UtcNow;
                        rescheduledJobs.Add(existingJob);
                        scheduleIndex++;
                        continue;
                    }

                    skippedCount++;
                    continue;
                }

                var videoSourceUrl = FirstNonEmpty(post.VideoUrl, post.PermalinkUrl);
                if (string.IsNullOrWhiteSpace(videoSourceUrl)
                    || !Uri.TryCreate(videoSourceUrl, UriKind.Absolute, out var parsedVideoUrl)
                    || (parsedVideoUrl.Scheme != Uri.UriSchemeHttp && parsedVideoUrl.Scheme != Uri.UriSchemeHttps))
                {
                    skippedCount++;
                    continue;
                }

                var scheduledFor = BuildScheduledFor(
                    startAt,
                    interval,
                    scheduleIndex,
                    dailyTimes,
                    request.TimezoneOffsetMinutes);
                scheduleIndex++;

                jobs.Add(new FacebookReelUploadJob
                {
                    UserId = request.UserId,
                    PageId = request.PageId,
                    PageName = page.PageName,
                    FacebookPostUrlId = post.Id,
                    VideoSourceUrl = videoSourceUrl,
                    Caption = post.Caption,
                    S3Key = post.S3Key,
                    S3Bucket = post.S3Bucket,
                    S3Region = post.S3Region,
                    Status = FacebookReelUploadJobStatus.Queued,
                    GraphApiVersion = request.GraphApiVersion,
                    ScheduledFor = scheduledFor,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    RetainUntil = DateTime.UtcNow.AddDays(7)
                });
            }

            if (jobs.Count > 0)
            {
                _context.FacebookReelUploadJobs.AddRange(jobs);
            }

            if (jobs.Count > 0 || rescheduledJobs.Count > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);

                foreach (var job in jobs)
                {
                    await _queue.QueueAsync(
                        new FacebookReelUploadWorkItem(job.Id, new CreateFacebookReelUploadJobRequest
                        {
                            UserId = request.UserId,
                            PageId = request.PageId,
                            FacebookPostUrlId = job.FacebookPostUrlId,
                            GraphApiVersion = request.GraphApiVersion,
                            Platform = request.Platform
                        }),
                        cancellationToken);
                }
            }

            var affectedJobs = jobs.Concat(rescheduledJobs).ToList();

            return Accepted(new FacebookReelUploadBatchResponse
            {
                Success = true,
                MatchedCount = matchedPosts.Count,
                QueuedCount = affectedJobs.Count,
                SkippedCount = skippedCount + missingLocalFileCount,
                IntervalHours = interval.TotalHours,
                Jobs = affectedJobs
                    .OrderBy(job => job.ScheduledFor ?? job.CreatedAt)
                    .Select(ToResponse)
                    .ToList(),
                Message = affectedJobs.Count == 0
                    ? $"No new {request.Platform} videos were queued. Matched {matchedPosts.Count}, skipped {skippedCount + missingLocalFileCount}."
                    : $"{(request.IncludeQueued ? "Queued/rescheduled" : "Queued")} {affectedJobs.Count} {request.Platform} video(s) using {request.DailyPostCount} daily time slot(s). Skipped {skippedCount + missingLocalFileCount}."
            });
        }

        private static FacebookReelUploadJobResponse ToResponse(FacebookReelUploadJob job)
        {
            return new FacebookReelUploadJobResponse
            {
                Id = job.Id,
                UserId = job.UserId,
                PageId = job.PageId,
                PageName = job.PageName,
                FacebookPostUrlId = job.FacebookPostUrlId,
                VideoSourceUrl = job.VideoSourceUrl,
                Caption = job.Caption,
                Status = job.Status,
                S3Bucket = job.S3Bucket,
                S3Region = job.S3Region,
                S3EndpointUrl = job.S3EndpointUrl,
                S3Key = job.S3Key,
                GraphApiVersion = job.GraphApiVersion,
                FacebookVideoId = job.FacebookVideoId,
                FacebookPostId = job.FacebookPostId,
                ErrorMessage = job.ErrorMessage,
                Attempts = job.Attempts,
                ScheduledFor = job.ScheduledFor,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                RetainUntil = job.RetainUntil
            };
        }

        private static string NormalizeGraphApiVersion(string? graphApiVersion)
        {
            graphApiVersion = graphApiVersion?.Trim();
            if (string.IsNullOrWhiteSpace(graphApiVersion))
            {
                return "v24.0";
            }

            return graphApiVersion.StartsWith('v') ? graphApiVersion : $"v{graphApiVersion}";
        }

        private static string? NormalizeSourcePlatform(string? platform)
        {
            platform = platform?.Trim();
            if (string.IsNullOrWhiteSpace(platform))
            {
                return null;
            }

            if (platform.Equals(SocialPostPlatform.Facebook, StringComparison.OrdinalIgnoreCase))
            {
                return SocialPostPlatform.Facebook;
            }

            if (platform.Equals(SocialPostPlatform.TikTok, StringComparison.OrdinalIgnoreCase))
            {
                return SocialPostPlatform.TikTok;
            }

            if (platform.Equals(SocialPostPlatform.RedNote, StringComparison.OrdinalIgnoreCase))
            {
                return SocialPostPlatform.RedNote;
            }

            return null;
        }

        private static DateTime BuildScheduledFor(
            DateTime startAtUtc,
            TimeSpan fallbackInterval,
            int scheduleIndex,
            IReadOnlyList<TimeSpan> dailyTimes,
            int timezoneOffsetMinutes)
        {
            if (dailyTimes.Count == 0)
            {
                return startAtUtc.AddTicks(fallbackInterval.Ticks * scheduleIndex);
            }

            var localStartAt = startAtUtc.AddMinutes(-timezoneOffsetMinutes);
            var scheduledLocal = GetDailySlot(localStartAt, dailyTimes, scheduleIndex);

            return DateTime.SpecifyKind(scheduledLocal.AddMinutes(timezoneOffsetMinutes), DateTimeKind.Utc);
        }

        private static DateTime GetDailySlot(DateTime localStartAt, IReadOnlyList<TimeSpan> dailyTimes, int scheduleIndex)
        {
            var remaining = scheduleIndex;
            var currentDate = localStartAt.Date;

            while (true)
            {
                foreach (var dailyTime in dailyTimes)
                {
                    var candidate = currentDate.Add(dailyTime);
                    if (candidate < localStartAt)
                    {
                        continue;
                    }

                    if (remaining == 0)
                    {
                        return candidate;
                    }

                    remaining--;
                }

                currentDate = currentDate.AddDays(1);
            }
        }

        private static List<TimeSpan> ParseDailyTimes(IEnumerable<string>? values)
        {
            if (values is null)
            {
                return new List<TimeSpan>();
            }

            var parsedTimes = new List<TimeSpan>();
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!TimeSpan.TryParse(value.Trim(), out var parsedTime)
                    || parsedTime < TimeSpan.Zero
                    || parsedTime >= TimeSpan.FromDays(1))
                {
                    continue;
                }

                parsedTimes.Add(new TimeSpan(parsedTime.Hours, parsedTime.Minutes, 0));
            }

            return parsedTimes
                .Distinct()
                .OrderBy(time => time)
                .ToList();
        }

        private bool HasExistingLocalDownload(FacebookPostUrl post)
        {
            if (post.S3UploadStatus is not (FacebookPostS3UploadStatus.Downloaded or FacebookPostS3UploadStatus.Uploaded)
                || string.IsNullOrWhiteSpace(post.S3Key))
            {
                return false;
            }

            try
            {
                return System.IO.File.Exists(_storage.GetAbsolutePath(post.S3Key));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid local storage key for post {PostId}.", post.Id);
                return false;
            }
        }

        private static void MarkLocalDownloadMissing(FacebookPostUrl post)
        {
            post.S3UploadStatus = FacebookPostS3UploadStatus.NotUploaded;
            post.S3Bucket = null;
            post.S3Region = null;
            post.S3Key = null;
            post.S3UploadedAt = null;
            post.S3UploadError = "Local video file is missing. Download it again.";
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
                : value.ToUniversalTime();
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }
    }
}
