using System.Diagnostics;
using System.Globalization;

namespace Smapi.API.Services
{
    public record ExtractedVideoFrame(string Path, string MimeType);

    public interface IVideoFrameExtractor
    {
        Task<IReadOnlyList<ExtractedVideoFrame>> ExtractRandomFramesAsync(
            string videoPath,
            int count,
            string outputDirectory,
            CancellationToken cancellationToken);
    }

    public class FfmpegVideoFrameExtractor : IVideoFrameExtractor
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FfmpegVideoFrameExtractor> _logger;

        public FfmpegVideoFrameExtractor(
            IConfiguration configuration,
            ILogger<FfmpegVideoFrameExtractor> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ExtractedVideoFrame>> ExtractRandomFramesAsync(
            string videoPath,
            int count,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            if (count <= 0 || string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                return Array.Empty<ExtractedVideoFrame>();
            }

            Directory.CreateDirectory(outputDirectory);

            var ffmpegPath = FirstNonEmpty(
                _configuration["FFmpeg:ExecutablePath"],
                _configuration["Ffmpeg:ExecutablePath"],
                "ffmpeg")!;
            var duration = await TryGetDurationAsync(videoPath, cancellationToken);
            var offsets = BuildRandomOffsets(duration, count);
            var frames = new List<ExtractedVideoFrame>();

            for (var index = 0; index < offsets.Count; index++)
            {
                var outputPath = Path.Combine(outputDirectory, $"frame-{index + 1}.jpg");
                var success = await TryExtractFrameAsync(
                    ffmpegPath,
                    videoPath,
                    outputPath,
                    offsets[index],
                    cancellationToken);

                if (success && File.Exists(outputPath))
                {
                    frames.Add(new ExtractedVideoFrame(outputPath, "image/jpeg"));
                }
            }

            return frames;
        }

        private async Task<TimeSpan?> TryGetDurationAsync(string videoPath, CancellationToken cancellationToken)
        {
            var ffprobePath = FirstNonEmpty(
                _configuration["FFmpeg:ProbeExecutablePath"],
                _configuration["Ffmpeg:ProbeExecutablePath"],
                "ffprobe")!;

            try
            {
                var result = await RunProcessAsync(
                    ffprobePath,
                    new[]
                    {
                        "-v", "error",
                        "-show_entries", "format=duration",
                        "-of", "default=noprint_wrappers=1:nokey=1",
                        videoPath
                    },
                    cancellationToken);

                if (result.ExitCode == 0
                    && double.TryParse(result.Stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    && seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                _logger.LogWarning("ffprobe could not read duration for {VideoPath}: {Error}", videoPath, TrimForLog(result.Stderr));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ffprobe failed for {VideoPath}. Falling back to early frame offsets.", videoPath);
            }

            return null;
        }

        private async Task<bool> TryExtractFrameAsync(
            string ffmpegPath,
            string videoPath,
            string outputPath,
            TimeSpan offset,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await RunProcessAsync(
                    ffmpegPath,
                    new[]
                    {
                        "-hide_banner",
                        "-loglevel", "error",
                        "-y",
                        "-ss", offset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        "-i", videoPath,
                        "-frames:v", "1",
                        "-q:v", "3",
                        outputPath
                    },
                    cancellationToken);

                if (result.ExitCode == 0)
                {
                    return true;
                }

                _logger.LogWarning("ffmpeg frame extraction failed for {VideoPath}: {Error}", videoPath, TrimForLog(result.Stderr));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ffmpeg frame extraction failed for {VideoPath}.", videoPath);
            }

            return false;
        }

        private static List<TimeSpan> BuildRandomOffsets(TimeSpan? duration, int count)
        {
            if (duration is null || duration.Value.TotalSeconds <= 1)
            {
                return Enumerable.Range(0, count)
                    .Select(index => TimeSpan.FromSeconds(1 + (index * 2)))
                    .ToList();
            }

            var totalSeconds = duration.Value.TotalSeconds;
            var minSeconds = totalSeconds > 6 ? 1d : 0.1d;
            var maxSeconds = Math.Max(minSeconds, totalSeconds - 0.5d);
            var offsets = new List<TimeSpan>();

            for (var index = 0; index < count; index++)
            {
                var second = minSeconds + (Random.Shared.NextDouble() * (maxSeconds - minSeconds));
                offsets.Add(TimeSpan.FromSeconds(second));
            }

            return offsets.OrderBy(offset => offset).ToList();
        }

        private static async Task<ProcessResult> RunProcessAsync(
            string fileName,
            IEnumerable<string> arguments,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                throw;
            }

            return new ProcessResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
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
                // The process may already have exited during cancellation.
            }
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 500 ? value : value[..500];
        }

        private record ProcessResult(int ExitCode, string Stdout, string Stderr);
    }
}
