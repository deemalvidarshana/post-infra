using System.Diagnostics;
using System.Text;

namespace Smapi.API.Services
{
    public interface IYtDlpVideoDownloader
    {
        Task<string> DownloadAsync(string videoUrl, string outputDirectory, CancellationToken cancellationToken);
    }

    public class YtDlpVideoDownloader : IYtDlpVideoDownloader
    {
        private readonly IConfiguration _configuration;
        private const int MaxDownloadAttempts = 3;
        private static readonly string[] TikTokImpersonateTargets =
        {
            "chrome-133:macos-15",
            "chrome-131:android-14",
            "chrome-136:macos-15"
        };

        public YtDlpVideoDownloader(IConfiguration configuration)
        {
            _configuration = configuration;
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
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("yt-dlp failed without returning an error.");
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

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }
    }
}
