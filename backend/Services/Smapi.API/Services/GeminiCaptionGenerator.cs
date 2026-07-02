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
        private const string OpenRouterChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
        private readonly HttpClient _httpClient;
        private readonly SmapiDbContext _context;
        private readonly ILogger<GeminiCaptionGenerator> _logger;

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
            var setting = await FindGlobalGeminiSettingAsync(cancellationToken);
            if (setting is null || !IsConfigured(setting))
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
                _logger.LogWarning(ex, "OpenRouter caption generation failed for post {PostId}.", post.Id);
                return null;
            }
        }

        private async Task<GeminiSetting?> FindGlobalGeminiSettingAsync(CancellationToken cancellationToken)
        {
            return await _context.GeminiSettings
                .AsNoTracking()
                .Where(item => item.UserId == GlobalOpenRouterSettingUserId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _context.GeminiSettings
                    .AsNoTracking()
                    .OrderByDescending(item => item.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
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
            GeminiSetting setting,
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
                OpenRouterChatCompletionsUrl);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {setting.ApiKey.Trim()}");
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://smautomate.duckdns.org");
            request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "SM Automate");
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenRouter caption request failed with status {StatusCode}: {Body}",
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

        private static bool IsConfigured(GeminiSetting setting)
        {
            return !string.IsNullOrWhiteSpace(setting.Model)
                && !string.IsNullOrWhiteSpace(setting.ApiKey)
                && !IsLegacyDirectGeminiSetting(setting);
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
