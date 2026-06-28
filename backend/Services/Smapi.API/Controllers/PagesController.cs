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
        private readonly IApifyTikTokPostsClient _apifyTikTokPostsClient;
        private readonly ILocalVideoStorageService _storage;
        private readonly IFacebookPostS3UploadQueue _downloadQueue;
        private readonly IGeminiCaptionGenerator _captionGenerator;
        private readonly ILogger<PagesController> _logger;

        public PagesController(
            SmapiDbContext context,
            IApifyFacebookPostsClient apifyFacebookPostsClient,
            IApifyTikTokPostsClient apifyTikTokPostsClient,
            ILocalVideoStorageService storage,
            IFacebookPostS3UploadQueue downloadQueue,
            IGeminiCaptionGenerator captionGenerator,
            ILogger<PagesController> logger)
        {
            _context = context;
            _apifyFacebookPostsClient = apifyFacebookPostsClient;
            _apifyTikTokPostsClient = apifyTikTokPostsClient;
            _storage = storage;
            _downloadQueue = downloadQueue;
            _captionGenerator = captionGenerator;
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

        [HttpGet("facebook")]
        public async Task<ActionResult<IEnumerable<FacebookPage>>> GetAllFacebookPages(
            [FromQuery] string? userId,
            CancellationToken cancellationToken)
        {
            userId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();

            var query = _context.FacebookPages.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(p => p.UserId == userId);
            }

            return await query
                .OrderByDescending(p => p.ConnectedAt)
                .ToListAsync(cancellationToken);
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
            [FromQuery] string? pageId,
            [FromQuery] string? platform,
            [FromQuery] bool downloadedOnly = false,
            CancellationToken cancellationToken = default)
        {
            userId = userId.Trim();
            pageId = string.IsNullOrWhiteSpace(pageId) ? null : pageId.Trim();
            var normalizedPlatform = NormalizeSourcePlatform(platform);

            var query = _context.FacebookPostUrls
                .Where(post => post.UserId == userId);

            if (!string.IsNullOrWhiteSpace(pageId))
            {
                query = query.Where(post => post.PageId == pageId);
            }

            if (!string.IsNullOrWhiteSpace(normalizedPlatform))
            {
                query = query.Where(post => post.Platform == normalizedPlatform);
            }
            else
            {
                query = query.Where(post => post.Platform != SocialPostPlatform.RedNote);
            }

            if (downloadedOnly)
            {
                query = query.Where(post => (post.S3UploadStatus == FacebookPostS3UploadStatus.Downloaded
                        || post.S3UploadStatus == FacebookPostS3UploadStatus.Uploaded)
                    && post.S3Key != null
                    && post.S3Key != "");
            }

            var posts = await query
                .OrderByDescending(post => post.ScrapedAt)
                .Take(200)
                .ToListAsync(cancellationToken);

            if (downloadedOnly)
            {
                var changed = false;
                var availablePosts = new List<FacebookPostUrl>();
                foreach (var post in posts)
                {
                    if (HasExistingLocalDownload(post))
                    {
                        availablePosts.Add(post);
                        continue;
                    }

                    MarkLocalDownloadMissing(post);
                    changed = true;
                }

                if (changed)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }

                posts = availablePosts;
            }

            return Ok(posts.Select(ToResponse).ToList());
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
            request.PageId = request.PageId?.Trim();

            if (string.IsNullOrWhiteSpace(request.PageId))
            {
                return BadRequest(new { success = false, message = "Facebook Page ID is required for page-scoped scraping." });
            }

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
                .Where(post => post.UserId == request.UserId
                    && post.PageId == request.PageId
                    && post.Platform == SocialPostPlatform.Facebook
                    && urls.Contains(post.PermalinkUrl));

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
                    
                    existingPost.Platform = SocialPostPlatform.Facebook;
                    existingPost.PostId = FirstNonEmpty(item.GetItemId(), existingPost.PostId);
                    existingPost.PageId = request.PageId;
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
                    Platform = SocialPostPlatform.Facebook,
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

        [HttpPost("tiktok/scrape")]
        public async Task<ActionResult<FacebookScrapeResponse>> ScrapeTikTokPosts(
            [FromBody] TikTokScrapeRequest request,
            CancellationToken cancellationToken)
        {
            var validProfiles = request.Profiles
                .Select(NormalizeTikTokProfileInput)
                .Where(profile => !string.IsNullOrWhiteSpace(profile))
                .Select(profile => profile!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validProfiles.Count == 0)
            {
                return BadRequest(new { success = false, message = "At least one valid TikTok profile URL is required." });
            }

            request.Profiles = validProfiles;
            request.ResultsPerPage = Math.Clamp(request.ResultsPerPage, 1, 1000);
            request.UserId = string.IsNullOrWhiteSpace(request.UserId) ? "user-123" : request.UserId.Trim();
            request.PageId = request.PageId?.Trim();

            if (string.IsNullOrWhiteSpace(request.PageId))
            {
                return BadRequest(new { success = false, message = "Facebook Page ID is required for page-scoped TikTok scraping." });
            }

            IReadOnlyList<ApifyTikTokPostItem> scrapedItems;
            try
            {
                scrapedItems = await _apifyTikTokPostsClient.ScrapePostsAsync(request, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TikTok scrape failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = $"TikTok scrape failed: {TrimForClient(ex.Message)}"
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
                .Where(post => post.UserId == request.UserId
                    && post.PageId == request.PageId
                    && post.Platform == SocialPostPlatform.TikTok
                    && urls.Contains(post.PermalinkUrl));

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
                    existingPost.Platform = SocialPostPlatform.TikTok;
                    existingPost.PostId = FirstNonEmpty(item.GetItemId(), existingPost.PostId);
                    existingPost.PageId = request.PageId;
                    existingPost.SourcePageUrl = FirstNonEmpty(NormalizeUrl(item.GetProfileUrl()), existingPost.SourcePageUrl);
                    existingPost.VideoUrl = FirstNonEmpty(item.GetVideoUrl(), existingPost.VideoUrl);
                    existingPost.PostCreatedAt = item.GetCreatedAt() ?? existingPost.PostCreatedAt;
                    existingPost.Caption = FirstNonEmpty(item.GetCaption(), existingPost.Caption);
                    existingPost.AuthorName = FirstNonEmpty(item.GetAuthorName(), existingPost.AuthorName);
                    existingPost.LikeCount = item.GetLikeCount() ?? existingPost.LikeCount;
                    existingPost.ShareCount = item.GetShareCount() ?? existingPost.ShareCount;
                    existingPost.PlayCount = item.GetPlayCount() ?? existingPost.PlayCount;
                    existingPost.CommentCount = item.GetCommentCount() ?? existingPost.CommentCount;
                    existingPost.DurationSeconds = item.GetDurationSeconds() ?? existingPost.DurationSeconds;
                    existingPost.MusicName = FirstNonEmpty(item.GetMusicName(), existingPost.MusicName);
                    existingPost.MusicAuthor = FirstNonEmpty(item.GetMusicAuthor(), existingPost.MusicAuthor);
                    existingPost.ScrapedAt = DateTime.UtcNow;

                    if (existingPost.S3UploadStatus == "Downloaded" && !string.IsNullOrEmpty(existingPost.S3Key))
                    {
                        try
                        {
                            var path = _storage.GetAbsolutePath(existingPost.S3Key);
                            if (!System.IO.File.Exists(path))
                            {
                                _logger.LogInformation("TikTok scrape: File missing for post {PostId} at {Path}. Resetting status to NotUploaded.", existingPost.Id, path);
                                existingPost.S3UploadStatus = "NotUploaded";
                                existingPost.S3Key = null;
                            }
                        }
                        catch
                        {
                        }
                    }

                    updatedCount++;
                    changedPosts.Add(existingPost);
                    continue;
                }

                var newPost = new FacebookPostUrl
                {
                    Platform = SocialPostPlatform.TikTok,
                    PermalinkUrl = permalinkUrl,
                    PostId = item.GetItemId(),
                    PageId = request.PageId,
                    SourcePageUrl = NormalizeUrl(item.GetProfileUrl()),
                    VideoUrl = item.GetVideoUrl(),
                    PostCreatedAt = item.GetCreatedAt(),
                    Caption = item.GetCaption(),
                    AuthorName = item.GetAuthorName(),
                    LikeCount = item.GetLikeCount(),
                    ShareCount = item.GetShareCount(),
                    PlayCount = item.GetPlayCount(),
                    CommentCount = item.GetCommentCount(),
                    DurationSeconds = item.GetDurationSeconds(),
                    MusicName = item.GetMusicName(),
                    MusicAuthor = item.GetMusicAuthor(),
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

        [HttpGet("rednote/downloads/{userId}")]
        public async Task<ActionResult<IEnumerable<FacebookPostUrlResponse>>> GetRedNoteDownloads(
            string userId,
            [FromQuery] string? pageId,
            CancellationToken cancellationToken)
        {
            userId = userId.Trim();
            pageId = string.IsNullOrWhiteSpace(pageId) ? "rednote" : pageId.Trim();

            return await _context.FacebookPostUrls
                .AsNoTracking()
                .Where(post => post.UserId == userId
                    && post.PageId == pageId
                    && post.Platform == SocialPostPlatform.RedNote)
                .OrderByDescending(post => post.ScrapedAt)
                .Take(200)
                .Select(post => new FacebookPostUrlResponse
                {
                    Id = post.Id,
                    Platform = post.Platform,
                    PermalinkUrl = post.PermalinkUrl,
                    PostId = post.PostId,
                    PageId = post.PageId,
                    SourcePageUrl = post.SourcePageUrl,
                    VideoUrl = post.VideoUrl,
                    PostCreatedAt = post.PostCreatedAt,
                    Caption = post.Caption,
                    AuthorName = post.AuthorName,
                    LikeCount = post.LikeCount,
                    ShareCount = post.ShareCount,
                    PlayCount = post.PlayCount,
                    CommentCount = post.CommentCount,
                    DurationSeconds = post.DurationSeconds,
                    MusicName = post.MusicName,
                    MusicAuthor = post.MusicAuthor,
                    S3UploadStatus = post.S3UploadStatus,
                    S3Bucket = post.S3Bucket,
                    S3Region = post.S3Region,
                    S3Key = post.S3Key,
                    S3UploadedAt = post.S3UploadedAt,
                    S3UploadError = post.S3UploadError,
                    ScrapedAt = post.ScrapedAt
                })
                .ToListAsync(cancellationToken);
        }

        [HttpGet("rednote/caption-prompt/{userId}")]
        public async Task<ActionResult<RedNoteCaptionPromptResponse>> GetRedNoteCaptionPrompt(
            string userId,
            [FromQuery] string? pageId,
            CancellationToken cancellationToken)
        {
            userId = string.IsNullOrWhiteSpace(userId) ? "user-123" : userId.Trim();
            pageId = string.IsNullOrWhiteSpace(pageId) ? "rednote" : pageId.Trim();

            var prompt = await _context.RedNoteCaptionPrompts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.UserId == userId && item.PageId == pageId,
                    cancellationToken);

            if (prompt is null)
            {
                return NotFound(new { success = false, message = "RedNote caption prompt has not been saved for this page yet." });
            }

            return Ok(ToResponse(prompt));
        }

        [HttpPut("rednote/caption-prompt")]
        public async Task<ActionResult<RedNoteCaptionPromptResponse>> SaveRedNoteCaptionPrompt(
            [FromBody] RedNoteCaptionPromptRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = string.IsNullOrWhiteSpace(request.UserId) ? "user-123" : request.UserId.Trim();
            request.PageId = string.IsNullOrWhiteSpace(request.PageId) ? "rednote" : request.PageId.Trim();
            var prompt = request.Prompt?.Trim();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return BadRequest(new { success = false, message = "RedNote caption prompt is required." });
            }

            var savedPrompt = await SaveRedNoteCaptionPromptAsync(
                request.UserId,
                request.PageId,
                prompt,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "RedNote caption prompt saved successfully.",
                prompt = ToResponse(savedPrompt)
            });
        }

        [HttpPost("rednote/downloads")]
        public async Task<ActionResult<RedNoteDownloadResponse>> QueueRedNoteDownloads(
            [FromBody] RedNoteDownloadRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = string.IsNullOrWhiteSpace(request.UserId) ? "user-123" : request.UserId.Trim();
            request.PageId = string.IsNullOrWhiteSpace(request.PageId) ? "rednote" : request.PageId.Trim();

            var validUrls = request.Urls
                .Select(NormalizeRedNoteUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validUrls.Count == 0)
            {
                return BadRequest(new { success = false, message = "At least one valid RedNote/Xiaohongshu URL is required." });
            }

            if (!string.IsNullOrWhiteSpace(request.CaptionPrompt))
            {
                await SaveRedNoteCaptionPromptAsync(
                    request.UserId,
                    request.PageId,
                    request.CaptionPrompt.Trim(),
                    cancellationToken);
            }

            var existingPosts = (await _context.FacebookPostUrls
                    .Where(post => post.UserId == request.UserId
                        && post.PageId == request.PageId
                        && post.Platform == SocialPostPlatform.RedNote
                        && validUrls.Contains(post.PermalinkUrl))
                    .ToListAsync(cancellationToken))
                .ToDictionary(post => post.PermalinkUrl, StringComparer.OrdinalIgnoreCase);

            var savedCount = 0;
            var updatedCount = 0;
            var skippedCount = 0;
            var queuedPosts = new List<FacebookPostUrl>();
            var changedPosts = new List<FacebookPostUrl>();

            foreach (var url in validUrls)
            {
                if (existingPosts.TryGetValue(url, out var existingPost))
                {
                    existingPost.ScrapedAt = DateTime.UtcNow;
                    existingPost.S3UploadError = null;

                    if (existingPost.S3UploadStatus is (FacebookPostS3UploadStatus.Downloaded or FacebookPostS3UploadStatus.Uploaded)
                        && !HasExistingLocalDownload(existingPost))
                    {
                        MarkLocalDownloadMissing(existingPost);
                    }

                    if (existingPost.S3UploadStatus is FacebookPostS3UploadStatus.Queued
                        or FacebookPostS3UploadStatus.Downloading
                        or FacebookPostS3UploadStatus.Downloaded
                        or FacebookPostS3UploadStatus.Uploading
                        or FacebookPostS3UploadStatus.Uploaded)
                    {
                        skippedCount++;
                        changedPosts.Add(existingPost);
                        continue;
                    }

                    existingPost.S3UploadStatus = FacebookPostS3UploadStatus.Queued;
                    existingPost.S3UploadError = null;
                    updatedCount++;
                    queuedPosts.Add(existingPost);
                    changedPosts.Add(existingPost);
                    continue;
                }

                var newPost = new FacebookPostUrl
                {
                    Platform = SocialPostPlatform.RedNote,
                    PermalinkUrl = url,
                    PageId = request.PageId,
                    SourcePageUrl = url,
                    ScrapedAt = DateTime.UtcNow,
                    UserId = request.UserId,
                    S3UploadStatus = FacebookPostS3UploadStatus.Queued
                };

                _context.FacebookPostUrls.Add(newPost);
                existingPosts[url] = newPost;
                savedCount++;
                queuedPosts.Add(newPost);
                changedPosts.Add(newPost);
            }

            await _context.SaveChangesAsync(cancellationToken);

            foreach (var post in queuedPosts)
            {
                await _downloadQueue.QueueAsync(
                    new FacebookPostS3UploadWorkItem(post.Id, request.UserId, request.PageId),
                    cancellationToken);
            }

            return Ok(new RedNoteDownloadResponse
            {
                Success = true,
                SavedCount = savedCount,
                UpdatedCount = updatedCount,
                QueuedCount = queuedPosts.Count,
                SkippedCount = skippedCount,
                Message = queuedPosts.Count == 0
                    ? "No new or failed RedNote videos were available to queue."
                    : $"Queued {queuedPosts.Count} RedNote video(s) for local download.",
                Posts = changedPosts
                    .OrderByDescending(post => post.ScrapedAt)
                    .Select(ToResponse)
                    .ToList()
            });
        }

        [HttpPost("rednote/downloads/{id:int}/caption/retry")]
        public async Task<IActionResult> RetryRedNoteCaption(
            int id,
            [FromBody] RedNoteCaptionRetryRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = string.IsNullOrWhiteSpace(request.UserId) ? "user-123" : request.UserId.Trim();
            request.PageId = string.IsNullOrWhiteSpace(request.PageId) ? "rednote" : request.PageId.Trim();

            var post = await _context.FacebookPostUrls
                .FirstOrDefaultAsync(
                    item => item.Id == id
                        && item.UserId == request.UserId
                        && item.PageId == request.PageId
                        && item.Platform == SocialPostPlatform.RedNote,
                    cancellationToken);

            if (post is null)
            {
                return NotFound(new { success = false, message = "RedNote download row was not found." });
            }

            if (post.S3UploadStatus is not (FacebookPostS3UploadStatus.Downloaded or FacebookPostS3UploadStatus.Uploaded)
                || string.IsNullOrWhiteSpace(post.S3Key))
            {
                return BadRequest(new { success = false, message = "Download the RedNote video before retrying the AI caption.", post = ToResponse(post) });
            }

            if (!HasExistingLocalDownload(post))
            {
                MarkLocalDownloadMissing(post);
                await _context.SaveChangesAsync(cancellationToken);
                return BadRequest(new { success = false, message = "The local video file is missing. Download it again before retrying the AI caption.", post = ToResponse(post) });
            }

            var captionPrompt = request.CaptionPrompt?.Trim();
            if (!string.IsNullOrWhiteSpace(captionPrompt))
            {
                await SaveRedNoteCaptionPromptAsync(
                    request.UserId,
                    request.PageId,
                    captionPrompt,
                    cancellationToken);
            }

            string localVideoPath;
            try
            {
                localVideoPath = _storage.GetAbsolutePath(post.S3Key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid local storage key for RedNote post {PostId}.", post.Id);
                return BadRequest(new { success = false, message = "The saved local video path is invalid. Download it again before retrying the AI caption.", post = ToResponse(post) });
            }

            var generatedCaption = await _captionGenerator.GenerateCaptionAsync(
                localVideoPath,
                post,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(generatedCaption))
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    success = false,
                    message = "AI caption could not be generated. Check the Gemini model/API key and this page's caption prompt.",
                    post = ToResponse(post)
                });
            }

            post.Caption = generatedCaption;
            post.S3UploadError = null;
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "RedNote AI caption regenerated successfully.",
                post = ToResponse(post)
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

            var metaAppValidation = await ValidateMetaAppAsync(page.UserId, page.FacebookMetaAppId);
            if (metaAppValidation is not null)
            {
                return metaAppValidation;
            }

            var existingPage = await _context.FacebookPages.FirstOrDefaultAsync(
                p => p.UserId == page.UserId && p.PageId == page.PageId);
            
            if (existingPage != null)
            {
                existingPage.AccessToken = page.AccessToken;
                existingPage.PageName = page.PageName;
                existingPage.Category = string.IsNullOrWhiteSpace(page.Category) ? null : page.Category.Trim();
                existingPage.AvatarUrl = string.IsNullOrWhiteSpace(page.AvatarUrl) ? null : page.AvatarUrl.Trim();
                existingPage.FacebookMetaAppId = page.FacebookMetaAppId;
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

            var metaAppValidation = await ValidateMetaAppAsync(page.UserId, page.FacebookMetaAppId);
            if (metaAppValidation is not null)
            {
                return metaAppValidation;
            }

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
            existingPage.FacebookMetaAppId = page.FacebookMetaAppId;
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
        public async Task<IActionResult> DeleteFacebookPost(
            int id,
            [FromQuery] string? userId,
            [FromQuery] string? pageId,
            CancellationToken cancellationToken)
        {
            userId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
            pageId = string.IsNullOrWhiteSpace(pageId) ? null : pageId.Trim();

            var query = _context.FacebookPostUrls.Where(post => post.Id == id);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(post => post.UserId == userId);
            }

            if (!string.IsNullOrWhiteSpace(pageId))
            {
                query = query.Where(post => post.PageId == pageId);
            }

            var post = await query.FirstOrDefaultAsync(cancellationToken);
            if (post == null)
            {
                return NotFound(new { success = false, message = "Post not found." });
            }

            var linkedJobs = await _context.FacebookReelUploadJobs
                .Where(job => job.FacebookPostUrlId == post.Id)
                .ToListAsync(cancellationToken);

            var deletedFileCount = DeleteLocalFiles(
                new[] { post.S3Key }.Concat(linkedJobs.Select(job => job.S3Key)),
                $"post {id}");

            _context.FacebookReelUploadJobs.RemoveRange(linkedJobs);
            _context.FacebookPostUrls.Remove(post);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = $"Post deleted permanently with {linkedJobs.Count} upload job(s) and {deletedFileCount} local video file(s)."
            });
        }

        [HttpDelete("facebook/posts/all/{userId}/{pageId}")]
        public async Task<IActionResult> DeleteAllFacebookPosts(
            string userId,
            string pageId,
            [FromQuery] string? platform,
            CancellationToken cancellationToken)
        {
            userId = userId.Trim();
            pageId = pageId.Trim();
            var normalizedPlatform = NormalizeSourcePlatform(platform);

            var postsQuery = _context.FacebookPostUrls
                .Where(p => p.UserId == userId && p.PageId == pageId);

            postsQuery = string.IsNullOrWhiteSpace(normalizedPlatform)
                ? postsQuery.Where(p => p.Platform != SocialPostPlatform.RedNote)
                : postsQuery.Where(p => p.Platform == normalizedPlatform);

            var posts = await postsQuery.ToListAsync(cancellationToken);

            if (posts.Count == 0)
            {
                return Ok(new { success = true, message = "No posts found to delete.", deletedCount = 0 });
            }

            var postIds = posts.Select(post => post.Id).ToList();
            var linkedJobs = await _context.FacebookReelUploadJobs
                .Where(job => job.FacebookPostUrlId.HasValue && postIds.Contains(job.FacebookPostUrlId.Value))
                .ToListAsync(cancellationToken);

            var deletedFileCount = DeleteLocalFiles(
                posts.Select(post => post.S3Key).Concat(linkedJobs.Select(job => job.S3Key)),
                $"bulk page {pageId}");

            _logger.LogInformation("Removing {Count} post records from database for user {UserId} and page {PageId}", posts.Count, userId, pageId);
            _context.FacebookReelUploadJobs.RemoveRange(linkedJobs);
            _context.FacebookPostUrls.RemoveRange(posts);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = $"Deleted {posts.Count} posts, {linkedJobs.Count} upload job(s), and {deletedFileCount} local video file(s) successfully.",
                deletedCount = posts.Count,
                deletedJobCount = linkedJobs.Count,
                deletedFileCount
            });
        }

        private static FacebookPostUrlResponse ToResponse(FacebookPostUrl post)
        {
            return new FacebookPostUrlResponse
            {
                Id = post.Id,
                Platform = post.Platform,
                PermalinkUrl = post.PermalinkUrl,
                PostId = post.PostId,
                PageId = post.PageId,
                SourcePageUrl = post.SourcePageUrl,
                VideoUrl = post.VideoUrl,
                PostCreatedAt = post.PostCreatedAt,
                Caption = post.Caption,
                AuthorName = post.AuthorName,
                LikeCount = post.LikeCount,
                ShareCount = post.ShareCount,
                PlayCount = post.PlayCount,
                CommentCount = post.CommentCount,
                DurationSeconds = post.DurationSeconds,
                MusicName = post.MusicName,
                MusicAuthor = post.MusicAuthor,
                S3UploadStatus = post.S3UploadStatus,
                S3Bucket = post.S3Bucket,
                S3Region = post.S3Region,
                S3Key = post.S3Key,
                S3UploadedAt = post.S3UploadedAt,
                S3UploadError = post.S3UploadError,
                ScrapedAt = post.ScrapedAt
            };
        }

        private async Task<RedNoteCaptionPrompt> SaveRedNoteCaptionPromptAsync(
            string userId,
            string pageId,
            string prompt,
            CancellationToken cancellationToken)
        {
            var existingPrompt = await _context.RedNoteCaptionPrompts
                .FirstOrDefaultAsync(
                    item => item.UserId == userId && item.PageId == pageId,
                    cancellationToken);
            var now = DateTime.UtcNow;

            if (existingPrompt is null)
            {
                existingPrompt = new RedNoteCaptionPrompt
                {
                    UserId = userId,
                    PageId = pageId,
                    Prompt = prompt,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.RedNoteCaptionPrompts.Add(existingPrompt);
            }
            else
            {
                existingPrompt.Prompt = prompt;
                existingPrompt.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return existingPrompt;
        }

        private static RedNoteCaptionPromptResponse ToResponse(RedNoteCaptionPrompt prompt)
        {
            return new RedNoteCaptionPromptResponse
            {
                UserId = prompt.UserId,
                PageId = prompt.PageId,
                Prompt = prompt.Prompt,
                UpdatedAt = prompt.UpdatedAt
            };
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

        private static string? NormalizeTikTokProfileInput(string? profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                return null;
            }

            var value = profile.Trim();
            if (value.StartsWith('@'))
            {
                value = $"https://www.tiktok.com/{value}";
            }
            else if (!value.Contains("://", StringComparison.Ordinal) && !value.Contains('/', StringComparison.Ordinal))
            {
                value = $"https://www.tiktok.com/@{value.TrimStart('@')}";
            }
            else if (!value.Contains("://", StringComparison.Ordinal))
            {
                value = $"https://{value.TrimStart('/')}";
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || (!uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)
                    && !uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return NormalizeUrl(value);
        }

        private static string? ExtractTikTokHandle(string profileUrl)
        {
            if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(segment => segment.StartsWith('@'))
                ?.TrimStart('@');
        }

        private static string? NormalizeRedNoteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var value = url.Trim();
            if (!value.Contains("://", StringComparison.Ordinal))
            {
                value = $"https://{value.TrimStart('/')}";
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !IsRedNoteHost(uri.Host))
            {
                return null;
            }

            return NormalizeUrl(value);
        }

        private static bool IsRedNoteHost(string host)
        {
            return host.Equals("xhslink.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".xhslink.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("xiaohongshu.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".xiaohongshu.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("rednote.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".rednote.com", StringComparison.OrdinalIgnoreCase);
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

        private async Task<IActionResult?> ValidateMetaAppAsync(string userId, int? facebookMetaAppId)
        {
            if (!facebookMetaAppId.HasValue)
            {
                return null;
            }

            var exists = await _context.FacebookMetaApps
                .AsNoTracking()
                .AnyAsync(item => item.Id == facebookMetaAppId.Value && item.UserId == userId);

            return exists
                ? null
                : BadRequest(new { success = false, message = "Selected Meta App was not found for this user." });
        }

        private int DeleteLocalFiles(IEnumerable<string?> storageKeys, string context)
        {
            var deletedCount = 0;
            foreach (var storageKey in storageKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var localPath = _storage.GetAbsolutePath(storageKey);
                    _logger.LogInformation("Attempting to delete local file for {Context} at {Path}.", context, localPath);

                    if (!System.IO.File.Exists(localPath))
                    {
                        _logger.LogWarning("Local file for {Context} was not found at {Path}.", context, localPath);
                        continue;
                    }

                    System.IO.File.Delete(localPath);
                    deletedCount++;
                    _logger.LogInformation("Successfully deleted local file for {Context} at {Path}.", context, localPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete local video file for {Context} with storage key {StorageKey}.", context, storageKey);
                }
            }

            return deletedCount;
        }

        private static string TrimForClient(string value)
        {
            value = value.Trim();
            return value.Length <= 500 ? value : value[..500];
        }
    }
}
