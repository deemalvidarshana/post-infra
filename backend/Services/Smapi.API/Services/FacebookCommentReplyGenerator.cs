using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public record FacebookCommentReplyContext(
        FacebookPage Page,
        FacebookAutoReplySetting Setting,
        FacebookCommentEvent Event);

    public interface IFacebookCommentReplyGenerator
    {
        Task<string> GenerateReplyAsync(
            FacebookCommentReplyContext context,
            CancellationToken cancellationToken);
    }

    public class FacebookCommentReplyGenerator : IFacebookCommentReplyGenerator
    {
        private const string GlobalOpenRouterSettingUserId = "__global__";
        private const string OpenRouterChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
        private readonly HttpClient _httpClient;
        private readonly SmapiDbContext _context;
        private readonly ILogger<FacebookCommentReplyGenerator> _logger;

        public FacebookCommentReplyGenerator(
            HttpClient httpClient,
            SmapiDbContext context,
            ILogger<FacebookCommentReplyGenerator> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _logger = logger;
        }

        public async Task<string> GenerateReplyAsync(
            FacebookCommentReplyContext context,
            CancellationToken cancellationToken)
        {
            var openRouterSetting = await FindGlobalOpenRouterSettingAsync(cancellationToken);
            if (openRouterSetting is null
                || string.IsNullOrWhiteSpace(openRouterSetting.ApiKey)
                || string.IsNullOrWhiteSpace(openRouterSetting.Model)
                || IsLegacyDirectGeminiSetting(openRouterSetting))
            {
                throw new InvalidOperationException("OpenRouter settings are not configured.");
            }

            var payload = new
            {
                model = NormalizeModel(openRouterSetting.Model),
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You write short, warm, natural Facebook comment replies for a storytelling reel page."
                    },
                    new
                    {
                        role = "user",
                        content = BuildPrompt(context)
                    }
                },
                temperature = 0.4,
                max_tokens = 100
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                OpenRouterChatCompletionsUrl);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {openRouterSetting.ApiKey.Trim()}");
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://smautomate.duckdns.org");
            request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "SM Automate");
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenRouter comment reply request failed with status {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    TrimForLog(responseBody));
                throw new InvalidOperationException($"OpenRouter reply generation failed with status {(int)response.StatusCode}.");
            }

            var reply = CleanReply(ExtractText(responseBody));
            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new InvalidOperationException("OpenRouter returned an empty reply.");
            }

            return reply;
        }

        private async Task<GeminiSetting?> FindGlobalOpenRouterSettingAsync(CancellationToken cancellationToken)
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

        private static string BuildPrompt(FacebookCommentReplyContext context)
        {
            var builder = new StringBuilder();
            builder.AppendLine(context.Setting.Prompt.Trim());
            builder.AppendLine();
            builder.AppendLine($"Facebook Page name: {context.Page.PageName}");
            builder.AppendLine($"Reply language: {context.Setting.Language}");
            builder.AppendLine($"Tone: {context.Setting.Tone}");
            builder.AppendLine($"Comment author: {context.Event.CommentAuthorName ?? "Unknown"}");
            builder.AppendLine($"Comment: {context.Event.CommentText}");
            builder.AppendLine();
            builder.AppendLine("Write one Facebook comment reply only.");
            builder.AppendLine("Reply in the same language as the comment: French for French comments and English for English comments.");
            builder.AppendLine("Write exactly one complete, natural sentence of no more than 20 words.");
            builder.AppendLine("Never stop mid-sentence and never end with an unfinished phrase.");
            builder.AppendLine("Keep it safe for public posting.");
            builder.AppendLine("Do not include explanations, markdown, hashtags, or quotation marks unless the comment requires them.");

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

        private static string? CleanReply(string? reply)
        {
            reply = reply?.Trim();
            if (string.IsNullOrWhiteSpace(reply))
            {
                return null;
            }

            if (reply.StartsWith("```", StringComparison.Ordinal))
            {
                reply = reply.Trim('`').Trim();
            }

            if (reply.Length >= 2 && reply.StartsWith('"') && reply.EndsWith('"'))
            {
                reply = reply[1..^1].Trim();
            }

            return reply.Length <= 2000 ? reply : reply[..2000].Trim();
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
