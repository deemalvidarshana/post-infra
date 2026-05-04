using Microsoft.AspNetCore.Identity;

namespace Identity.API.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Add custom properties here (e.g., FirstName, LastName)
        public string? FullName { get; set; }
    }
}
