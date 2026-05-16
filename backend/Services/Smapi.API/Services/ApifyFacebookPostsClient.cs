using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Smapi.API.Data;
using Smapi.API.Models.DTOs;

namespace Smapi.API.Services
{
    public interface IApifyFacebookPostsClient
    {
        Task<IReadOnlyList<ApifyFacebookPostItem>> ScrapePostsAsync(FacebookScrapeRequest request, CancellationToken cancellationToken);
    }

    public class ApifyFacebookPostsClient : IApifyFacebookPostsClient
    {
        private const string GlobalApifySettingUserId = "__global__";
        private const int MaxActorAttempts = 3;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly SmapiDbContext _context;
        private readonly ILogger<ApifyFacebookPostsClient> _logger;

        public ApifyFacebookPostsClient(
            HttpClient httpClient,
            IConfiguration configuration,
            SmapiDbContext context,
            ILogger<ApifyFacebookPostsClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _context = context;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ApifyFacebookPostItem>> ScrapePostsAsync(FacebookScrapeRequest request, CancellationToken cancellationToken)
        {
            var token = await ResolveApiTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Save an Apify API key in Settings before scraping.");
            }

            var reelsActorId = _configuration["Apify:FacebookReelsActorId"] ?? "apify~facebook-reels-scraper";
            var reelsInput = new
            {
                onlyPostsNewerThan = request.OnlyPostsNewerThan?.ToString("yyyy-MM-dd"),
                resultsLimit = request.ResultsLimit,
                startUrls = request.StartUrls.Select(url => new { url = url.Url }).ToList()
            };

            var reelItems = await RunActorAsync(reelsActorId, reelsInput, token, cancellationToken);
            await TryEnrichCaptionsAsync(reelItems, request.StartUrls, request.ResultsLimit, token, cancellationToken);

            return reelItems;
        }

        private async Task TryEnrichCaptionsAsync(
            IReadOnlyList<ApifyFacebookPostItem> reelItems,
            IReadOnlyList<FacebookStartUrl> startUrls,
            int resultsLimit,
            string token,
            CancellationToken cancellationToken)
        {
            if (bool.TryParse(_configuration["Apify:EnrichMissingCaptions"], out var enrichMissingCaptions)
                && !enrichMissingCaptions)
            {
                return;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                await EnrichCaptionsAsync(reelItems, startUrls, resultsLimit, token, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Apify caption enrichment timed out. Continuing with Reel scraper results.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Apify caption enrichment failed. Continuing with Reel scraper results.");
            }
        }

        private async Task<string?> ResolveApiTokenAsync(CancellationToken cancellationToken)
        {
            var savedToken = await _context.ApifySettings
                .AsNoTracking()
                .Where(item => item.UserId == GlobalApifySettingUserId)
                .Select(item => item.ApiToken)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _context.ApifySettings
                    .AsNoTracking()
                    .OrderByDescending(item => item.UpdatedAt)
                    .Select(item => item.ApiToken)
                    .FirstOrDefaultAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(savedToken)
                ? _configuration["Apify:ApiToken"]?.Trim()
                : savedToken.Trim();
        }

        private async Task<List<ApifyFacebookPostItem>> RunActorAsync(
            string actorId,
            object actorInput,
            string token,
            CancellationToken cancellationToken)
        {
            actorId = actorId.Replace("/", "~", StringComparison.Ordinal);

            var endpoint = $"https://api.apify.com/v2/acts/{actorId}/run-sync-get-dataset-items?token={Uri.EscapeDataString(token)}&format=json&clean=true";
            Exception? lastError = null;

            for (var attempt = 1; attempt <= MaxActorAttempts; attempt++)
            {
                try
                {
                    using var response = await _httpClient.PostAsJsonAsync(endpoint, actorInput, JsonOptions, cancellationToken);
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var message = $"Apify actor {actorId} request failed with status {(int)response.StatusCode}: {TrimForLog(responseBody)}";
                        if (attempt < MaxActorAttempts && IsTransientStatusCode(response.StatusCode))
                        {
                            lastError = new InvalidOperationException(message);
                            await DelayBeforeRetryAsync(attempt, cancellationToken);
                            continue;
                        }

                        throw new InvalidOperationException(message);
                    }

                    var items = JsonSerializer.Deserialize<List<ApifyFacebookPostItem>>(responseBody, JsonOptions);
                    return items ?? [];
                }
                catch (Exception ex) when (attempt < MaxActorAttempts && IsTransientApifyFailure(ex))
                {
                    lastError = ex;
                    _logger.LogWarning(ex, "Apify actor {ActorId} request failed on attempt {Attempt}. Retrying.", actorId, attempt);
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                }
            }

            throw new InvalidOperationException(BuildApifyFailureMessage(lastError, actorId), lastError);
        }

