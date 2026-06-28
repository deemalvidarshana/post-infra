using System.Security.Cryptography;
using Smapi.API.Data;
using Smapi.API.Models;
using Smapi.API.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FacebookMetaAppsController : ControllerBase
    {
        private readonly SmapiDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;

        public FacebookMetaAppsController(
            SmapiDbContext context,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<FacebookMetaAppResponse>>> GetMetaApps(
            [FromQuery] string? userId,
            CancellationToken cancellationToken)
        {
            userId = NormalizeNullable(userId);

            var query = _context.FacebookMetaApps.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(item => item.UserId == userId);
            }

            var apps = await query
                .OrderByDescending(item => item.IsDefault)
                .ThenBy(item => item.Name)
                .ToListAsync(cancellationToken);

            return Ok(apps.Select(ToResponse));
        }

        [HttpPost]
        public async Task<IActionResult> CreateMetaApp(
            [FromBody] FacebookMetaAppRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.Name = request.Name.Trim();

            if (string.IsNullOrWhiteSpace(request.UserId)
                || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { success = false, message = "User ID and Meta App name are required." });
            }

            var duplicateName = await _context.FacebookMetaApps
                .AnyAsync(
                    item => item.UserId == request.UserId && item.Name == request.Name,
                    cancellationToken);
            if (duplicateName)
            {
                return Conflict(new { success = false, message = "A Meta App with this name already exists for this user." });
            }

            var webhookKey = await BuildUniqueWebhookKeyAsync(request.WebhookKey, request.Name, cancellationToken);
            var shouldBeDefault = request.IsDefault
                || !await _context.FacebookMetaApps.AnyAsync(item => item.UserId == request.UserId, cancellationToken);
            var now = DateTime.UtcNow;

            var metaApp = new FacebookMetaApp
            {
                UserId = request.UserId,
                Name = request.Name,
                AppId = NormalizeNullable(request.AppId),
                AppSecret = NormalizeNullable(request.AppSecret),
                VerifyToken = string.IsNullOrWhiteSpace(request.VerifyToken)
                    ? GenerateVerifyToken()
                    : request.VerifyToken.Trim(),
                WebhookKey = webhookKey,
                GraphApiVersion = NormalizeGraphApiVersion(request.GraphApiVersion),
                IsDefault = shouldBeDefault,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.FacebookMetaApps.Add(metaApp);
            if (shouldBeDefault)
            {
                await ClearOtherDefaultsAsync(request.UserId, metaApp.Id, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Facebook Meta App saved.",
                metaApp = ToResponse(metaApp)
            });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateMetaApp(
            int id,
            [FromBody] FacebookMetaAppRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.Name = request.Name.Trim();

            var metaApp = await _context.FacebookMetaApps
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (metaApp is null)
            {
                return NotFound(new { success = false, message = "Meta App was not found." });
            }

            if (string.IsNullOrWhiteSpace(request.UserId)
                || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { success = false, message = "User ID and Meta App name are required." });
            }

            var duplicateName = await _context.FacebookMetaApps
                .AnyAsync(
                    item => item.Id != id
                        && item.UserId == request.UserId
                        && item.Name == request.Name,
                    cancellationToken);
            if (duplicateName)
            {
                return Conflict(new { success = false, message = "A Meta App with this name already exists for this user." });
            }

            var normalizedWebhookKey = NormalizeWebhookKey(request.WebhookKey);
            if (!string.IsNullOrWhiteSpace(normalizedWebhookKey)
                && !string.Equals(normalizedWebhookKey, metaApp.WebhookKey, StringComparison.OrdinalIgnoreCase))
            {
                var duplicateWebhookKey = await _context.FacebookMetaApps
                    .AnyAsync(item => item.Id != id && item.WebhookKey == normalizedWebhookKey, cancellationToken);
                if (duplicateWebhookKey)
                {
                    return Conflict(new { success = false, message = "This webhook key is already used by another Meta App." });
                }

                metaApp.WebhookKey = normalizedWebhookKey;
            }

            metaApp.UserId = request.UserId;
            metaApp.Name = request.Name;
            metaApp.AppId = NormalizeNullable(request.AppId);
            metaApp.VerifyToken = string.IsNullOrWhiteSpace(request.VerifyToken)
                ? metaApp.VerifyToken
                : request.VerifyToken.Trim();
            metaApp.GraphApiVersion = NormalizeGraphApiVersion(request.GraphApiVersion);
            metaApp.IsDefault = request.IsDefault;
            metaApp.UpdatedAt = DateTime.UtcNow;

            var appSecret = NormalizeNullable(request.AppSecret);
            if (!string.IsNullOrWhiteSpace(appSecret))
            {
                metaApp.AppSecret = appSecret;
            }

            if (metaApp.IsDefault)
            {
                await ClearOtherDefaultsAsync(metaApp.UserId, metaApp.Id, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Facebook Meta App updated.",
                metaApp = ToResponse(metaApp)
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteMetaApp(int id, CancellationToken cancellationToken)
        {
            var metaApp = await _context.FacebookMetaApps
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (metaApp is null)
            {
                return NotFound(new { success = false, message = "Meta App was not found." });
            }

            var linkedPageCount = await _context.FacebookPages
                .CountAsync(item => item.FacebookMetaAppId == id, cancellationToken);
            if (linkedPageCount > 0)
            {
                return Conflict(new
                {
                    success = false,
                    message = $"This Meta App is linked to {linkedPageCount} Facebook Page(s). Move those pages first."
                });
            }

            _context.FacebookMetaApps.Remove(metaApp);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { success = true, message = "Facebook Meta App deleted." });
        }

        [HttpPost("page-subscriptions")]
        public async Task<IActionResult> SubscribePageWebhook(
            [FromBody] FacebookPageWebhookSubscriptionRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.PageId = request.PageId.Trim();

            var page = await _context.FacebookPages
                .AsNoTracking()
                .Include(item => item.FacebookMetaApp)
                .FirstOrDefaultAsync(
                    item => item.UserId == request.UserId && item.PageId == request.PageId,
                    cancellationToken);
            if (page is null)
            {
                return NotFound(new { success = false, message = "Facebook Page was not found." });
            }

            if (page.FacebookMetaApp is null)
            {
                return BadRequest(new { success = false, message = "Select a Meta App for this Facebook Page before subscribing the webhook." });
            }

            if (string.IsNullOrWhiteSpace(page.AccessToken))
            {
                return BadRequest(new { success = false, message = "Facebook Page access token is missing." });
            }

            var version = NormalizeGraphApiVersion(page.FacebookMetaApp.GraphApiVersion);
            var url = $"https://graph.facebook.com/{version}/{Uri.EscapeDataString(page.PageId)}/subscribed_apps";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["subscribed_fields"] = "feed",
                ["access_token"] = page.AccessToken
            });

            var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    message = $"Meta page subscription failed: {TrimForClient(responseBody)}"
                });
            }

            return Ok(new
            {
                success = true,
                message = $"Webhook subscribed for {page.PageName}.",
                metaApp = ToResponse(page.FacebookMetaApp)
            });
        }

        private async Task ClearOtherDefaultsAsync(
            string userId,
            int keepMetaAppId,
            CancellationToken cancellationToken)
        {
            var existingDefaults = await _context.FacebookMetaApps
                .Where(item => item.UserId == userId
                    && item.Id != keepMetaAppId
                    && item.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var existingDefault in existingDefaults)
            {
                existingDefault.IsDefault = false;
                existingDefault.UpdatedAt = DateTime.UtcNow;
            }
        }

        private async Task<string> BuildUniqueWebhookKeyAsync(
            string? requestedWebhookKey,
            string name,
            CancellationToken cancellationToken)
        {
            var webhookKey = NormalizeWebhookKey(requestedWebhookKey);
            if (!string.IsNullOrWhiteSpace(webhookKey)
                && !await _context.FacebookMetaApps.AnyAsync(item => item.WebhookKey == webhookKey, cancellationToken))
            {
                return webhookKey;
            }

            var baseKey = NormalizeWebhookKey(name);
            if (string.IsNullOrWhiteSpace(baseKey))
            {
                baseKey = "meta-app";
            }

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var candidate = $"{baseKey}-{RandomHex(4)}";
                if (!await _context.FacebookMetaApps.AnyAsync(item => item.WebhookKey == candidate, cancellationToken))
                {
                    return candidate;
                }
            }

            return $"meta-app-{RandomHex(8)}";
        }

        private static FacebookMetaAppResponse ToResponse(FacebookMetaApp metaApp)
        {
            return new FacebookMetaAppResponse
            {
                Id = metaApp.Id,
                UserId = metaApp.UserId,
                Name = metaApp.Name,
                AppId = metaApp.AppId,
                HasAppSecret = !string.IsNullOrWhiteSpace(metaApp.AppSecret),
                AppSecretLength = metaApp.AppSecret?.Length ?? 0,
                VerifyToken = metaApp.VerifyToken,
                WebhookKey = metaApp.WebhookKey,
                CallbackPath = $"/api/smapi/FacebookWebhooks/meta/{Uri.EscapeDataString(metaApp.WebhookKey)}",
                GraphApiVersion = metaApp.GraphApiVersion,
                IsDefault = metaApp.IsDefault,
                UpdatedAt = metaApp.UpdatedAt
            };
        }

        private static string NormalizeGraphApiVersion(string? graphApiVersion)
        {
            graphApiVersion = graphApiVersion?.Trim();
            if (string.IsNullOrWhiteSpace(graphApiVersion))
            {
                return "v24.0";
            }

            return graphApiVersion.StartsWith('v') ? graphApiVersion : $"v{graphApiVersion}";
        }

        private static string? NormalizeNullable(string? value)
        {
            value = value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string NormalizeWebhookKey(string? value)
        {
            value = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray();
            var key = new string(chars)
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Aggregate(string.Empty, (current, part) => string.IsNullOrEmpty(current) ? part : $"{current}-{part}");

            return key.Length <= 96 ? key : key[..96].Trim('-');
        }

        private static string GenerateVerifyToken()
        {
            return $"verify-{RandomHex(16)}";
        }

        private static string RandomHex(int byteCount)
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();
        }

        private static string TrimForClient(string value)
        {
            value = value.Trim();
            return value.Length <= 500 ? value : value[..500];
        }
    }
}
