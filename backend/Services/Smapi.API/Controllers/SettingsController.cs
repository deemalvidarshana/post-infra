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
        private const string GlobalOpenRouterSettingUserId = "__global__";
        private const string GlobalGroqSettingUserId = "__global_groq__";
        private const string GlobalAiProviderSettingUserId = "__global_ai_provider__";
        private const string OpenRouterProvider = "openrouter";
        private const string GroqProvider = "groq";
        private const string DefaultOpenRouterModel = "openrouter/free";
        private const string DefaultGroqModel = "llama-3.1-8b-instant";
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

        [HttpGet("openrouter")]
        public async Task<ActionResult<OpenRouterSettingResponse>> GetOpenRouterSettings(
            CancellationToken cancellationToken)
        {
            var setting = await FindGlobalOpenRouterSettingAsync(cancellationToken);

            if (setting is null || IsLegacyDirectGeminiSetting(setting))
            {
                return NotFound(new { success = false, message = "OpenRouter settings have not been saved yet." });
            }

            return Ok(ToOpenRouterResponse(setting));
        }

        [HttpGet("ai")]
        public async Task<ActionResult<AiProviderSettingResponse>> GetAiProviderSettings(
            CancellationToken cancellationToken)
        {
            var activeProvider = await FindActiveProviderAsync(cancellationToken);
            var openRouterSetting = await FindProviderSettingAsync(OpenRouterProvider, cancellationToken);
            var groqSetting = await FindProviderSettingAsync(GroqProvider, cancellationToken);

            if (string.IsNullOrWhiteSpace(activeProvider))
            {
                activeProvider = ResolveDefaultActiveProvider(openRouterSetting, groqSetting);
            }

            var activeSetting = string.Equals(activeProvider, GroqProvider, StringComparison.OrdinalIgnoreCase)
                ? groqSetting
                : openRouterSetting;

            return Ok(ToAiProviderResponse(activeProvider, activeSetting, openRouterSetting, groqSetting));
        }

        [HttpGet("openrouter/{userId}")]
        public Task<ActionResult<OpenRouterSettingResponse>> GetOpenRouterSettingsForLegacyUserRoute(
            string userId,
            CancellationToken cancellationToken)
        {
            return GetOpenRouterSettings(cancellationToken);
        }

        [HttpPut("openrouter")]
        public async Task<IActionResult> SaveOpenRouterSettings(
            [FromBody] OpenRouterSettingRequest request,
            CancellationToken cancellationToken)
        {
            var model = request.Model?.Trim();
            var apiKey = request.ApiKey?.Trim();

            if (string.IsNullOrWhiteSpace(model))
            {
                return BadRequest(new { success = false, message = "OpenRouter model is required." });
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return BadRequest(new { success = false, message = "OpenRouter API key is required." });
            }

            var existing = await _context.GeminiSettings
                .FirstOrDefaultAsync(item => item.UserId == GlobalOpenRouterSettingUserId, cancellationToken);
            var now = DateTime.UtcNow;

            if (existing is null)
            {
                existing = new GeminiSetting
                {
                    UserId = GlobalOpenRouterSettingUserId,
                    Model = model,
                    ApiKey = apiKey,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.GeminiSettings.Add(existing);
            }
            else
            {
                existing.Model = model;
                existing.ApiKey = apiKey;
                existing.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "OpenRouter settings saved successfully.",
                settings = ToOpenRouterResponse(existing)
            });
        }

        [HttpGet("groq")]
        public async Task<ActionResult<OpenRouterSettingResponse>> GetGroqSettings(
            CancellationToken cancellationToken)
        {
            var setting = await FindProviderSettingAsync(GroqProvider, cancellationToken);

            if (setting is null)
            {
                return NotFound(new { success = false, message = "Groq settings have not been saved yet." });
            }

            return Ok(ToProviderResponse(setting, GlobalGroqSettingUserId));
        }

        [HttpPut("groq")]
        public async Task<IActionResult> SaveGroqSettings(
            [FromBody] OpenRouterSettingRequest request,
            CancellationToken cancellationToken)
        {
            var model = string.IsNullOrWhiteSpace(request.Model)
                ? DefaultGroqModel
                : request.Model.Trim();
            var apiKey = request.ApiKey?.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return BadRequest(new { success = false, message = "Groq API key is required." });
            }

            var setting = await SaveProviderSettingAsync(
                GroqProvider,
                model,
                apiKey,
                cancellationToken);
            await SaveActiveProviderAsync(GroqProvider, cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Groq settings saved successfully.",
                settings = ToProviderResponse(setting, GlobalGroqSettingUserId)
            });
        }

        [HttpPut("ai")]
        public async Task<IActionResult> SaveAiProviderSettings(
            [FromBody] AiProviderSettingRequest request,
            CancellationToken cancellationToken)
        {
            var provider = NormalizeProvider(request.Provider);
            var model = request.Model?.Trim();
            var apiKey = request.ApiKey?.Trim();

            if (provider is null)
            {
                return BadRequest(new { success = false, message = "AI provider must be OpenRouter or Groq." });
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                model = GetDefaultModel(provider);
            }

            var existing = await FindProviderSettingForUpdateAsync(provider, cancellationToken);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (existing is null || string.IsNullOrWhiteSpace(existing.ApiKey))
                {
                    return BadRequest(new { success = false, message = $"{GetProviderLabel(provider)} API key is required." });
                }

                apiKey = existing.ApiKey;
            }

            var setting = await SaveProviderSettingAsync(provider, model, apiKey, cancellationToken);
            await SaveActiveProviderAsync(provider, cancellationToken);

            var openRouterSetting = provider == OpenRouterProvider
                ? setting
                : await FindProviderSettingAsync(OpenRouterProvider, cancellationToken);
            var groqSetting = provider == GroqProvider
                ? setting
                : await FindProviderSettingAsync(GroqProvider, cancellationToken);

            return Ok(new
            {
                success = true,
                message = $"{GetProviderLabel(provider)} settings saved successfully.",
                settings = ToAiProviderResponse(provider, setting, openRouterSetting, groqSetting)
            });
        }

        [HttpGet("gemini")]
        public async Task<ActionResult<OpenRouterSettingResponse>> GetGeminiSettingsLegacy(
            CancellationToken cancellationToken)
        {
            return await GetOpenRouterSettings(cancellationToken);
        }

        [HttpGet("gemini/{userId}")]
        public Task<ActionResult<OpenRouterSettingResponse>> GetGeminiSettingsForLegacyUserRoute(
            string userId,
            CancellationToken cancellationToken)
        {
            return GetOpenRouterSettings(cancellationToken);
        }

        [HttpPut("gemini")]
        public Task<IActionResult> SaveGeminiSettingsLegacy(
            [FromBody] OpenRouterSettingRequest request,
            CancellationToken cancellationToken)
        {
            return SaveOpenRouterSettings(request, cancellationToken);
        }

        private async Task<GeminiSetting?> FindGlobalOpenRouterSettingAsync(CancellationToken cancellationToken)
        {
            var setting = await _context.GeminiSettings
                .AsNoTracking()
                .Where(item => item.UserId == GlobalOpenRouterSettingUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (setting is not null)
            {
                return setting;
            }

            return await _context.GeminiSettings
                .AsNoTracking()
                .Where(item => item.UserId != GlobalGroqSettingUserId
                    && item.UserId != GlobalAiProviderSettingUserId)
                .OrderByDescending(item => item.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static OpenRouterSettingResponse ToOpenRouterResponse(GeminiSetting setting)
        {
            return ToProviderResponse(setting, GlobalOpenRouterSettingUserId);
        }

        private static OpenRouterSettingResponse ToProviderResponse(GeminiSetting setting, string userId)
        {
            return new OpenRouterSettingResponse
            {
                UserId = userId,
                Model = setting.Model,
                ApiKey = null,
                HasApiKey = !string.IsNullOrWhiteSpace(setting.ApiKey),
                ApiKeyLength = setting.ApiKey?.Length ?? 0,
                UpdatedAt = setting.UpdatedAt
            };
        }

        private static bool IsLegacyDirectGeminiSetting(GeminiSetting setting)
        {
            var model = setting.Model?.Trim() ?? string.Empty;
            var apiKey = setting.ApiKey?.Trim() ?? string.Empty;

            return model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)
                || model.StartsWith("models/gemini-", StringComparison.OrdinalIgnoreCase)
                || apiKey.StartsWith("AIza", StringComparison.Ordinal);
        }

        private async Task<string?> FindActiveProviderAsync(CancellationToken cancellationToken)
        {
            var active = await _context.GeminiSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == GlobalAiProviderSettingUserId, cancellationToken);

            return NormalizeProvider(active?.Model);
        }

        private async Task SaveActiveProviderAsync(string provider, CancellationToken cancellationToken)
        {
            var existing = await _context.GeminiSettings
                .FirstOrDefaultAsync(item => item.UserId == GlobalAiProviderSettingUserId, cancellationToken);
            var now = DateTime.UtcNow;

            if (existing is null)
            {
                existing = new GeminiSetting
                {
                    UserId = GlobalAiProviderSettingUserId,
                    Model = provider,
                    ApiKey = "active",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.GeminiSettings.Add(existing);
            }
            else
            {
                existing.Model = provider;
                existing.ApiKey = "active";
                existing.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        private async Task<GeminiSetting?> FindProviderSettingAsync(
            string provider,
            CancellationToken cancellationToken)
        {
            var userId = GetProviderSettingUserId(provider);
            var setting = await _context.GeminiSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

            if (provider == OpenRouterProvider && (setting is null || IsLegacyDirectGeminiSetting(setting)))
            {
                return null;
            }

            return setting;
        }

        private async Task<GeminiSetting?> FindProviderSettingForUpdateAsync(
            string provider,
            CancellationToken cancellationToken)
        {
            var userId = GetProviderSettingUserId(provider);
            return await _context.GeminiSettings
                .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        }

        private async Task<GeminiSetting> SaveProviderSettingAsync(
            string provider,
            string model,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var existing = await FindProviderSettingForUpdateAsync(provider, cancellationToken);
            var now = DateTime.UtcNow;
            var userId = GetProviderSettingUserId(provider);

            if (existing is null)
            {
                existing = new GeminiSetting
                {
                    UserId = userId,
                    Model = model,
                    ApiKey = apiKey,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.GeminiSettings.Add(existing);
            }
            else
            {
                existing.Model = model;
                existing.ApiKey = apiKey;
                existing.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return existing;
        }

        private static AiProviderSettingResponse ToAiProviderResponse(
            string activeProvider,
            GeminiSetting? activeSetting,
            GeminiSetting? openRouterSetting,
            GeminiSetting? groqSetting)
        {
            activeProvider = NormalizeProvider(activeProvider) ?? GroqProvider;
            var activeOption = ToAiProviderOption(activeProvider, activeSetting, activeProvider);

            return new AiProviderSettingResponse
            {
                ActiveProvider = activeProvider,
                Provider = activeProvider,
                Label = activeOption.Label,
                Model = activeOption.Model,
                HasApiKey = activeOption.HasApiKey,
                ApiKeyLength = activeOption.ApiKeyLength,
                UpdatedAt = activeOption.UpdatedAt,
                Providers = new[]
                {
                    ToAiProviderOption(OpenRouterProvider, openRouterSetting, activeProvider),
                    ToAiProviderOption(GroqProvider, groqSetting, activeProvider)
                }
            };
        }

        private static AiProviderOptionResponse ToAiProviderOption(
            string provider,
            GeminiSetting? setting,
            string activeProvider)
        {
            return new AiProviderOptionResponse
            {
                Provider = provider,
                Label = GetProviderLabel(provider),
                DefaultModel = GetDefaultModel(provider),
                Model = setting?.Model ?? GetDefaultModel(provider),
                HasApiKey = !string.IsNullOrWhiteSpace(setting?.ApiKey),
                ApiKeyLength = setting?.ApiKey?.Length ?? 0,
                IsActive = string.Equals(provider, activeProvider, StringComparison.OrdinalIgnoreCase),
                UpdatedAt = setting?.UpdatedAt
            };
        }

        private static string ResolveDefaultActiveProvider(
            GeminiSetting? openRouterSetting,
            GeminiSetting? groqSetting)
        {
            if (groqSetting is not null && !string.IsNullOrWhiteSpace(groqSetting.ApiKey))
            {
                return GroqProvider;
            }

            if (openRouterSetting is not null && !string.IsNullOrWhiteSpace(openRouterSetting.ApiKey))
            {
                return OpenRouterProvider;
            }

            return GroqProvider;
        }

        private static string? NormalizeProvider(string? provider)
        {
            provider = provider?.Trim().ToLowerInvariant();
            return provider switch
            {
                OpenRouterProvider => OpenRouterProvider,
                GroqProvider => GroqProvider,
                _ => null
            };
        }

        private static string GetProviderSettingUserId(string provider)
        {
            return provider == GroqProvider
                ? GlobalGroqSettingUserId
                : GlobalOpenRouterSettingUserId;
        }

        private static string GetDefaultModel(string provider)
        {
            return provider == GroqProvider
                ? DefaultGroqModel
                : DefaultOpenRouterModel;
        }

        private static string GetProviderLabel(string provider)
        {
            return provider == GroqProvider ? "Groq" : "OpenRouter";
        }
    }
}
