using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models.DTOs
{
    public class CreateFacebookReelUploadJobRequest
    {
        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PageId { get; set; } = string.Empty;

        public int? FacebookPostUrlId { get; set; }

        [MaxLength(4096)]
        public string? VideoUrl { get; set; }

        public string? Caption { get; set; }

        [MaxLength(256)]
        public string? S3Bucket { get; set; }

        [MaxLength(64)]
        public string? S3Region { get; set; }

        [MaxLength(256)]
        public string? AwsAccessKeyId { get; set; }

        [MaxLength(256)]
        public string? AwsSecretAccessKey { get; set; }

        [MaxLength(2048)]
        public string? AwsSessionToken { get; set; }

        [MaxLength(4096)]
        public string? S3EndpointUrl { get; set; }

        [MaxLength(32)]
        public string GraphApiVersion { get; set; } = "v24.0";

        [MaxLength(32)]
        public string Platform { get; set; } = "Facebook";
    }

    public class CreateFacebookReelUploadBatchRequest
    {
        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PageId { get; set; } = string.Empty;

        [Range(1, 48)]
        public int DailyPostCount { get; set; } = 6;

        public DateTime? StartAt { get; set; }

        [MaxLength(32)]
        public string GraphApiVersion { get; set; } = "v24.0";

        [MaxLength(32)]
        public string Platform { get; set; } = "Facebook";
    }

    public class FacebookReelUploadBatchResponse
    {
        public bool Success { get; set; }

        public int MatchedCount { get; set; }

        public int QueuedCount { get; set; }

        public int SkippedCount { get; set; }

        public double IntervalHours { get; set; }

        public List<FacebookReelUploadJobResponse> Jobs { get; set; } = new();

        public string Message { get; set; } = string.Empty;
    }

    public class FacebookReelUploadJobResponse
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string PageId { get; set; } = string.Empty;

        public string? PageName { get; set; }

        public int? FacebookPostUrlId { get; set; }

        public string VideoSourceUrl { get; set; } = string.Empty;

        public string? Caption { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? S3Bucket { get; set; }

        public string? S3Region { get; set; }

        public string? S3EndpointUrl { get; set; }

        public string? S3Key { get; set; }

        public string GraphApiVersion { get; set; } = string.Empty;

        public string? FacebookVideoId { get; set; }

        public string? FacebookPostId { get; set; }

        public string? ErrorMessage { get; set; }

        public int Attempts { get; set; }

        public DateTime? ScheduledFor { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? RetainUntil { get; set; }
    }
}
