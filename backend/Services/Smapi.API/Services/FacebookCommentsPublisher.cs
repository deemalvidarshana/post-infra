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
            CancellationToken cancellationToken);
    }

    public class FacebookCommentsPublisher : IFacebookCommentsPublisher
    {
        private readonly HttpClient _httpClient;

        public FacebookCommentsPublisher(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string?> ReplyToCommentAsync(
            string commentId,
            string pageAccessToken,
            string message,
            string graphApiVersion,
            CancellationToken cancellationToken)
        {
            var version = NormalizeGraphApiVersion(graphApiVersion);
            var url = $"https://graph.facebook.com/{version}/{Uri.EscapeDataString(commentId)}/comments";

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["message"] = message,
                ["access_token"] = pageAccessToken
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
