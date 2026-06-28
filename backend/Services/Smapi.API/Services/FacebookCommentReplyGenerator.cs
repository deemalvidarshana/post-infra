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
        private const int VideoFrameCount = 6;
        private readonly HttpClient _httpClient;
        private readonly SmapiDbContext _context;
        private readonly IVideoFrameExtractor _frameExtractor;
        private readonly ILocalVideoStorageService _storage;
        private readonly ILogger<FacebookCommentReplyGenerator> _logger;

        public FacebookCommentReplyGenerator(
            HttpClient httpClient,
            SmapiDbContext context,
            IVideoFrameExtractor frameExtractor,
            ILocalVideoStorageService storage,
            ILogger<FacebookCommentReplyGenerator> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _frameExtractor = frameExtractor;
            _storage = storage;
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

            var parts = await BuildContentPartsAsync(context, cancellationToken);
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts
                    }
                },
                generationConfig = new
                {
                    temperature = 0.4,
                    maxOutputTokens = 256
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

        private async Task<List<object>> BuildContentPartsAsync(
            FacebookCommentReplyContext context,
            CancellationToken cancellationToken)
        {
            var parts = new List<object>();
            var postContext = await FindPostContextAsync(context, cancellationToken);
            var frameDirectory = Path.Combine(
                Path.GetTempPath(),
                "smapi-comment-reply-frames",
                context.Event.Id.ToString(),
                Guid.NewGuid().ToString("N"));
            var attachedFrameCount = 0;

            try
            {
                if (!string.IsNullOrWhiteSpace(postContext.VideoPath)
                    && File.Exists(postContext.VideoPath))
                {
                    var frames = await _frameExtractor.ExtractRandomFramesAsync(
                        postContext.VideoPath,
                        VideoFrameCount,
                        frameDirectory,
                        cancellationToken);

                    foreach (var frame in frames.Take(VideoFrameCount))
                    {
                        var bytes = await File.ReadAllBytesAsync(frame.Path, cancellationToken);
                        parts.Add(new
                        {
                            inline_data = new
                            {
                                mime_type = frame.MimeType,
                                data = Convert.ToBase64String(bytes)
                            }
                        });
                        attachedFrameCount += 1;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not extract video context for Facebook comment event {EventId}; using text context only.",
                    context.Event.Id);
            }
            finally
            {
                TryDeleteDirectory(frameDirectory);
            }

            parts.Add(new { text = BuildPrompt(context, postContext, attachedFrameCount) });
            return parts;
        }

        private async Task<FacebookCommentPostContext> FindPostContextAsync(
            FacebookCommentReplyContext context,
            CancellationToken cancellationToken)
        {
            var postId = context.Event.PostId?.Trim();
            if (string.IsNullOrWhiteSpace(postId))
            {
                return new FacebookCommentPostContext(null, null, null);
            }

            var objectId = postId.Contains('_', StringComparison.Ordinal)
                ? postId[(postId.LastIndexOf('_') + 1)..]
                : postId;

            var job = await _context.FacebookReelUploadJobs
                .AsNoTracking()
                .Include(item => item.FacebookPostUrl)
                .Where(item => item.PageId == context.Page.PageId)
                .Where(item => item.FacebookPostId == postId || item.FacebookPostId == objectId)
                .OrderByDescending(item => item.CompletedAt ?? item.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            string? videoPath = null;
            if (!string.IsNullOrWhiteSpace(job?.S3Key))
            {
                try
                {
                    var candidatePath = _storage.GetAbsolutePath(job.S3Key);
                    if (File.Exists(candidatePath))
                    {
                        videoPath = candidatePath;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Could not resolve stored video for Facebook comment event {EventId}.",
                        context.Event.Id);
                }
            }

            return new FacebookCommentPostContext(
                CleanContextText(job?.Caption),
                CleanContextText(job?.FacebookPostUrl?.Caption),
                videoPath);
        }

        private static string BuildPrompt(
            FacebookCommentReplyContext context,
            FacebookCommentPostContext postContext,
            int attachedFrameCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine(context.Setting.Prompt.Trim());
            builder.AppendLine();
            builder.AppendLine($"Facebook Page name: {context.Page.PageName}");
            builder.AppendLine($"Reply language: {context.Setting.Language}");
            builder.AppendLine($"Tone: {context.Setting.Tone}");
            builder.AppendLine($"Comment author: {context.Event.CommentAuthorName ?? "Unknown"}");
            builder.AppendLine($"Comment: {context.Event.CommentText}");

            if (attachedFrameCount > 0)
            {
                builder.AppendLine($"Video context: {attachedFrameCount} frames from this exact Facebook video are attached.");
                builder.AppendLine("Use the frames to understand the characters, situation, and emotional tone before replying.");
            }

            if (!string.IsNullOrWhiteSpace(postContext.PublishedCaption))
            {
                builder.AppendLine($"Published post caption: {postContext.PublishedCaption}");
            }

            if (!string.IsNullOrWhiteSpace(postContext.SourceCaption)
                && !string.Equals(
                    postContext.SourceCaption,
                    postContext.PublishedCaption,
                    StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"Source video caption: {postContext.SourceCaption}");
            }

            builder.AppendLine();
            builder.AppendLine("Write one Facebook comment reply only.");
            builder.AppendLine("Keep it short, natural, and safe for public posting.");
            builder.AppendLine("Treat emoji-only comments as reactions to the post and its emotional tone, not automatically as a personal problem.");
            builder.AppendLine("Return a complete reply; never end mid-sentence.");
            builder.AppendLine("Do not include explanations, markdown, hashtags, or quotation marks unless the comment requires them.");

            return builder.ToString();
        }

        private static string? CleanContextText(string? value)
        {
            value = value?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Length <= 1000 ? value : value[..1000].Trim();
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

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Temporary frame cleanup is best effort.
            }
        }

        private sealed record FacebookCommentPostContext(
            string? PublishedCaption,
            string? SourceCaption,
            string? VideoPath);
    }
}
