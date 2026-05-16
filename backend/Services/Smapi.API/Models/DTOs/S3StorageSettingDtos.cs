using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models.DTOs
{
    public class S3StorageSettingRequest
    {
        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string Bucket { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Region { get; set; } = string.Empty;

        [MaxLength(4096)]
        public string? EndpointUrl { get; set; }

        [MaxLength(256)]
        public string? AccessKeyId { get; set; }

        [MaxLength(4096)]
        public string? SecretAccessKey { get; set; }

        [MaxLength(4096)]
        public string? SessionToken { get; set; }
    }

    public class S3StorageSettingResponse
    {
        public string UserId { get; set; } = string.Empty;

        public string Bucket { get; set; } = string.Empty;

        public string Region { get; set; } = string.Empty;

        public string? EndpointUrl { get; set; }

        public string? AccessKeyId { get; set; }

        public bool HasSecretAccessKey { get; set; }

        public bool HasSessionToken { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
