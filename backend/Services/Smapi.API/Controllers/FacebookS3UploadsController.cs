using Smapi.API.Data;
using Smapi.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Controllers
{
    public class FacebookS3UploadRequest
    {
        public string UserId { get; set; } = string.Empty;

        public string PageId { get; set; } = string.Empty;

        public List<int> PostIds { get; set; } = new();
    }

    [Route("api/[controller]")]
    [ApiController]
    public class FacebookS3UploadsController : ControllerBase
    {
        private readonly SmapiDbContext _context;
        private readonly IFacebookPostS3UploadQueue _queue;
        private readonly IFacebookPostS3DownloadCancellation _downloadCancellation;
        private readonly ILocalVideoStorageService _storage;

        public FacebookS3UploadsController(
            SmapiDbContext context,
            IFacebookPostS3UploadQueue queue,
            IFacebookPostS3DownloadCancellation downloadCancellation,
            ILocalVideoStorageService storage)
        {
            _context = context;
            _queue = queue;
            _downloadCancellation = downloadCancellation;
            _storage = storage;
        }

        [HttpPost("facebook/reels")]
        public async Task<IActionResult> QueueFacebookReelUploads(
            [FromBody] FacebookS3UploadRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.PageId = request.PageId.Trim();

            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.PageId))
            {
                return BadRequest(new { success = false, message = "User ID and Facebook Page ID are required." });
            }

            var postIds = request.PostIds.Distinct().ToList();
            var query = _context.FacebookPostUrls.Where(post => post.UserId == request.UserId);
            if (postIds.Count > 0)
            {
                query = query.Where(post => postIds.Contains(post.Id));
            }
            else
            {
                query = query.Where(post => post.PageId == request.PageId);
            }

            var posts = await query
                .OrderByDescending(post => post.ScrapedAt)
                .Take(200)
                .ToListAsync(cancellationToken);

            var queuedPostIds = new List<int>();
            var skippedCount = 0;
            foreach (var post in posts)
            {
                if (post.S3UploadStatus is (FacebookPostS3UploadStatus.Downloaded or FacebookPostS3UploadStatus.Uploaded)
                    && !HasExistingLocalDownload(post))
                {
                    MarkLocalDownloadMissing(post);
                }

                if (post.S3UploadStatus is FacebookPostS3UploadStatus.Queued
                    or FacebookPostS3UploadStatus.Downloading
                    or FacebookPostS3UploadStatus.Downloaded
                    or FacebookPostS3UploadStatus.Uploading
                    or FacebookPostS3UploadStatus.Uploaded)
                {
                    skippedCount++;
                    continue;
                }

                post.S3UploadStatus = FacebookPostS3UploadStatus.Queued;
                post.S3UploadError = null;
                queuedPostIds.Add(post.Id);
            }

            await _context.SaveChangesAsync(cancellationToken);

            foreach (var postId in queuedPostIds)
            {
                await _queue.QueueAsync(
                    new FacebookPostS3UploadWorkItem(postId, request.UserId, request.PageId),
                    cancellationToken);
            }

            return Accepted(new
            {
                success = true,
                queuedCount = queuedPostIds.Count,
                skippedCount,
                totalCount = posts.Count,
                message = queuedPostIds.Count == 0
                    ? "No new or failed videos were available for local download."
                    : $"Queued {queuedPostIds.Count} new or failed video(s) for local download. Skipped {skippedCount} already downloaded or running."
            });
        }

        [HttpPost("facebook/reels/stop")]
        public async Task<IActionResult> StopFacebookReelDownloads(
            [FromBody] FacebookS3UploadRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.PageId = request.PageId.Trim();

            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.PageId))
            {
                return BadRequest(new { success = false, message = "User ID and Facebook Page ID are required." });
            }

            var postIds = request.PostIds.Distinct().ToList();
            var query = _context.FacebookPostUrls.Where(post => post.UserId == request.UserId);
            if (postIds.Count > 0)
            {
                query = query.Where(post => postIds.Contains(post.Id));
            }
            else
            {
                query = query.Where(post => post.PageId == request.PageId);
            }

            var posts = await query
                .Where(post => post.S3UploadStatus == FacebookPostS3UploadStatus.Queued
                    || post.S3UploadStatus == FacebookPostS3UploadStatus.Downloading
                    || post.S3UploadStatus == FacebookPostS3UploadStatus.Uploading)
                .OrderByDescending(post => post.ScrapedAt)
                .Take(200)
                .ToListAsync(cancellationToken);

            var stoppedPostIds = posts.Select(post => post.Id).ToList();
            _downloadCancellation.Cancel(stoppedPostIds);

            foreach (var post in posts)
            {
                post.S3UploadStatus = FacebookPostS3UploadStatus.Cancelled;
                post.S3UploadError = "Download stopped by user.";
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                stoppedCount = stoppedPostIds.Count,
                message = stoppedPostIds.Count == 0
                    ? "No queued or active local downloads were found."
                    : $"Stopped {stoppedPostIds.Count} queued or active local download(s)."
            });
        }

        [HttpGet("facebook/reels/{postId:int}/url")]
        public async Task<IActionResult> GetUploadedVideoUrl(
            int postId,
            [FromQuery] string userId,
            [FromQuery] int expiresMinutes,
            CancellationToken cancellationToken)
        {
            userId = userId.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { success = false, message = "User ID is required." });
            }

            var post = await _context.FacebookPostUrls
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == postId && item.UserId == userId, cancellationToken);

            if (post is null)
            {
                return NotFound(new { success = false, message = "Uploaded video was not found." });
            }

            if (post.S3UploadStatus is not (FacebookPostS3UploadStatus.Downloaded or FacebookPostS3UploadStatus.Uploaded)
                || string.IsNullOrWhiteSpace(post.S3Key))
            {
                return BadRequest(new { success = false, message = "This reel has not been downloaded locally yet." });
            }

            var lifetimeMinutes = Math.Clamp(expiresMinutes <= 0 ? 60 : expiresMinutes, 1, 1440);
            var expiresAt = DateTime.UtcNow.AddMinutes(lifetimeMinutes);
            var url = await _storage.CreateReadUrlAsync(post.S3Key, cancellationToken);

            return Ok(new
            {
                success = true,
                url,
                expiresAt,
                localPath = _storage.GetAbsolutePath(post.S3Key)
            });
        }

        [HttpGet("local/{**storageKey}")]
        public IActionResult GetLocalVideo(string storageKey)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
            {
                return BadRequest(new { success = false, message = "Local video path is required." });
            }

            var localPath = _storage.GetAbsolutePath(storageKey);
            if (!System.IO.File.Exists(localPath))
            {
                return NotFound(new { success = false, message = "Local video file was not found." });
            }

            return PhysicalFile(localPath, "video/mp4", enableRangeProcessing: true);
        }

        private bool HasExistingLocalDownload(Models.FacebookPostUrl post)
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
            catch
            {
                return false;
            }
        }

        private static void MarkLocalDownloadMissing(Models.FacebookPostUrl post)
        {
            post.S3UploadStatus = FacebookPostS3UploadStatus.NotUploaded;
            post.S3Bucket = null;
            post.S3Region = null;
            post.S3Key = null;
            post.S3UploadedAt = null;
            post.S3UploadError = "Local video file is missing. Download it again.";
        }
    }
}
