using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models.DTOs
{
    public class FacebookAutoReplySettingRequest
    {
        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PageId { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        [MaxLength(32)]
        public string? Mode { get; set; }

        [MaxLength(4096)]
        public string? Prompt { get; set; }

        [MaxLength(128)]
        public string? Tone { get; set; }

        [MaxLength(64)]
        public string? Language { get; set; }

        [Range(1, int.MaxValue)]
        public int MaxRepliesPerPostPerDay { get; set; } = 20;

        [MaxLength(2048)]
        public string? IgnoreKeywords { get; set; }

        [MaxLength(2048)]
        public string? EscalationKeywords { get; set; }

        [MaxLength(32)]
        public string? GraphApiVersion { get; set; }
    }

    public class FacebookAutoReplySettingResponse
    {
        public string UserId { get; set; } = string.Empty;

        public string PageId { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public string Mode { get; set; } = string.Empty;

        public string Prompt { get; set; } = string.Empty;

        public string Tone { get; set; } = string.Empty;

        public string Language { get; set; } = string.Empty;

        public int MaxRepliesPerPostPerDay { get; set; }

        public string? IgnoreKeywords { get; set; }

        public string? EscalationKeywords { get; set; }

        public string GraphApiVersion { get; set; } = string.Empty;

        public DateTime? UpdatedAt { get; set; }
    }

    public class FacebookCommentEventResponse
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string PageId { get; set; } = string.Empty;

        public string? PostId { get; set; }

        public string CommentId { get; set; } = string.Empty;

        public string? ParentCommentId { get; set; }

        public string? CommentText { get; set; }

        public string? CommentAuthorId { get; set; }

        public string? CommentAuthorName { get; set; }

        public string Verb { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? GeneratedReply { get; set; }

        public string? ReplyCommentId { get; set; }

        public string? SkipReason { get; set; }

        public string? ErrorMessage { get; set; }

        public int Attempts { get; set; }

        public DateTime ReceivedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime? ProcessedAt { get; set; }
    }

    public class FacebookCommentReplyRequest
    {
        [MaxLength(2000)]
        public string? Reply { get; set; }
    }
}
