using System.Net.Http.Json;
using System.Text.Json;

namespace Smapi.API.Services
{
    public interface IFacebookCommentsPublisher
    {
        Task<string?> ReplyToCommentAsync(
            string commentId,
            string pageAccessToken,
            string message,
            string graphApiVersion,
            string? mentionAuthorId,
            string? mentionAuthorName,
            CancellationToken cancellationToken);
    }

    public class FacebookCommentsPublisher : IFacebookCommentsPublisher
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FacebookCommentsPublisher> _logger;

        public FacebookCommentsPublisher(
            HttpClient httpClient,
            ILogger<FacebookCommentsPublisher> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string?> ReplyToCommentAsync(
            string commentId,
            string pageAccessToken,
            string message,
            string graphApiVersion,
            string? mentionAuthorId,
            string? mentionAuthorName,
            CancellationToken cancellationToken)
        {
            var version = NormalizeGraphApiVersion(graphApiVersion);
            var escapedCommentId = Uri.EscapeDataString(commentId);
            var replyUrl = $"https://graph.facebook.com/{version}/{escapedCommentId}/comments";
            var mentionReplyUrl = $"https://graph.facebook.com/{version}/{escapedCommentId}";
            var plainMessage = message.Trim();
            var mentionMessage = BuildMentionMessage(plainMessage, mentionAuthorId, mentionAuthorName);

            if (mentionMessage is null)
            {
                return await PostReplyAsync(replyUrl, pageAccessToken, plainMessage, cancellationToken);
            }

            try
            {
                return await PostReplyAsync(mentionReplyUrl, pageAccessToken, mentionMessage, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Facebook comment mention reply failed for comment {CommentId}; retrying without mention.",
                    commentId);

                return await PostReplyAsync(replyUrl, pageAccessToken, plainMessage, cancellationToken);
            }
        }

        private async Task<string?> PostReplyAsync(
            string url,
            string pageAccessToken,
            string message,
            CancellationToken cancellationToken)
        {
            using var content = JsonContent.Create(new
            {
                message,
                access_token = pageAccessToken
            });

            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Facebook comment reply failed with status {(int)response.StatusCode}: {FormatGraphError(responseBody)}");
            }

            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
        }

        private static string? BuildMentionMessage(
            string message,
            string? mentionAuthorId,
            string? mentionAuthorName)
        {
            mentionAuthorId = mentionAuthorId?.Trim();
            if (string.IsNullOrWhiteSpace(mentionAuthorId)
                || !mentionAuthorId.All(char.IsDigit))
            {
                return null;
            }

            message = RemoveLeadingAuthorName(message, mentionAuthorName);
            // Meta's Comments and @mentions API expects the mention token after
            // the reply text (for example: "your_message_text@[PSID]").
            return string.IsNullOrWhiteSpace(message)
                ? $"@[{mentionAuthorId}]"
                : $"{message} @[{mentionAuthorId}]";
        }

        private static string RemoveLeadingAuthorName(string message, string? authorName)
        {
            message = message.Trim();
            authorName = authorName?.Trim();
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(authorName))
            {
                return message;
            }

            if (!message.StartsWith(authorName, StringComparison.OrdinalIgnoreCase))
            {
                return message;
            }

            var remaining = message[authorName.Length..].TrimStart();
            remaining = remaining.TrimStart('-', ':', ',', '–', '—').TrimStart();
            return string.IsNullOrWhiteSpace(remaining) ? message : remaining;
        }

        private static string NormalizeGraphApiVersion(string graphApiVersion)
        {
            graphApiVersion = graphApiVersion.Trim();
            if (string.IsNullOrWhiteSpace(graphApiVersion))
            {
                return "v24.0";
            }

            return graphApiVersion.StartsWith('v') ? graphApiVersion : $"v{graphApiVersion}";
        }

        private static string FormatGraphError(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (!document.RootElement.TryGetProperty("error", out var error)
                    || error.ValueKind != JsonValueKind.Object)
                {
                    return TrimForLog(responseBody);
                }

                var message = GetOptionalString(error, "message");
                var type = GetOptionalString(error, "type");
                var code = GetOptionalString(error, "code");
                var traceId = GetOptionalString(error, "fbtrace_id");

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    parts.Add(message);
                }

                if (!string.IsNullOrWhiteSpace(type))
                {
                    parts.Add($"type={type}");
                }

                if (!string.IsNullOrWhiteSpace(code))
                {
                    parts.Add($"code={code}");
                }

                if (!string.IsNullOrWhiteSpace(traceId))
                {
                    parts.Add($"fbtrace_id={traceId}");
                }

                return parts.Count == 0 ? TrimForLog(responseBody) : TrimForLog(string.Join("; ", parts));
            }
            catch (JsonException)
            {
                return TrimForLog(responseBody);
            }
        }

        private static string? GetOptionalString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }
    }
}
