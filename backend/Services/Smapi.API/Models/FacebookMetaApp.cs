using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public class FacebookMetaApp
    {
        public int Id { get; set; }

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

        [Required]
        [MaxLength(128)]
        public string VerifyToken { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string WebhookKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string GraphApiVersion { get; set; } = "v24.0";

        public bool IsDefault { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
