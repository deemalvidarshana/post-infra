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
        private const string GlobalGeminiSettingUserId = "__global__";
        private const int FrameCount = 2;
        private readonly HttpClient _httpClient;
        private readonly SmapiDbContext _context;
        private readonly IVideoFrameExtractor _frameExtractor;
        private readonly ILogger<GeminiCaptionGenerator> _logger;

        public GeminiCaptionGenerator(
            HttpClient httpClient,
            SmapiDbContext context,
            IVideoFrameExtractor frameExtractor,
            ILogger<GeminiCaptionGenerator> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _frameExtractor = frameExtractor;
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

            var frameDirectory = Path.Combine(
                Path.GetTempPath(),
                "smapi-gemini-frames",
                post.Id.ToString(),
                Guid.NewGuid().ToString("N"));

            try
            {
                var frames = await _frameExtractor.ExtractRandomFramesAsync(
                    videoPath,
                    FrameCount,
                    frameDirectory,
                    cancellationToken);

                if (frames.Count == 0)
                {
                    _logger.LogWarning("No frames were extracted for post {PostId}; skipping Gemini caption.", post.Id);
                    return null;
                }

                return await RequestCaptionAsync(setting, pagePrompt.Prompt, frames, post, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini caption generation failed for post {PostId}.", post.Id);
                return null;
            }
            finally
            {
                TryDeleteDirectory(frameDirectory);
            }
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
            IReadOnlyList<ExtractedVideoFrame> frames,
            FacebookPostUrl post,
            CancellationToken cancellationToken)
        {
            var parts = new List<object>();
            foreach (var frame in frames.Take(FrameCount))
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
            }

            parts.Add(new { text = BuildPrompt(prompt, post) });

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts
                    }
                }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(NormalizeModel(setting.Model))}:generateContent");
            request.Headers.TryAddWithoutValidation("x-goog-api-key", setting.ApiKey.Trim());
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Gemini caption request failed with status {StatusCode}: {Body}",
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
            builder.AppendLine("Use the attached random frames from the downloaded video.");
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
                && !string.IsNullOrWhiteSpace(setting.ApiKey);
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
                // Frame snapshots are temporary and can be cleaned best-effort.
            }
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 500 ? value : value[..500];
        }
    }
}
