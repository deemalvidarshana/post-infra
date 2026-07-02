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

    public class OpenRouterSettingRequest
    {
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? Model { get; set; }

        [MaxLength(4096)]
        public string? ApiKey { get; set; }
    }

    public class OpenRouterSettingResponse
    {
        public string UserId { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string? ApiKey { get; set; }

        public bool HasApiKey { get; set; }

        public int ApiKeyLength { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    public class AiProviderSettingRequest
    {
        [MaxLength(32)]
        public string? Provider { get; set; }

        [MaxLength(128)]
        public string? Model { get; set; }

        [MaxLength(4096)]
        public string? ApiKey { get; set; }
    }

    public class AiProviderSettingResponse
    {
        public string ActiveProvider { get; set; } = string.Empty;

        public string Provider { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public bool HasApiKey { get; set; }

        public int ApiKeyLength { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public IEnumerable<AiProviderOptionResponse> Providers { get; set; } =
            Enumerable.Empty<AiProviderOptionResponse>();
    }

    public class AiProviderOptionResponse
    {
        public string Provider { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string DefaultModel { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public bool HasApiKey { get; set; }

        public int ApiKeyLength { get; set; }

        public bool IsActive { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
