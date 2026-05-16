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
    public class PagesController : ControllerBase
    {
        private readonly SmapiDbContext _context;
        private readonly IApifyFacebookPostsClient _apifyFacebookPostsClient;
        private readonly ILocalVideoStorageService _storage;
        private readonly ILogger<PagesController> _logger;

        public PagesController(
            SmapiDbContext context,
            IApifyFacebookPostsClient apifyFacebookPostsClient,
            ILocalVideoStorageService storage,
            ILogger<PagesController> logger)
        {
            _context = context;
            _apifyFacebookPostsClient = apifyFacebookPostsClient;
            _storage = storage;
            _logger = logger;
        }

        [HttpGet("facebook/by-page/{pageId}")]
        public async Task<ActionResult<FacebookPage>> GetFacebookPageByPageId(string pageId)
        {
            pageId = pageId.Trim();
            var page = await _context.FacebookPages
                .AsNoTracking()
                .OrderByDescending(item => item.ConnectedAt)
                .FirstOrDefaultAsync(item => item.PageId == pageId);

            if (page is null)
            {
                return NotFound(new { success = false, message = "Facebook page was not found." });
            }

            return Ok(page);
        }

        [HttpGet("facebook/{userId}")]
        public async Task<ActionResult<IEnumerable<FacebookPage>>> GetFacebookPages(string userId)
        {
            return await _context.FacebookPages
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.ConnectedAt)
                .ToListAsync();
        }

        [HttpGet("facebook/posts/{userId}")]
        public async Task<ActionResult<IEnumerable<FacebookPostUrlResponse>>> GetFacebookPostUrls(
            string userId,
            [FromQuery] string? pageId)
        {
            userId = userId.Trim();
            pageId = string.IsNullOrWhiteSpace(pageId) ? null : pageId.Trim();

            var query = _context.FacebookPostUrls
                .Where(post => post.UserId == userId);

            if (!string.IsNullOrWhiteSpace(pageId))
            {
                query = query.Where(post => post.PageId == pageId);
            }

            return await query
                .OrderByDescending(post => post.ScrapedAt)
                .Take(200)
                .Select(post => new FacebookPostUrlResponse
                {
                    Id = post.Id,
                    PermalinkUrl = post.PermalinkUrl,
                    PostId = post.PostId,
                    PageId = post.PageId,
                    SourcePageUrl = post.SourcePageUrl,
                    VideoUrl = post.VideoUrl,
                    PostCreatedAt = post.PostCreatedAt,
                    Caption = post.Caption,
                    S3UploadStatus = post.S3UploadStatus,
                    S3Bucket = post.S3Bucket,
                    S3Region = post.S3Region,
                    S3Key = post.S3Key,
                    S3UploadedAt = post.S3UploadedAt,
                    S3UploadError = post.S3UploadError,
                    ScrapedAt = post.ScrapedAt
                })
                .ToListAsync();
        }

        [HttpPost("facebook/scrape")]
        public async Task<ActionResult<FacebookScrapeResponse>> ScrapeFacebookPosts(
            [FromBody] FacebookScrapeRequest request,
            CancellationToken cancellationToken)
        {
            var validStartUrls = request.StartUrls
                .Where(item => Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                .Select(item => item.Url)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validStartUrls.Count == 0)
            {
                return BadRequest(new { success = false, message = "At least one valid Facebook page URL is required." });
            }

            request.StartUrls = validStartUrls.Select(url => new FacebookStartUrl { Url = url }).ToList();
            request.ResultsLimit = Math.Clamp(request.ResultsLimit, 1, 1000);
            request.UserId = string.IsNullOrWhiteSpace(request.UserId) ? "user-123" : request.UserId.Trim();
            request.PageId = string.IsNullOrWhiteSpace(request.PageId) ? null : request.PageId.Trim();

            IReadOnlyList<ApifyFacebookPostItem> scrapedItems;
            try
            {
                scrapedItems = await _apifyFacebookPostsClient.ScrapePostsAsync(request, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Facebook scrape failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = $"Facebook scrape failed: {TrimForClient(ex.Message)}"
                });
            }

            var scrapedPosts = scrapedItems
                .Select(item => new
                {
                    Item = item,
                    Url = NormalizeUrl(item.GetPermalinkUrl())
                })
                .Where(post => !string.IsNullOrWhiteSpace(post.Url))
                .GroupBy(post => post.Url!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var urls = scrapedPosts.Select(post => post.Url!).ToList();
            var existingPostsQuery = _context.FacebookPostUrls
                .Where(post => post.UserId == request.UserId && urls.Contains(post.PermalinkUrl));

            if (!string.IsNullOrWhiteSpace(request.PageId))
            {
                existingPostsQuery = existingPostsQuery
                    .Where(post => post.PageId == request.PageId || post.PageId == null);
            }

            var existingPosts = (await existingPostsQuery
                    .OrderByDescending(post => post.PageId == request.PageId)
                    .ToListAsync(cancellationToken))
                .GroupBy(post => post.PermalinkUrl, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var savedCount = 0;
            var updatedCount = 0;
            var changedPosts = new List<FacebookPostUrl>();


            foreach (var scrapedPost in scrapedPosts)
            {
                var item = scrapedPost.Item;
                var permalinkUrl = scrapedPost.Url!;

                if (existingPosts.TryGetValue(permalinkUrl, out var existingPost))
                {
                    _logger.LogInformation("Scrape: Found existing post {PostId}. Current status: {Status}", existingPost.Id, existingPost.S3UploadStatus);
                    
                    existingPost.PostId = FirstNonEmpty(item.GetItemId(), existingPost.PostId);
                    existingPost.PageId = FirstNonEmpty(existingPost.PageId, request.PageId);
                    existingPost.SourcePageUrl = FirstNonEmpty(NormalizeUrl(item.GetSourcePageUrl()), existingPost.SourcePageUrl);
                    existingPost.VideoUrl = FirstNonEmpty(item.GetVideoUrl(), existingPost.VideoUrl);
                    existingPost.PostCreatedAt = item.GetCreatedAt() ?? existingPost.PostCreatedAt;
                    existingPost.Caption = FirstNonEmpty(item.GetCaption(), existingPost.Caption);
                    existingPost.ScrapedAt = DateTime.UtcNow;

                    if (existingPost.S3UploadStatus == "Downloaded" && !string.IsNullOrEmpty(existingPost.S3Key))
                    {
                        try {
                            var path = _storage.GetAbsolutePath(existingPost.S3Key);
                            if (!System.IO.File.Exists(path))
                            {
                                _logger.LogInformation("Scrape: File missing for post {PostId} at {Path}. Resetting status to NotUploaded.", existingPost.Id, path);
                                existingPost.S3UploadStatus = "NotUploaded";
                                existingPost.S3Key = null;
                            }
                        } catch { }
                    }

                    updatedCount++;
                    changedPosts.Add(existingPost);
                    continue;
                }
                else
                {
                    _logger.LogInformation("Scrape: Creating new post record for {Permalink}", permalinkUrl);
                }

                var newPost = new FacebookPostUrl
                {
                    PermalinkUrl = permalinkUrl,
                    PostId = item.GetItemId(),
                    PageId = request.PageId,
                    SourcePageUrl = NormalizeUrl(item.GetSourcePageUrl()),
                    VideoUrl = item.GetVideoUrl(),
                    PostCreatedAt = item.GetCreatedAt(),
                    Caption = item.GetCaption(),
                    ScrapedAt = DateTime.UtcNow,
                    UserId = request.UserId
                };

                _context.FacebookPostUrls.Add(newPost);
                existingPosts[permalinkUrl] = newPost;
                savedCount++;
                changedPosts.Add(newPost);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new FacebookScrapeResponse
            {
                Success = true,
                ScrapedCount = scrapedItems.Count,
                SavedCount = savedCount,
                UpdatedCount = updatedCount,
                SkippedCount = scrapedItems.Count - scrapedPosts.Count,
                Posts = changedPosts
                    .OrderByDescending(post => post.ScrapedAt)
                    .Select(ToResponse)
                    .ToList()
            });
        }

        [HttpPost("facebook/connect")]
        public async Task<IActionResult> ConnectFacebookPage([FromBody] FacebookPage page)
        {
            if (string.IsNullOrEmpty(page.PageId) || string.IsNullOrEmpty(page.AccessToken))
            {
                return BadRequest(new { success = false, message = "Page ID and Access Token are required." });
            }

            page.PageId = page.PageId.Trim();
            page.PageName = page.PageName.Trim();
            page.UserId = string.IsNullOrWhiteSpace(page.UserId) ? "user-123" : page.UserId.Trim();

            var existingPage = await _context.FacebookPages.FirstOrDefaultAsync(
                p => p.UserId == page.UserId && p.PageId == page.PageId);
            
            if (existingPage != null)
            {
                existingPage.AccessToken = page.AccessToken;
                existingPage.PageName = page.PageName;
                existingPage.Category = string.IsNullOrWhiteSpace(page.Category) ? null : page.Category.Trim();
                existingPage.AvatarUrl = string.IsNullOrWhiteSpace(page.AvatarUrl) ? null : page.AvatarUrl.Trim();
                existingPage.ConnectedAt = DateTime.UtcNow;
                _context.FacebookPages.Update(existingPage);
            }
            else
            {
                _context.FacebookPages.Add(page);
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Facebook page connected successfully." });
        }

        [HttpPut("facebook/{id}")]
        public async Task<IActionResult> UpdateFacebookPage(int id, [FromBody] FacebookPage page)
        {
            _logger.LogInformation("UpdateFacebookPage request received for ID: {Id}. UserId: {UserId}, PageId: {PageId}", 
                id, page.UserId, page.PageId);

            if (string.IsNullOrWhiteSpace(page.UserId)
                || string.IsNullOrWhiteSpace(page.PageId)
                || string.IsNullOrWhiteSpace(page.PageName)
                || string.IsNullOrWhiteSpace(page.AccessToken))
            {
                _logger.LogWarning("UpdateFacebookPage: Missing required fields.");
                return BadRequest(new { success = false, message = "User ID, Page ID, Page Name and Access Token are required." });
            }

            var existingPage = await _context.FacebookPages.FindAsync(id);
            if (existingPage == null)
            {
                return NotFound(new { success = false, message = "Facebook page was not found." });
            }

            page.UserId = page.UserId.Trim();
            page.PageId = page.PageId.Trim();
            page.PageName = page.PageName.Trim();

            var duplicateExists = await _context.FacebookPages.AnyAsync(
                item => item.Id != id && item.UserId == page.UserId && item.PageId == page.PageId);

            if (duplicateExists)
            {
                return Conflict(new { success = false, message = "This Facebook Page ID is already connected for this user." });
            }

            existingPage.UserId = page.UserId;
            existingPage.PageId = page.PageId;
            existingPage.PageName = page.PageName;
            existingPage.AccessToken = page.AccessToken;
            existingPage.Category = string.IsNullOrWhiteSpace(page.Category) ? null : page.Category.Trim();
            existingPage.AvatarUrl = string.IsNullOrWhiteSpace(page.AvatarUrl) ? null : page.AvatarUrl.Trim();
            existingPage.ConnectedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Facebook page updated successfully." });
        }

        [HttpDelete("facebook/{id}")]
        public async Task<IActionResult> DeleteFacebookPage(int id)
        {
            var page = await _context.FacebookPages.FindAsync(id);
            if (page == null)
            {
                return NotFound();
            }

            _context.FacebookPages.Remove(page);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Facebook page disconnected successfully." });
        }

        [HttpDelete("facebook/posts/{id}")]
        public async Task<IActionResult> DeleteFacebookPost(int id)
        {
            var post = await _context.FacebookPostUrls.FindAsync(id);
            if (post == null)
            {
                return NotFound(new { success = false, message = "Post not found." });
            }

            if (!string.IsNullOrWhiteSpace(post.S3Key))
            {
                try
                {
                    var localPath = _storage.GetAbsolutePath(post.S3Key);
                    _logger.LogInformation("Attempting to delete local video file for post {PostId} at path: {LocalPath}", id, localPath);
                    
                    if (System.IO.File.Exists(localPath))
                    {
                        System.IO.File.Delete(localPath);
                        _logger.LogInformation("Successfully deleted local video file for post {PostId}.", id);
                    }
                    else
                    {
                        _logger.LogWarning("Local video file for post {PostId} was not found at path: {LocalPath}", id, localPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete local video file for post {PostId} at path: {LocalPath}", id, post.S3Key);
                }
            }
            else
            {
                _logger.LogInformation("No S3Key (local path) found for post {PostId}, skipping file deletion.", id);
            }

            _context.FacebookPostUrls.Remove(post);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Facebook post deleted successfully." });
        }

        [HttpDelete("facebook/posts/all/{userId}/{pageId}")]
        public async Task<IActionResult> DeleteAllFacebookPosts(string userId, string pageId)
        {
            userId = userId.Trim();
            pageId = pageId.Trim();

            var posts = await _context.FacebookPostUrls
                .Where(p => p.UserId == userId && p.PageId == pageId)
                .ToListAsync();

            if (posts.Count == 0)
            {
                return Ok(new { success = true, message = "No posts found to delete.", deletedCount = 0 });
            }

            foreach (var post in posts)
            {
                _logger.LogInformation("Processing bulk delete for post {PostId}. S3Key: {S3Key}", post.Id, post.S3Key);
                if (!string.IsNullOrWhiteSpace(post.S3Key))
                {
                    try
                    {
                        var localPath = _storage.GetAbsolutePath(post.S3Key);
                        _logger.LogInformation("Attempting to delete local file for post {PostId} at: {Path}", post.Id, localPath);
                        if (System.IO.File.Exists(localPath))
                        {
                            System.IO.File.Delete(localPath);
                            _logger.LogInformation("Successfully deleted local file for post {PostId}", post.Id);
                        }
                        else
                        {
                            _logger.LogWarning("Local file for post {PostId} not found at: {Path}", post.Id, localPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete local video file for post {PostId}", post.Id);
                    }
                }
            }

            _logger.LogInformation("Removing {Count} post records from database for user {UserId} and page {PageId}", posts.Count, userId, pageId);
            _context.FacebookPostUrls.RemoveRange(posts);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Deleted {posts.Count} posts and their video files successfully.", deletedCount = posts.Count });
        }

        private static FacebookPostUrlResponse ToResponse(FacebookPostUrl post)
        {
            return new FacebookPostUrlResponse
            {
                Id = post.Id,
                PermalinkUrl = post.PermalinkUrl,
                PostId = post.PostId,
                PageId = post.PageId,
                SourcePageUrl = post.SourcePageUrl,
                VideoUrl = post.VideoUrl,
                PostCreatedAt = post.PostCreatedAt,
                Caption = post.Caption,
                S3UploadStatus = post.S3UploadStatus,
                S3Bucket = post.S3Bucket,
                S3Region = post.S3Region,
                S3Key = post.S3Key,
                S3UploadedAt = post.S3UploadedAt,
                S3UploadError = post.S3UploadError,
                ScrapedAt = post.ScrapedAt
            };
        }

        private static string? NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return url.Trim();
            }

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
                Query = string.Empty
            };

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string TrimForClient(string value)
        {
            value = value.Trim();
            return value.Length <= 500 ? value : value[..500];
        }
    }
}
