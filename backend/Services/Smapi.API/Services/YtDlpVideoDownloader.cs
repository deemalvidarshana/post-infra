using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Smapi.API.Services
{
    public interface IYtDlpVideoDownloader
    {
        Task<string> DownloadAsync(string videoUrl, string outputDirectory, CancellationToken cancellationToken);
    }

    public class YtDlpVideoDownloader : IYtDlpVideoDownloader
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<YtDlpVideoDownloader> _logger;
        private const int MaxDownloadAttempts = 3;
        private static readonly string[] TikTokImpersonateTargets =
        {
            "chrome-133:macos-15",
            "chrome-131:android-14",
            "chrome-136:macos-15"
        };

        public YtDlpVideoDownloader(
            IConfiguration configuration,
            HttpClient httpClient,
            ILogger<YtDlpVideoDownloader> logger)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> DownloadAsync(string videoUrl, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);

            var executablePath = _configuration["YtDlp:ExecutablePath"];
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                executablePath = "yt-dlp";
            }

            var outputTemplate = Path.Combine(outputDirectory, "reel.%(ext)s");
            Exception? lastError = null;

            for (var attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
            {
                try
                {
                    var impersonateTarget = IsTikTokUrl(videoUrl)
                        ? TikTokImpersonateTargets[(attempt - 1) % TikTokImpersonateTargets.Length]
                        : null;

                    return await RunYtDlpAsync(
                        executablePath,
                        videoUrl,
                        outputTemplate,
                        outputDirectory,
                        impersonateTarget,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < MaxDownloadAttempts)
                {
                    lastError = ex;
                    if (IsTikTokUrl(videoUrl) && IsTikTokWebpageChallengeFailure(ex))
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (IsTikTokUrl(videoUrl) && TryExtractTikTokVideoId(videoUrl, out var videoId))
            {
                try
                {
                    _logger.LogWarning(
                        lastError,
                        "yt-dlp failed for TikTok video {VideoId}. Trying TikTok's official player API.",
                        videoId);

                    return await DownloadTikTokFromPlayerApiAsync(
                        videoId,
                        outputDirectory,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception fallbackError)
                {
                    throw new InvalidOperationException(
                        $"{lastError?.Message ?? "yt-dlp failed without returning an error."} " +
                        $"TikTok player fallback also failed: {fallbackError.Message}",
                        fallbackError);
                }
            }

            throw lastError ?? new InvalidOperationException("yt-dlp failed without returning an error.");
        }

        private async Task<string> DownloadTikTokFromPlayerApiAsync(
            string videoId,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            var playerUrl = new Uri($"https://www.tiktok.com/player/v1/{videoId}");
            var apiUrl = new Uri(
                $"https://www.tiktok.com/player/api/v1/items?item_ids={videoId}&language=en&aid=1459&data_source=web_core");

            using var apiRequest = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            AddTikTokPlayerHeaders(apiRequest, playerUrl);

            using var apiResponse = await _httpClient.SendAsync(
                apiRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            apiResponse.EnsureSuccessStatusCode();

            await using var jsonStream = await apiResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);
            var candidates = ExtractTikTokVideoCandidates(document.RootElement);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("TikTok's player API returned no downloadable MP4 URL.");
            }

            var outputPath = Path.Combine(outputDirectory, "reel.mp4");
            Exception? lastDownloadError = null;

            foreach (var candidate in candidates)
            {
                try
                {
                    using var videoRequest = new HttpRequestMessage(HttpMethod.Get, candidate.Url);
                    AddTikTokPlayerHeaders(videoRequest, playerUrl);
                    using var videoResponse = await _httpClient.SendAsync(
                        videoRequest,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    videoResponse.EnsureSuccessStatusCode();

                    await using var input = await videoResponse.Content.ReadAsStreamAsync(cancellationToken);
                    await using var output = new FileStream(
                        outputPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        useAsync: true);
                    await input.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);

                    if (output.Length == 0)
                    {
                        throw new InvalidOperationException("TikTok returned an empty video file.");
                    }

                    return outputPath;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastDownloadError = ex;
                    TryDeleteFile(outputPath);
                }
            }

            throw new InvalidOperationException(
                "TikTok's player API returned video URLs, but none could be downloaded.",
                lastDownloadError);
        }

        private static List<TikTokVideoCandidate> ExtractTikTokVideoCandidates(JsonElement root)
        {
            if (!root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() == 0
                || !items[0].TryGetProperty("video_info", out var videoInfo))
            {
                return [];
            }

            var candidates = new List<TikTokVideoCandidate>();
            if (videoInfo.TryGetProperty("profiles", out var profiles)
                && profiles.ValueKind == JsonValueKind.Array)
            {
                foreach (var profile in profiles.EnumerateArray())
                {
                    var codec = profile.TryGetProperty("codec_type", out var codecElement)
                        ? codecElement.GetString()
                        : null;
                    var bitrate = profile.TryGetProperty("bitrate", out var bitrateElement)
                        && bitrateElement.TryGetInt64(out var parsedBitrate)
                            ? parsedBitrate
                            : 0;

                    if (profile.TryGetProperty("play_addr", out var playAddress)
                        && playAddress.TryGetProperty("url_list", out var urls))
                    {
                        AddTikTokUrls(candidates, urls, codec, bitrate);
                    }
                }
            }

            if (videoInfo.TryGetProperty("url_list", out var defaultUrls))
            {
                var bitrate = videoInfo.TryGetProperty("meta", out var meta)
                    && meta.TryGetProperty("bitrate", out var bitrateElement)
                    && bitrateElement.TryGetInt64(out var parsedBitrate)
                        ? parsedBitrate
                        : 0;
                AddTikTokUrls(candidates, defaultUrls, "h264", bitrate);
            }

            return candidates
                .Where(candidate => Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps)
                .OrderByDescending(candidate => candidate.Codec.Equals("h264", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(candidate => candidate.Bitrate)
                .DistinctBy(candidate => candidate.Url)
                .Take(12)
                .ToList();
        }

        private static void AddTikTokUrls(
            ICollection<TikTokVideoCandidate> candidates,
            JsonElement urls,
            string? codec,
            long bitrate)
        {
            if (urls.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var urlElement in urls.EnumerateArray())
            {
                var url = urlElement.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    candidates.Add(new TikTokVideoCandidate(url, codec ?? string.Empty, bitrate));
                }
            }
        }

        private static void AddTikTokPlayerHeaders(HttpRequestMessage request, Uri playerUrl)
        {
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
            request.Headers.Referrer = playerUrl;
            request.Headers.TryAddWithoutValidation("agw-js-conv", "str");
            request.Headers.Accept.ParseAdd("*/*");
        }

        private static bool TryExtractTikTokVideoId(string videoUrl, out string videoId)
        {
            videoId = string.Empty;
            if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < segments.Length - 1; index++)
            {
                if (!segments[index].Equals("video", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = segments[index + 1];
                if (candidate.Length >= 10 && candidate.All(char.IsDigit))
                {
                    videoId = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsTikTokWebpageChallengeFailure(Exception exception)
        {
            return exception.Message.Contains(
                "Unexpected response from webpage request",
                StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> RunYtDlpAsync(
            string executablePath,
            string videoUrl,
            string outputTemplate,
            string outputDirectory,
            string? impersonateTarget,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best");
            startInfo.ArgumentList.Add("--merge-output-format");
            startInfo.ArgumentList.Add("mp4");
            startInfo.ArgumentList.Add("--force-ipv4");
            startInfo.ArgumentList.Add("--no-update");
            startInfo.ArgumentList.Add("--no-playlist");
            if (!string.IsNullOrWhiteSpace(impersonateTarget))
            {
                startInfo.ArgumentList.Add("--impersonate");
                startInfo.ArgumentList.Add(impersonateTarget);
            }
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputTemplate);
            startInfo.ArgumentList.Add(videoUrl);

            using var process = new Process { StartInfo = startInfo };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stdout.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stderr.AppendLine(args.Data);
                }
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("yt-dlp could not be started. Install yt-dlp or configure YtDlp:ExecutablePath.", ex);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                throw;
            }

            if (process.ExitCode != 0)
            {
                var details = stderr.Length > 0 ? stderr.ToString() : stdout.ToString();
                throw new InvalidOperationException(BuildFailureMessage(process.ExitCode, details));
            }

            var downloadedFile = Directory
                .EnumerateFiles(outputDirectory)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (downloadedFile is null)
            {
                throw new InvalidOperationException("yt-dlp finished but no video file was created.");
            }

            return downloadedFile;
        }

        private static string BuildFailureMessage(int exitCode, string details)
        {
            var trimmedDetails = TrimForLog(details);
            if (trimmedDetails.Contains("Failed to resolve", StringComparison.OrdinalIgnoreCase)
                || trimmedDetails.Contains("getaddrinfo failed", StringComparison.OrdinalIgnoreCase))
            {
                return $"yt-dlp could not resolve the video source DNS after retries. Check internet/DNS/VPN on the backend machine, then retry. Raw error: {trimmedDetails}";
            }

            return $"yt-dlp failed with exit code {exitCode}: {trimmedDetails}";
        }

        private static bool IsTikTokUrl(string videoUrl)
        {
            if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("tiktokv.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".tiktokv.com", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may already have exited while cancellation was being handled.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A later attempt will overwrite the same temporary file.
            }
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }

        private sealed record TikTokVideoCandidate(string Url, string Codec, long Bitrate);
    }
}
