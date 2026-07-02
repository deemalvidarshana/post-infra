using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public interface IGeminiCaptionGenerator
    {
        Task<string?> GenerateCaptionAsync(
            string videoPath,
            FacebookPostUrl post,
            CancellationToken cancellationToken);
    }

    public class GeminiCaptionGenerator : IGeminiCaptionGenerator
    {
        private const string GlobalOpenRouterSettingUserId = "__global__";
        private const string GlobalGroqSettingUserId = "__global_groq__";
        private const string GlobalAiProviderSettingUserId = "__global_ai_provider__";
        private const string OpenRouterProvider = "openrouter";
        private const string GroqProvider = "groq";
        private const string OpenRouterChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
        private const string GroqChatCompletionsUrl = "https://api.groq.com/openai/v1/chat/completions";
        private readonly HttpClient _httpClient;
        private readonly SmapiDbContext _context;
        private readonly ILogger<GeminiCaptionGenerator> _logger;

        private sealed record AiProviderConfig(
            string Provider,
            string Label,
            string EndpointUrl,
            string Model,
            string ApiKey);

        public GeminiCaptionGenerator(
            HttpClient httpClient,
            SmapiDbContext context,
            ILogger<GeminiCaptionGenerator> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _logger = logger;
        }

        public async Task<string?> GenerateCaptionAsync(
            string videoPath,
            FacebookPostUrl post,
            CancellationToken cancellationToken)
        {
            var setting = await FindActiveAiProviderSettingAsync(cancellationToken);
            if (setting is null)
            {
                return null;
            }

            var pagePrompt = await FindRedNoteCaptionPromptAsync(post, cancellationToken);
            if (pagePrompt is null || string.IsNullOrWhiteSpace(pagePrompt.Prompt))
            {
                return null;
            }

            try
            {
                return await RequestCaptionAsync(setting, pagePrompt.Prompt, post, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI caption generation failed for post {PostId}.", post.Id);
                return null;
            }
        }

        private async Task<AiProviderConfig?> FindActiveAiProviderSettingAsync(CancellationToken cancellationToken)
        {
            var activeProviderSetting = await _context.GeminiSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == GlobalAiProviderSettingUserId, cancellationToken);
            var activeProvider = NormalizeProvider(activeProviderSetting?.Model);

            var openRouterSetting = await FindProviderSettingAsync(OpenRouterProvider, cancellationToken);
            var groqSetting = await FindProviderSettingAsync(GroqProvider, cancellationToken);

            if (activeProvider is null)
            {
                activeProvider = groqSetting is not null
                    ? GroqProvider
                    : OpenRouterProvider;
            }

            var selectedSetting = activeProvider == GroqProvider
                ? groqSetting
                : openRouterSetting;

            if (!IsConfiguredProviderSetting(activeProvider, selectedSetting))
            {
                return null;
            }

            return new AiProviderConfig(
                activeProvider,
                activeProvider == GroqProvider ? "Groq" : "OpenRouter",
                activeProvider == GroqProvider ? GroqChatCompletionsUrl : OpenRouterChatCompletionsUrl,
                selectedSetting!.Model,
                selectedSetting.ApiKey);
        }

        private async Task<GeminiSetting?> FindProviderSettingAsync(
            string provider,
            CancellationToken cancellationToken)
        {
            var userId = provider == GroqProvider
                ? GlobalGroqSettingUserId
                : GlobalOpenRouterSettingUserId;

            var setting = await _context.GeminiSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

            if (provider == OpenRouterProvider
                && setting is not null
                && IsLegacyDirectGeminiSetting(setting))
            {
                return null;
            }

            return setting;
        }

        private async Task<RedNoteCaptionPrompt?> FindRedNoteCaptionPromptAsync(
            FacebookPostUrl post,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(post.PageId))
            {
                return null;
            }

            return await _context.RedNoteCaptionPrompts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.UserId == post.UserId && item.PageId == post.PageId,
                    cancellationToken);
        }

        private async Task<string?> RequestCaptionAsync(
            AiProviderConfig setting,
            string prompt,
            FacebookPostUrl post,
            CancellationToken cancellationToken)
        {
            var payload = new
            {
                model = NormalizeModel(setting.Model),
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You write Facebook Reel captions for short-form social video pages."
                    },
                    new
                    {
                        role = "user",
                        content = BuildPrompt(prompt, post)
                    }
                },
                temperature = 0.7,
                max_tokens = 700
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                setting.EndpointUrl);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {setting.ApiKey.Trim()}");
            if (setting.Provider == OpenRouterProvider)
            {
                request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://smautomate.duckdns.org");
                request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "SM Automate");
            }
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "{Provider} caption request failed with status {StatusCode}: {Body}",
                    setting.Label,
                    (int)response.StatusCode,
                    TrimForLog(responseBody));
                return null;
            }

            var caption = ExtractText(responseBody);
            return CleanCaption(caption);
        }

        private static string BuildPrompt(string prompt, FacebookPostUrl post)
        {
            var builder = new StringBuilder();
            builder.AppendLine(prompt.Trim());
            builder.AppendLine();
            builder.AppendLine("No video frames are attached. Use the page prompt and available post metadata only.");
            builder.AppendLine("Follow the user's requested length, word count, language, and style exactly, even if it is longer than a typical social caption.");
            builder.AppendLine("Return only one final Facebook Reel caption text. Do not explain your reasoning.");

            if (!string.IsNullOrWhiteSpace(post.MusicName))
            {
                builder.AppendLine($"Music: {post.MusicName}");
            }

            if (!string.IsNullOrWhiteSpace(post.AuthorName))
            {
                builder.AppendLine($"Original creator/page: {post.AuthorName}");
            }

            return builder.ToString();
        }

        private static string NormalizeModel(string model)
        {
            model = model.Trim();
            return model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? model["models/".Length..]
                : model;
        }

        private static string? ExtractText(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
            {
                return ExtractContentText(content);
            }

            if (firstChoice.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            return null;
        }

        private static string? ExtractContentText(JsonElement content)
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            if (content.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    builder.Append(part.GetString());
                    continue;
                }

                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    builder.Append(textElement.GetString());
                }
            }

            return builder.ToString();
        }

        private static string? CleanCaption(string? caption)
        {
            caption = caption?.Trim();
            if (string.IsNullOrWhiteSpace(caption))
            {
                return null;
            }

            if (caption.StartsWith("```", StringComparison.Ordinal))
            {
                caption = caption.Trim('`').Trim();
            }

            if (caption.Length >= 2
                && caption.StartsWith('"')
                && caption.EndsWith('"'))
            {
                caption = caption[1..^1].Trim();
            }

            return caption.Length <= 2200 ? caption : caption[..2200].Trim();
        }

        private static bool IsConfiguredProviderSetting(string provider, GeminiSetting? setting)
        {
            if (setting is null
                || string.IsNullOrWhiteSpace(setting.Model)
                || string.IsNullOrWhiteSpace(setting.ApiKey))
            {
                return false;
            }

            return provider != OpenRouterProvider || !IsLegacyDirectGeminiSetting(setting);
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

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 500 ? value : value[..500];
        }

        private static bool IsLegacyDirectGeminiSetting(GeminiSetting setting)
        {
            var model = setting.Model?.Trim() ?? string.Empty;
            var apiKey = setting.ApiKey?.Trim() ?? string.Empty;

            return model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)
                || model.StartsWith("models/gemini-", StringComparison.OrdinalIgnoreCase)
                || apiKey.StartsWith("AIza", StringComparison.Ordinal);
        }
    }
}
