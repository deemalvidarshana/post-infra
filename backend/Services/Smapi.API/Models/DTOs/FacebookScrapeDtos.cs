using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models.DTOs
{
    public class FacebookScrapeRequest
    {
        public DateOnly? OnlyPostsNewerThan { get; set; }

        [Range(1, 1000)]
        public int ResultsLimit { get; set; } = 100;

        [MinLength(1)]
        public List<FacebookStartUrl> StartUrls { get; set; } = new();

        public string UserId { get; set; } = "user-123";

        public string? PageId { get; set; }
    }

    public class FacebookStartUrl
    {
        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;
    }

    public class FacebookScrapeResponse
    {
        public bool Success { get; set; }

        public int ScrapedCount { get; set; }

        public int SavedCount { get; set; }

        public int UpdatedCount { get; set; }

        public int SkippedCount { get; set; }

        public List<FacebookPostUrlResponse> Posts { get; set; } = new();
    }

    public class TikTokScrapeRequest
    {
        public DateOnly? NewestPostDate { get; set; }

        public DateOnly? OldestPostDateUnified { get; set; }

        [Range(1, 1000)]
        public int ResultsPerPage { get; set; } = 100;

        [MinLength(1)]
        public List<string> Profiles { get; set; } = new();

        public string UserId { get; set; } = "user-123";

        public string? PageId { get; set; }
    }

    public class RedNoteDownloadRequest
    {
        [MinLength(1)]
        public List<string> Urls { get; set; } = new();

        public string UserId { get; set; } = "user-123";

        public string PageId { get; set; } = "rednote";
    }

    public class RedNoteDownloadResponse
    {
        public bool Success { get; set; }

        public int SavedCount { get; set; }

        public int UpdatedCount { get; set; }

        public int QueuedCount { get; set; }

        public int SkippedCount { get; set; }

        public string Message { get; set; } = string.Empty;

        public List<FacebookPostUrlResponse> Posts { get; set; } = new();
    }

    public class FacebookPostUrlResponse
    {
        public int Id { get; set; }

        public string Platform { get; set; } = "Facebook";

        public string PermalinkUrl { get; set; } = string.Empty;

        public string? PostId { get; set; }

        public string? PageId { get; set; }

        public string? SourcePageUrl { get; set; }

        public string? VideoUrl { get; set; }

        public DateTime? PostCreatedAt { get; set; }

        public string? Caption { get; set; }

        public string? AuthorName { get; set; }

        public long? LikeCount { get; set; }

        public long? ShareCount { get; set; }

        public long? PlayCount { get; set; }

        public long? CommentCount { get; set; }

        public int? DurationSeconds { get; set; }

        public string? MusicName { get; set; }

        public string? MusicAuthor { get; set; }

        public string S3UploadStatus { get; set; } = "NotUploaded";

        public string? S3Bucket { get; set; }

        public string? S3Region { get; set; }

        public string? S3Key { get; set; }

        public DateTime? S3UploadedAt { get; set; }

        public string? S3UploadError { get; set; }

        public DateTime ScrapedAt { get; set; }
    }
}
