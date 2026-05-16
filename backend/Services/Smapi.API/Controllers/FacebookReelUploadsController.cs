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
        private readonly ILogger<FacebookReelUploadsController> _logger;

        public FacebookReelUploadsController(
            SmapiDbContext context, 
            IFacebookReelUploadQueue queue,
            ILogger<FacebookReelUploadsController> logger)
        {
            _context = context;
            _queue = queue;
            _logger = logger;
        }

        [HttpGet("{userId}")]
        public async Task<ActionResult<IEnumerable<FacebookReelUploadJobResponse>>> GetJobs(
            string userId,
            [FromQuery] string? pageId,
            CancellationToken cancellationToken)
        {
            var query = _context.FacebookReelUploadJobs
                .AsNoTracking()
                .Where(job => job.UserId == userId);

            if (!string.IsNullOrWhiteSpace(pageId))
            {
                query = query.Where(job => job.PageId == pageId);
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

        [HttpPost]
        public async Task<ActionResult<FacebookReelUploadJobResponse>> CreateJob(
            [FromBody] CreateFacebookReelUploadJobRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.PageId = request.PageId.Trim();
            request.GraphApiVersion = NormalizeGraphApiVersion(request.GraphApiVersion);

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
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        post => post.UserId == request.UserId
                            && post.Id == request.FacebookPostUrlId.Value
                            && (post.PageId == request.PageId || post.PageId == null),
                        cancellationToken);

                if (scrapedPost is null)
                {
                    return BadRequest(new { success = false, message = "Selected scraped reel was not found for this user." });
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

            _logger.LogInformation("CreateBatch request received. UserId: {UserId}, PageId: {PageId}, DailyPostCount: {DailyPostCount}, StartAt: {StartAt}", 
                request.UserId, request.PageId, request.DailyPostCount, request.StartAt);

            if (string.IsNullOrWhiteSpace(request.UserId)
                || string.IsNullOrWhiteSpace(request.PageId))
            {
                _logger.LogWarning("CreateBatch: User ID or Page ID is missing.");
                return BadRequest(new { success = false, message = "User ID and Facebook Page are required." });
            }

            request.DailyPostCount = Math.Clamp(request.DailyPostCount, 1, 48);
            var startAt = NormalizeUtc(request.StartAt ?? DateTime.UtcNow);
            var interval = TimeSpan.FromHours(24d / request.DailyPostCount);

            if (interval < TimeSpan.FromMinutes(5))
            {
                _logger.LogWarning("CreateBatch: Interval {Interval} is too short.", interval);
                return BadRequest(new { success = false, message = "Daily post count is too high for a safe publishing interval." });
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
                .AsNoTracking()
                .Where(post => post.UserId == request.UserId)
                .Where(post => post.PageId == request.PageId)
                .OrderByDescending(post => post.PostCreatedAt ?? post.ScrapedAt)
                .Take(500)
                .ToListAsync(cancellationToken);

            var matchedPostIds = matchedPosts.Select(post => post.Id).ToList();
            var alreadyQueuedPostIds = matchedPostIds.Count == 0
                ? new HashSet<int>()
                : await _context.FacebookReelUploadJobs
                    .Where(job => job.UserId == request.UserId
                        && job.PageId == request.PageId
                        && job.FacebookPostUrlId.HasValue
                        && matchedPostIds.Contains(job.FacebookPostUrlId.Value)
                        && job.Status != FacebookReelUploadJobStatus.Failed)
                    .Select(job => job.FacebookPostUrlId!.Value)
                    .ToHashSetAsync(cancellationToken);

            var jobs = new List<FacebookReelUploadJob>();
            var skippedCount = 0;
            var scheduleIndex = 0;
            foreach (var post in matchedPosts)
            {
                if (alreadyQueuedPostIds.Contains(post.Id))
                {
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

                var scheduledFor = startAt.AddTicks(interval.Ticks * scheduleIndex);
                scheduleIndex++;

                jobs.Add(new FacebookReelUploadJob
                {
                    UserId = request.UserId,
                    PageId = request.PageId,
                    PageName = page.PageName,
                    FacebookPostUrlId = post.Id,
                    VideoSourceUrl = videoSourceUrl,
                    Caption = post.Caption,
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
                await _context.SaveChangesAsync(cancellationToken);

                foreach (var job in jobs)
                {
                    await _queue.QueueAsync(
                        new FacebookReelUploadWorkItem(job.Id, new CreateFacebookReelUploadJobRequest
                        {
                            UserId = request.UserId,
                            PageId = request.PageId,
                            FacebookPostUrlId = job.FacebookPostUrlId,
                            GraphApiVersion = request.GraphApiVersion
                        }),
                        cancellationToken);
                }
            }

            return Accepted(new FacebookReelUploadBatchResponse
            {
                Success = true,
                MatchedCount = matchedPosts.Count,
                QueuedCount = jobs.Count,
                SkippedCount = skippedCount,
                IntervalHours = interval.TotalHours,
                Jobs = jobs.Select(ToResponse).ToList(),
                Message = jobs.Count == 0
                    ? $"No new reels were queued. Matched {matchedPosts.Count}, skipped {skippedCount}."
                    : $"Queued {jobs.Count} reel(s) at every {interval.TotalHours:0.##} hour(s). Skipped {skippedCount}."
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
