using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models.DTOs
{
    public class FacebookMetaAppRequest
    {
        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? AppId { get; set; }

        [MaxLength(4096)]
        public string? AppSecret { get; set; }

        [MaxLength(128)]
        public string? VerifyToken { get; set; }

        [MaxLength(128)]
        public string? WebhookKey { get; set; }

        [MaxLength(32)]
        public string? GraphApiVersion { get; set; }

        public bool IsDefault { get; set; }
    }

    public class FacebookMetaAppResponse
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? AppId { get; set; }

        public bool HasAppSecret { get; set; }

        public int AppSecretLength { get; set; }

        public string VerifyToken { get; set; } = string.Empty;

        public string WebhookKey { get; set; } = string.Empty;

        public string CallbackPath { get; set; } = string.Empty;

        public string GraphApiVersion { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public class FacebookPageWebhookSubscriptionRequest
    {
        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PageId { get; set; } = string.Empty;
    }
}