        private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
        {
            return Task.Delay(TimeSpan.FromSeconds(attempt * 3), cancellationToken);
        }

        private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            return statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
        }

        private static bool IsTransientApifyFailure(Exception exception)
        {
            var message = exception.ToString();
            return exception is HttpRequestException
                || exception is TaskCanceledException
                || message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection refused", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildApifyFailureMessage(Exception? exception, string actorId)
        {
            if (exception is null)
            {
                return $"Apify actor {actorId} failed after retries.";
            }

            var message = exception.GetBaseException().Message.Trim();
            if (message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase))
            {
                return $"Apify DNS lookup failed after retries. Check internet/DNS/VPN on this backend machine, then retry. Raw error: {message}";
            }

            return $"Apify actor {actorId} failed after retries: {TrimForLog(message)}";
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }

        private async Task EnrichCaptionsAsync(
            IReadOnlyList<ApifyFacebookPostItem> reelItems,
            IReadOnlyList<FacebookStartUrl> startUrls,
            int resultsLimit,
            string token,
            CancellationToken cancellationToken)
        {
            var missingCaptionItems = reelItems
                .Select(item => new
                {
                    Item = item,
                    Url = NormalizeUrl(item.GetPermalinkUrl())
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Url) && string.IsNullOrWhiteSpace(item.Item.GetCaption()))
                .GroupBy(item => item.Url!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (missingCaptionItems.Count == 0)
            {
                return;
            }

            var postsActorId = _configuration["Apify:FacebookPostsActorId"] ?? "apify~facebook-posts-scraper";
            var enrichmentUrls = startUrls
                .Select(item => item.Url)
                .Concat(missingCaptionItems.Select(item => item.Url!))
                .Select(NormalizeUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(url => new { url })
                .ToList();

            var postsInput = new
            {
                captionText = true,
                resultsLimit = Math.Clamp(Math.Max(resultsLimit, missingCaptionItems.Count), 1, 1000),
                startUrls = enrichmentUrls
            };

            var captionItems = await RunActorAsync(postsActorId, postsInput, token, cancellationToken);
            var captionsByUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var captionsById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var captionItem in captionItems)
            {
                var caption = captionItem.GetCaption();
                if (string.IsNullOrWhiteSpace(caption))
                {
                    continue;
                }

                foreach (var candidateUrl in captionItem.GetCandidateUrls().Select(NormalizeUrl))
                {
                    if (!string.IsNullOrWhiteSpace(candidateUrl))
                    {
                        captionsByUrl.TryAdd(candidateUrl, caption);
                    }
                }

                var itemId = captionItem.GetItemId();
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    captionsById.TryAdd(itemId, caption);
                }
            }

            foreach (var missingCaptionItem in missingCaptionItems)
            {
                var itemId = missingCaptionItem.Item.GetItemId();
                if (!string.IsNullOrWhiteSpace(itemId) && captionsById.TryGetValue(itemId, out var captionById))
                {
                    missingCaptionItem.Item.Caption = captionById;
                    continue;
                }

                foreach (var candidateUrl in missingCaptionItem.Item.GetCandidateUrls().Select(NormalizeUrl))
                {
                    if (!string.IsNullOrWhiteSpace(candidateUrl)
                        && captionsByUrl.TryGetValue(candidateUrl, out var captionByUrl))
                    {
                        missingCaptionItem.Item.Caption = captionByUrl;
                        break;
                    }
                }
            }
        }

