using Smapi.API.Data;
using Smapi.API.Models;
using Smapi.API.Models.DTOs;
using Smapi.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SettingsController : ControllerBase
    {
        private const string GlobalApifySettingUserId = "__global__";
        private readonly SmapiDbContext _context;
        private readonly ILocalVideoStorageService _storage;

        public SettingsController(SmapiDbContext context, ILocalVideoStorageService storage)
        {
            _context = context;
            _storage = storage;
        }

        [HttpGet("s3/{userId}")]
        public ActionResult<S3StorageSettingResponse> GetS3Settings(string userId, CancellationToken cancellationToken)
        {
            userId = userId.Trim();
            cancellationToken.ThrowIfCancellationRequested();

            return Ok(new S3StorageSettingResponse
            {
                UserId = userId,
                Bucket = _storage.RootDirectory,
                Region = "local",
                EndpointUrl = _storage.PublicBaseUrl,
                AccessKeyId = null,
                HasSecretAccessKey = false,
                HasSessionToken = false,
                UpdatedAt = null
            });
        }

        [HttpPut("s3")]
        public IActionResult SaveS3Settings(
            [FromBody] S3StorageSettingRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.UserId = request.UserId.Trim();

            return Ok(new
            {
                success = true,
                message = "Local storage is configured from appsettings. AWS S3 settings are no longer required.",
                settings = new S3StorageSettingResponse
                {
                    UserId = request.UserId,
                    Bucket = _storage.RootDirectory,
                    Region = "local",
                    EndpointUrl = _storage.PublicBaseUrl,
                    AccessKeyId = null,
                    HasSecretAccessKey = false,
                    HasSessionToken = false,
                    UpdatedAt = null
                }
            });
        }

        [HttpGet("apify")]
        public async Task<ActionResult<ApifySettingResponse>> GetApifySettings(
            CancellationToken cancellationToken)
        {
            var setting = await FindGlobalApifySettingAsync(cancellationToken);

            if (setting is null)
            {
                return NotFound(new { success = false, message = "Apify API key has not been saved yet." });
            }

            return Ok(ToResponse(setting));
        }

        [HttpGet("apify/{userId}")]
        public Task<ActionResult<ApifySettingResponse>> GetApifySettingsForLegacyUserRoute(
            string userId,
            CancellationToken cancellationToken)
        {
            return GetApifySettings(cancellationToken);
        }

        [HttpPut("apify")]
        public async Task<IActionResult> SaveApifySettings(
            [FromBody] ApifySettingRequest request,
            CancellationToken cancellationToken)
        {
            var apiToken = request.ApiToken?.Trim();

            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return BadRequest(new { success = false, message = "Apify API key is required." });
            }

            var existing = await _context.ApifySettings
                .FirstOrDefaultAsync(item => item.UserId == GlobalApifySettingUserId, cancellationToken);

            if (existing is null)
            {
                existing = new ApifySetting
                {
                    UserId = GlobalApifySettingUserId,
                    ApiToken = apiToken,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.ApifySettings.Add(existing);
            }
            else
            {
                existing.ApiToken = apiToken;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Apify API key saved successfully.",
                settings = ToResponse(existing)
            });
        }

        private async Task<ApifySetting?> FindGlobalApifySettingAsync(CancellationToken cancellationToken)
        {
            return await _context.ApifySettings
                .AsNoTracking()
                .Where(item => item.UserId == GlobalApifySettingUserId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _context.ApifySettings
                    .AsNoTracking()
                    .OrderByDescending(item => item.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
        }

        private static ApifySettingResponse ToResponse(ApifySetting setting)
        {
            return new ApifySettingResponse
            {
                UserId = GlobalApifySettingUserId,
                ApiToken = setting.ApiToken,
                HasApiToken = !string.IsNullOrWhiteSpace(setting.ApiToken),
                ApiTokenLength = setting.ApiToken?.Length ?? 0,
                UpdatedAt = setting.UpdatedAt
            };
        }
    }
}
