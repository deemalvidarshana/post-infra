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
        private const string GlobalGeminiSettingUserId = "__global__";
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
            var geminiSetting = await FindGlobalGeminiSettingAsync(cancellationToken);
            if (geminiSetting is null
                || string.IsNullOrWhiteSpace(geminiSetting.ApiKey)
                || string.IsNullOrWhiteSpace(geminiSetting.Model))
            {
                throw new InvalidOperationException("Gemini settings are not configured.");
            }

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = BuildPrompt(context) }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.4,
                    maxOutputTokens = 100,
                    thinkingConfig = new
                    {
                        thinkingBudget = 0
                    }
                }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(NormalizeModel(geminiSetting.Model))}:generateContent");
            request.Headers.TryAddWithoutValidation("x-goog-api-key", geminiSetting.ApiKey.Trim());
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Gemini comment reply request failed with status {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    TrimForLog(responseBody));
                throw new InvalidOperationException($"Gemini reply generation failed with status {(int)response.StatusCode}.");
            }

            var reply = CleanReply(ExtractText(responseBody));
            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new InvalidOperationException("Gemini returned an empty reply.");
            }

            return reply;
        }

        private async Task<GeminiSetting?> FindGlobalGeminiSettingAsync(CancellationToken cancellationToken)
        {
            return await _context.GeminiSettings
                .AsNoTracking()
                .Where(item => item.UserId == GlobalGeminiSettingUserId)
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
            if (!document.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement))
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
    }
}
