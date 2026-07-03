using System.Text.Json;

namespace Smapi.API.Services
{
    public record FacebookReelPublishResult(string VideoId, string? PostId);
    public record FacebookStoryPublishResult(string? StoryId);

    public interface IFacebookReelsPublisher
    {
        Task<FacebookReelPublishResult> PublishAsync(
            string pageId,
            string pageAccessToken,
            string videoFilePath,
            string? caption,
            string graphApiVersion,
            CancellationToken cancellationToken);

        Task<FacebookStoryPublishResult> PublishStoryAsync(
            string pageId,
            string pageAccessToken,
            string videoId,
            string graphApiVersion,
            CancellationToken cancellationToken);
    }

    public class FacebookReelsPublisher : IFacebookReelsPublisher
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FacebookReelsPublisher> _logger;

        public FacebookReelsPublisher(HttpClient httpClient, ILogger<FacebookReelsPublisher> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<FacebookReelPublishResult> PublishAsync(
            string pageId,
            string pageAccessToken,
            string videoFilePath,
            string? caption,
            string graphApiVersion,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(videoFilePath))
            {
                throw new FileNotFoundException("Video file for upload was not found.", videoFilePath);
            }

            var version = NormalizeGraphApiVersion(graphApiVersion);
            var graphBaseUrl = $"https://graph.facebook.com/{version}";

            var startUrl = $"{graphBaseUrl}/{Uri.EscapeDataString(pageId)}/video_reels";
            _logger.LogInformation("Facebook Reel upload start. URL: {StartUrl}", startUrl);

            using var startContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["upload_phase"] = "start",
                ["access_token"] = pageAccessToken
            });

            using var startResponse = await _httpClient.PostAsync(startUrl, startContent, cancellationToken);
            var startJson = await ReadJsonAsync(startResponse, "Facebook Reel upload start", cancellationToken);

            var videoId = GetRequiredString(startJson, "video_id", "Facebook Reel upload start");
            var uploadUrl = GetRequiredString(startJson, "upload_url", "Facebook Reel upload start");
            _logger.LogInformation("Facebook Reel upload started. VideoId: {VideoId}, UploadUrl: {UploadUrl}", videoId, uploadUrl);

            var fileInfo = new FileInfo(videoFilePath);
            var fileSize = fileInfo.Length;
            _logger.LogInformation("Uploading video binary. Path: {Path}, Size: {Size} bytes", videoFilePath, fileSize);

            using var fileStream = File.OpenRead(videoFilePath);
            using var transferRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            transferRequest.Headers.TryAddWithoutValidation("Authorization", $"OAuth {pageAccessToken}");
            transferRequest.Headers.TryAddWithoutValidation("offset", "0");
            transferRequest.Headers.TryAddWithoutValidation("file_size", fileSize.ToString());
            
            // Send the file stream as the content
            transferRequest.Content = new StreamContent(fileStream);
            transferRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            _logger.LogInformation("Sending binary transfer request to Facebook...");
            using var transferResponse = await _httpClient.SendAsync(transferRequest, cancellationToken);
            await EnsureSuccessAsync(transferResponse, "Facebook Reel file transfer", cancellationToken);
            _logger.LogInformation("Binary transfer completed successfully.");

            var finishFields = new Dictionary<string, string>
            {
                ["upload_phase"] = "finish",
                ["video_id"] = videoId,
                ["video_state"] = "PUBLISHED",
                ["access_token"] = pageAccessToken
            };

            if (!string.IsNullOrWhiteSpace(caption))
            {
                finishFields["description"] = caption;
            }

            using var finishContent = new FormUrlEncodedContent(finishFields);
            using var finishResponse = await _httpClient.PostAsync(startUrl, finishContent, cancellationToken);
            var finishJson = await ReadJsonAsync(finishResponse, "Facebook Reel publish finish", cancellationToken);

            return new FacebookReelPublishResult(
                videoId,
                GetOptionalString(finishJson, "post_id")
                    ?? GetOptionalString(finishJson, "id"));
        }

        public async Task<FacebookStoryPublishResult> PublishStoryAsync(
            string pageId,
            string pageAccessToken,
            string videoId,
            string graphApiVersion,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(videoId))
            {
                throw new InvalidOperationException("Facebook video ID is required before publishing a Page Story.");
            }

            var version = NormalizeGraphApiVersion(graphApiVersion);
            var graphBaseUrl = $"https://graph.facebook.com/{version}";
            var storyUrl = $"{graphBaseUrl}/{Uri.EscapeDataString(pageId)}/video_stories";
            _logger.LogInformation("Facebook Page Story publish start. URL: {StoryUrl}, VideoId: {VideoId}", storyUrl, videoId);

            using var storyContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["video_id"] = videoId,
                ["access_token"] = pageAccessToken
            });

            using var storyResponse = await _httpClient.PostAsync(storyUrl, storyContent, cancellationToken);
            var storyJson = await ReadJsonAsync(storyResponse, "Facebook Page Story publish", cancellationToken);

            return new FacebookStoryPublishResult(
                GetOptionalString(storyJson, "post_id")
                    ?? GetOptionalString(storyJson, "id"));
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

        private static async Task<JsonDocument> ReadJsonAsync(
            HttpResponseMessage response,
            string context,
            CancellationToken cancellationToken)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{context} failed with status {(int)response.StatusCode}: {TrimForLog(responseBody)}");
            }

            try
            {
                return JsonDocument.Parse(responseBody);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{context} returned invalid JSON: {TrimForLog(responseBody)}", ex);
            }
        }

        private static async Task EnsureSuccessAsync(
            HttpResponseMessage response,
            string context,
            CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{context} failed with status {(int)response.StatusCode}: {TrimForLog(responseBody)}");
        }

        private static string GetRequiredString(JsonDocument document, string propertyName, string context)
        {
            var value = GetOptionalString(document, propertyName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} response did not include {propertyName}.");
            }

            return value;
        }

        private static string? GetOptionalString(JsonDocument document, string propertyName)
        {
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }
    }
}
