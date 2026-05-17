using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models.DTOs
{
    public class GeminiSettingRequest
    {
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? Model { get; set; }

        [MaxLength(4096)]
        public string? ApiKey { get; set; }
    }

    public class GeminiSettingResponse
    {
        public string UserId { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string? ApiKey { get; set; }

        public bool HasApiKey { get; set; }

        public int ApiKeyLength { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
