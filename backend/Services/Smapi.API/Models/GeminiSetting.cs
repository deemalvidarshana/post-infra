using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public class GeminiSetting
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Model { get; set; } = string.Empty;

        [Required]
        [MaxLength(4096)]
        public string ApiKey { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
