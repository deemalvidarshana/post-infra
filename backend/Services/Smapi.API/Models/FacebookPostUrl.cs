using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public class FacebookPostUrl
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(32)]
        public string Platform { get; set; } = SocialPostPlatform.Facebook;

        [Required]
        [MaxLength(2048)]
        public string PermalinkUrl { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? PostId { get; set; }

        [MaxLength(128)]
        public string? PageId { get; set; }

        [MaxLength(2048)]
        public string? SourcePageUrl { get; set; }

        [MaxLength(4096)]
        public string? VideoUrl { get; set; }

        public DateTime? PostCreatedAt { get; set; }

        public string? Caption { get; set; }

        [MaxLength(256)]
        public string? AuthorName { get; set; }

        public long? LikeCount { get; set; }

        public long? ShareCount { get; set; }

        public long? PlayCount { get; set; }

        public long? CommentCount { get; set; }

        public int? DurationSeconds { get; set; }

        [MaxLength(512)]
        public string? MusicName { get; set; }

        [MaxLength(512)]
        public string? MusicAuthor { get; set; }

        [MaxLength(64)]
        public string S3UploadStatus { get; set; } = "NotUploaded";

        [MaxLength(128)]
        public string? S3Bucket { get; set; }

        [MaxLength(64)]
        public string? S3Region { get; set; }

        [MaxLength(4096)]
        public string? S3Key { get; set; }

        public DateTime? S3UploadedAt { get; set; }

        public string? S3UploadError { get; set; }

        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;
    }

    public static class SocialPostPlatform
    {
        public const string Facebook = "Facebook";
        public const string TikTok = "TikTok";
        public const string RedNote = "RedNote";
    }
}
