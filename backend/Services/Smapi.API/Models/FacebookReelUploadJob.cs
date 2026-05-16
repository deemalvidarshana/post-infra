using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public static class FacebookReelUploadJobStatus
    {
        public const string Queued = "Queued";
        public const string Downloading = "Downloading";
        public const string Downloaded = "Downloaded";
        public const string StoredLocally = "StoredLocally";
        public const string StoredInS3 = "StoredInS3";
        public const string Publishing = "Publishing";
        public const string Published = "Published";
        public const string Failed = "Failed";
    }

    public class FacebookReelUploadJob
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PageId { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? PageName { get; set; }

        public int? FacebookPostUrlId { get; set; }

        public FacebookPostUrl? FacebookPostUrl { get; set; }

        [Required]
        [MaxLength(4096)]
        public string VideoSourceUrl { get; set; } = string.Empty;

        public string? Caption { get; set; }

        [Required]
        [MaxLength(64)]
        public string Status { get; set; } = FacebookReelUploadJobStatus.Queued;

        [MaxLength(128)]
        public string? S3Bucket { get; set; }

        [MaxLength(64)]
        public string? S3Region { get; set; }

        [MaxLength(4096)]
        public string? S3EndpointUrl { get; set; }

        [MaxLength(4096)]
        public string? S3Key { get; set; }

        [MaxLength(32)]
        public string GraphApiVersion { get; set; } = "v24.0";

        [MaxLength(128)]
        public string? FacebookVideoId { get; set; }

        [MaxLength(128)]
        public string? FacebookPostId { get; set; }

        public string? ErrorMessage { get; set; }

        public int Attempts { get; set; }

        public DateTime? ScheduledFor { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? RetainUntil { get; set; }
    }
}
