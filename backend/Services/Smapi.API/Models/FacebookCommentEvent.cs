using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public static class FacebookCommentEventStatus
    {
        public const string Skipped = "Skipped";
        public const string Queued = "Queued";
        public const string Processing = "Processing";
        public const string PendingApproval = "PendingApproval";
        public const string Replied = "Replied";
        public const string Failed = "Failed";
    }

    public class FacebookCommentEvent
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PageId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? PostId { get; set; }

        [Required]
        [MaxLength(128)]
        public string CommentId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ParentCommentId { get; set; }

        public string? CommentText { get; set; }

        [MaxLength(128)]
        public string? CommentAuthorId { get; set; }

        [MaxLength(256)]
        public string? CommentAuthorName { get; set; }

        [Required]
        [MaxLength(32)]
        public string Verb { get; set; } = "add";

        [Required]
        [MaxLength(64)]
        public string Status { get; set; } = FacebookCommentEventStatus.Queued;

        public string? GeneratedReply { get; set; }

        [MaxLength(128)]
        public string? ReplyCommentId { get; set; }

        public string? SkipReason { get; set; }

        public string? ErrorMessage { get; set; }

        public string? RawPayload { get; set; }

        public int Attempts { get; set; }

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedAt { get; set; }
    }
}
