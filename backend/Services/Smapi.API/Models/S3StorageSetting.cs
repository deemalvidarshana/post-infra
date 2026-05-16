using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public class S3StorageSetting
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Bucket { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Region { get; set; } = string.Empty;

        [MaxLength(4096)]
        public string? EndpointUrl { get; set; }

        [Required]
        [MaxLength(256)]
        public string AccessKeyId { get; set; } = string.Empty;

        [Required]
        [MaxLength(4096)]
        public string SecretAccessKey { get; set; } = string.Empty;

        [MaxLength(4096)]
        public string? SessionToken { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
