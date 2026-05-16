using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models.DTOs
{
    public class ApifySettingRequest
    {
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(4096)]
        public string? ApiToken { get; set; }
    }

    public class ApifySettingResponse
    {
        public string UserId { get; set; } = string.Empty;

        public string? ApiToken { get; set; }

        public bool HasApiToken { get; set; }

        public int ApiTokenLength { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
