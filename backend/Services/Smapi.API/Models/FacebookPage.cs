using System.ComponentModel.DataAnnotations;

namespace Smapi.API.Models
{
    public class FacebookPage
    {
        public int Id { get; set; }
        
        [Required]
        public string PageId { get; set; } = string.Empty;
        
        [Required]
        public string PageName { get; set; } = string.Empty;
        
        [Required]
        public string AccessToken { get; set; } = string.Empty;
        
        public string? Category { get; set; }
        public string? AvatarUrl { get; set; }
        
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        
        [Required]
        public string UserId { get; set; } = string.Empty;
    }
}
