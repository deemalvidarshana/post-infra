using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PagesController : ControllerBase
    {
        private readonly SmapiDbContext _context;

        public PagesController(SmapiDbContext context)
        {
            _context = context;
        }

        [HttpGet("facebook/{userId}")]
        public async Task<ActionResult<IEnumerable<FacebookPage>>> GetFacebookPages(string userId)
        {
            return await _context.FacebookPages
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.ConnectedAt)
                .ToListAsync();
        }

        [HttpPost("facebook/connect")]
        public async Task<IActionResult> ConnectFacebookPage([FromBody] FacebookPage page)
        {
            if (string.IsNullOrEmpty(page.PageId) || string.IsNullOrEmpty(page.AccessToken))
            {
                return BadRequest(new { success = false, message = "Page ID and Access Token are required." });
            }

            var existingPage = await _context.FacebookPages.FirstOrDefaultAsync(p => p.PageId == page.PageId);
            
            if (existingPage != null)
            {
                existingPage.AccessToken = page.AccessToken;
                existingPage.PageName = page.PageName;
                existingPage.ConnectedAt = DateTime.UtcNow;
                _context.FacebookPages.Update(existingPage);
            }
            else
            {
                _context.FacebookPages.Add(page);
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Facebook page connected successfully." });
        }

        [HttpDelete("facebook/{id}")]
        public async Task<IActionResult> DeleteFacebookPage(int id)
        {
            var page = await _context.FacebookPages.FindAsync(id);
            if (page == null)
            {
                return NotFound();
            }

            _context.FacebookPages.Remove(page);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Facebook page disconnected successfully." });
        }
    }
}
