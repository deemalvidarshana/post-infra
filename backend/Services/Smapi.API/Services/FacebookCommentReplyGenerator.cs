using System.Net;
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
        private const string GlobalGroqSettingUserId = "__global_groq__";
        private const string GlobalAiProviderSettingUserId = "__global_ai_provider__";
        private const string OpenRouterProvider = "openrouter";
        private const string GroqProvider = "groq";
        private const string OpenRouterChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
        private const string GroqChatCompletionsUrl = "https://api.groq.com/openai/v1/chat/completions";
        private readonly HttpClient _httpClient;
        private readonly SmapiDbContext _context;
        private readonly ILogger<FacebookCommentReplyGenerator> _logger;

        private sealed record AiProviderConfig(
            string Provider,
            string Label,
            string EndpointUrl,
            string Model,
            string ApiKey);

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
            var aiProvider = await FindActiveAiProviderSettingAsync(cancellationToken);
            if (aiProvider is null)
            {
                throw new InvalidOperationException("AI provider settings are not configured.");
            }

            var payload = new
            {
                model = NormalizeModel(aiProvider.Model),
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
                aiProvider.EndpointUrl);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {aiProvider.ApiKey.Trim()}");
            if (aiProvider.Provider == OpenRouterProvider)
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
                    "{Provider} comment reply request failed with status {StatusCode}: {Body}",
                    aiProvider.Label,
                    (int)response.StatusCode,
                    TrimForLog(responseBody));

                if (ShouldUseFallback(response.StatusCode))
                {
                    return BuildFallbackReply(context, $"{aiProvider.Label} returned status {(int)response.StatusCode}");
                }

                throw new InvalidOperationException($"{aiProvider.Label} reply generation failed with status {(int)response.StatusCode}.");
            }

            string? generatedReply = null;
            try
            {
                generatedReply = ExtractText(responseBody);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Provider} comment reply response could not be parsed for page {PageId}, comment {CommentId}.",
                    aiProvider.Label,
                    context.Page.PageId,
                    context.Event.CommentId);
            }

            var reply = CleanReply(generatedReply);
            if (string.IsNullOrWhiteSpace(reply))
            {
                return BuildFallbackReply(context, $"{aiProvider.Label} returned an empty reply");
            }

            return reply;
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

        private string BuildFallbackReply(FacebookCommentReplyContext context, string reason)
        {
            _logger.LogWarning(
                "Using fallback Facebook comment reply because {Reason}. Page {PageId}, comment {CommentId}.",
                reason,
                context.Page.PageId,
                context.Event.CommentId);

            var comment = context.Event.CommentText?.Trim() ?? string.Empty;
            var preferredLanguage = ResolveReplyLanguage(comment, context.Setting.Language);

            if (preferredLanguage == "fr")
            {
                if (ContainsAny(comment, "suite", "partie", "parti", "p2", "p3", "part 2", "part 3", "svp", "svpl", "prochaine"))
                {
                    return "La suite arrive bientôt, restez connectés 😊";
                }

                if (LooksLikeShortReaction(comment))
                {
                    return "Merci pour votre soutien, ça nous fait vraiment plaisir 😊";
                }

                if (comment.Contains('?', StringComparison.Ordinal))
                {
                    return "Merci pour votre commentaire, on vous répond très bientôt 😊";
                }

                return "Merci pour votre commentaire, restez avec nous pour la suite 😊";
            }

            if (ContainsAny(comment, "part 2", "part 3", "next", "more", "continue", "episode", "please"))
            {
                return "The next part is coming soon, stay connected 😊";
            }

            if (LooksLikeShortReaction(comment))
            {
                return "Thanks for the love, we’re so happy the story touched you 😊";
            }

            if (comment.Contains('?', StringComparison.Ordinal))
            {
                return "Thanks for your comment, we’ll get back to you soon 😊";
            }

            return "Thanks for your comment, stay with us for the next part 😊";
        }

        private static bool ShouldUseFallback(HttpStatusCode statusCode)
        {
            var numericStatusCode = (int)statusCode;
            return statusCode == HttpStatusCode.TooManyRequests
                || numericStatusCode >= 500;
        }

        private static string ResolveReplyLanguage(string comment, string? configuredLanguage)
        {
            if (LooksFrench(comment))
            {
                return "fr";
            }

            if (!string.IsNullOrWhiteSpace(configuredLanguage)
                && configuredLanguage.Contains("french", StringComparison.OrdinalIgnoreCase))
            {
                return "fr";
            }

            return "en";
        }

        private static bool LooksFrench(string value)
        {
            return ContainsAny(
                value,
                "suite",
                "partie",
                "parti",
                "svp",
                "svpl",
                "merci",
                "bonjour",
                "prochaine",
                "histoire",
                "j'adore",
                "j’aime",
                "j'aime",
                "ça",
                "très",
                "où",
                "é",
                "è",
                "ê",
                "à",
                "ç");
        }

        private static bool LooksLikeShortReaction(string value)
        {
            var lettersAndDigits = value.Count(char.IsLetterOrDigit);
            return lettersAndDigits <= 12
                || ContainsAny(value, "❤️", "😍", "🥰", "😊", "😭", "😂", "😮", "love", "j'adore", "merci");
        }

        private static bool ContainsAny(string value, params string[] candidates)
        {
            return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
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