        private static string? NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return url.Trim();
            }

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
                Query = string.Empty
            };

            return builder.Uri.ToString().TrimEnd('/');
        }
    }

    public class ApifyFacebookPostItem
    {
        [JsonPropertyName("post_id")]
        public string? PostId { get; set; }

        [JsonPropertyName("permalink_url")]
        public string? PermalinkUrl { get; set; }

        [JsonPropertyName("profile_link")]
        public string? ProfileLink { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("caption")]
        public string? Caption { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }

        public string? GetPermalinkUrl()
        {
            return FirstNonEmpty(
                GetString("shareable_url"),
                GetString("topLevelReelUrl"),
                GetString("if_should_change_url_for_reels", "shareable_url"),
                GetString("short_form_video_context", "shareable_url"),
                GetString("playback_video", "permalink_url"),
                GetString("short_form_video_context", "playback_video", "permalink_url"),
                PermalinkUrl,
                GetString("reelUrl"),
                GetString("reel_url"),
                GetString("url"),
                GetString("facebookUrl"));
        }

        public string? GetSourcePageUrl()
        {
            return FirstNonEmpty(
                GetString("inputUrl"),
                GetString("facebookUrl"),
                ProfileLink,
                GetString("video_owner", "url"),
                GetString("short_form_video_context", "video_owner", "url"),
                GetString("pageUrl"),
                GetString("page_url"),
                GetString("profileUrl"),
                GetString("source_url"),
                GetString("sourceUrl"));
        }

        public string? GetItemId()
        {
            return FirstNonEmpty(
                PostId,
                GetString("facebookId"),
                GetString("video", "id"),
                GetString("short_form_video_context", "video", "id"),
                GetString("playback_video", "id"),
                GetString("short_form_video_context", "playback_video", "id"),
                GetString("tracking", "video_id"),
                GetString("tracking", "top_level_post_id"));
        }

        public string? GetCaption()
        {
            return FirstNonEmpty(
                Text,
                Caption,
                GetString("message", "text"),
                GetString("captionText"),
                GetString("message"),
                GetString("description"),
                GetString("title"));
        }

        public string? GetVideoUrl()
        {
            return FirstNonEmpty(
                GetString("videoUrl"),
                GetString("videoURL"),
                GetString("video_url"),
                GetString("videoDownloadUrl"),
                GetString("video_download_url"),
                GetString("downloadUrl"),
                GetString("download_url"),
                GetString("hdUrl"),
                GetString("hd_url"),
                GetString("sdUrl"),
                GetString("sd_url"),
                GetString("playableUrl"),
                GetString("playable_url"),
                GetString("playable_url_quality_hd"),
                GetString("source"),
                GetString("media", "source"),
                GetString("media", "playable_url"),
                GetString("video", "url"),
                GetString("video", "source"),
                GetString("video", "playable_url"),
                GetString("playback_video", "url"),
                GetString("playback_video", "source"),
                GetString("playback_video", "playable_url"),
                GetString("short_form_video_context", "video", "url"),
                GetString("short_form_video_context", "video", "source"),
                GetString("short_form_video_context", "video", "playable_url"),
                GetString("short_form_video_context", "playback_video", "url"),
                GetString("short_form_video_context", "playback_video", "source"),
                GetString("short_form_video_context", "playback_video", "playable_url"));
        }

        public DateTime? GetCreatedAt()
        {
            return CreatedAt
                ?? GetDateTime("timestamp")
                ?? GetDateTime("date")
                ?? GetDateTime("time")
                ?? GetUnixDateTime("creation_time");
        }

        public IEnumerable<string?> GetCandidateUrls()
        {
            yield return GetPermalinkUrl();
            yield return GetSourcePageUrl();
            yield return GetString("short_form_video_context", "shareable_url");
            yield return GetString("short_form_video_context", "playback_video", "permalink_url");

            var reelId = FirstNonEmpty(
                GetString("facebookId"),
                GetString("short_form_video_context", "video", "id"),
                GetString("short_form_video_context", "playback_video", "id"),
                GetString("video", "id"),
                GetString("playback_video", "id"));

            if (!string.IsNullOrWhiteSpace(reelId))
            {
                yield return $"https://www.facebook.com/reel/{reelId}";
            }
        }

        private string? GetString(string propertyName)
        {
            if (ExtraFields is null || !ExtraFields.TryGetValue(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null
            };
        }

        private string? GetString(params string[] propertyPath)
        {
            if (ExtraFields is null || propertyPath.Length == 0 || !ExtraFields.TryGetValue(propertyPath[0], out var value))
            {
                return null;
            }

            for (var index = 1; index < propertyPath.Length; index++)
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyPath[index], out value))
                {
                    return null;
                }
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null
            };
        }

        private DateTime? GetDateTime(string propertyName)
        {
            var value = GetString(propertyName);
            if (DateTimeOffset.TryParse(value, out var dateTimeOffset))
            {
                return dateTimeOffset.UtcDateTime;
            }

            return DateTime.TryParse(value, out var dateTime)
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : null;
        }

        private DateTime? GetUnixDateTime(string propertyName)
        {
            if (ExtraFields is null || !ExtraFields.TryGetValue(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unixSeconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
            }

            var rawValue = GetString(propertyName);
            return long.TryParse(rawValue, out unixSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
                : null;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
    }
}
