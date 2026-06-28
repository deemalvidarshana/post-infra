using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public static class FacebookAutoReplyMode
    {
        public const string ManualApproval = "ManualApproval";
        public const string Auto = "Auto";
    }

    public class FacebookAutoReplySetting
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PageId { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        [Required]
        [MaxLength(32)]
        public string Mode { get; set; } = FacebookAutoReplyMode.ManualApproval;

        [MaxLength(4096)]
        public string Prompt { get; set; } = DefaultPrompt;

        [MaxLength(128)]
        public string Tone { get; set; } = "Friendly";

        [MaxLength(64)]
        public string Language { get; set; } = "Sinhala/English";

        [Range(1, int.MaxValue)]
        public int MaxRepliesPerPostPerDay { get; set; } = 20;

        [MaxLength(2048)]
        public string? IgnoreKeywords { get; set; }

        [MaxLength(2048)]
        public string? EscalationKeywords { get; set; }

        [Required]
        [MaxLength(32)]
        public string GraphApiVersion { get; set; } = "v24.0";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public const string DefaultPrompt =
            "Reply as the Facebook Page. Be helpful, natural, and short. " +
            "Answer only what the commenter asked. Do not mention that you are AI.";
    }
}
