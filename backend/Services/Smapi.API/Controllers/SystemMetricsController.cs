using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SystemMetricsController : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<SystemMetricsResponse>> Get(CancellationToken cancellationToken)
        {
            var firstCpuSample = ReadCpuSample();
            await Task.Delay(250, cancellationToken);
            var secondCpuSample = ReadCpuSample();

            var memory = ReadMemory();
            var rootDisk = ReadDisk("/");
            var loadAverage = ReadLoadAverage();
            var network = ReadNetworkTotals();
            var uptime = ReadUptime();

            return Ok(new SystemMetricsResponse
            {
                CapturedAtUtc = DateTime.UtcNow,
                Hostname = Environment.MachineName,
                OperatingSystem = RuntimeInformation.OSDescription.Trim(),
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                ProcessCount = CountProcesses(),
                UptimeSeconds = uptime?.TotalSeconds,
                Cpu = new CpuMetric
                {
                    UsagePercent = CalculateCpuUsage(firstCpuSample, secondCpuSample),
                    Load1 = loadAverage.Load1,
                    Load5 = loadAverage.Load5,
                    Load15 = loadAverage.Load15
                },
                Memory = memory,
                Disk = rootDisk,
                Network = network
            });
        }

        private static CpuSample? ReadCpuSample()
        {
            try
            {
                var line = System.IO.File.ReadLines("/proc/stat").FirstOrDefault(item => item.StartsWith("cpu ", StringComparison.Ordinal));
                if (string.IsNullOrWhiteSpace(line))
                {
                    return null;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                    .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0)
                    .ToArray();

                if (parts.Length < 5)
                {
                    return null;
                }

                var idle = parts[3] + parts[4];
                var total = parts.Sum();
                return new CpuSample(total, idle);
            }
            catch
            {
                return null;
            }
        }

        private static double? CalculateCpuUsage(CpuSample? first, CpuSample? second)
        {
            if (first is null || second is null)
            {
                return null;
            }

            var totalDelta = second.Value.Total - first.Value.Total;
            var idleDelta = second.Value.Idle - first.Value.Idle;
            if (totalDelta <= 0)
            {
                return null;
            }

            var usage = (1d - (double)idleDelta / totalDelta) * 100d;
            return Math.Clamp(Math.Round(usage, 1), 0, 100);
        }

        private static MemoryMetric ReadMemory()
        {
            var values = ReadProcMemInfo();
            var total = values.GetValueOrDefault("MemTotal") * 1024L;
            var available = values.GetValueOrDefault("MemAvailable") * 1024L;
            var swapTotal = values.GetValueOrDefault("SwapTotal") * 1024L;
            var swapFree = values.GetValueOrDefault("SwapFree") * 1024L;

            var used = Math.Max(0, total - available);
            var swapUsed = Math.Max(0, swapTotal - swapFree);

            return new MemoryMetric
            {
                TotalBytes = total,
                UsedBytes = used,
                AvailableBytes = available,
                UsagePercent = CalculatePercent(used, total),
                SwapTotalBytes = swapTotal,
                SwapUsedBytes = swapUsed,
                SwapFreeBytes = swapFree,
                SwapUsagePercent = CalculatePercent(swapUsed, swapTotal)
            };
        }

        private static Dictionary<string, long> ReadProcMemInfo()
        {
            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var line in System.IO.File.ReadLines("/proc/meminfo"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    var numberText = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (long.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        values[parts[0]] = value;
                    }
                }
            }
            catch
            {
                // Leave the dictionary empty when /proc is unavailable.
            }

            return values;
        }

        private static DiskMetric ReadDisk(string path)
        {
            try
            {
                var disk = new DriveInfo(path);
                var total = disk.TotalSize;
                var free = disk.AvailableFreeSpace;
                var used = Math.Max(0, total - free);

                return new DiskMetric
                {
                    Path = path,
                    FileSystem = disk.DriveFormat,
                    TotalBytes = total,
                    UsedBytes = used,
                    FreeBytes = free,
                    UsagePercent = CalculatePercent(used, total)
                };
            }
            catch
            {
                return new DiskMetric { Path = path };
            }
        }

        private static LoadAverageMetric ReadLoadAverage()
        {
            try
            {
                var parts = System.IO.File.ReadAllText("/proc/loadavg")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                return new LoadAverageMetric
                {
                    Load1 = ParseDouble(parts.ElementAtOrDefault(0)),
                    Load5 = ParseDouble(parts.ElementAtOrDefault(1)),
                    Load15 = ParseDouble(parts.ElementAtOrDefault(2))
                };
            }
            catch
            {
                return new LoadAverageMetric();
            }
        }

        private static NetworkMetric ReadNetworkTotals()
        {
            long received = 0;
            long transmitted = 0;

            try
            {
                foreach (var line in System.IO.File.ReadLines("/proc/net/dev").Skip(2))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length != 2 || parts[0].Trim().Equals("lo", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var values = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length < 16)
                    {
                        continue;
                    }

                    received += ParseLong(values[0]);
                    transmitted += ParseLong(values[8]);
                }
            }
            catch
            {
                // Keep zeros when network counters are unavailable.
            }

            return new NetworkMetric
            {
                ReceivedBytes = received,
                TransmittedBytes = transmitted
            };
        }

        private static TimeSpan? ReadUptime()
        {
            try
            {
                var firstValue = System.IO.File.ReadAllText("/proc/uptime")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

                return double.TryParse(firstValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static int CountProcesses()
        {
            try
            {
                return Directory.EnumerateDirectories("/proc")
                    .Count(path => int.TryParse(Path.GetFileName(path), out _));
            }
            catch
            {
                return Process.GetProcesses().Length;
            }
        }

        private static double CalculatePercent(long used, long total)
        {
            if (total <= 0)
            {
                return 0;
            }

            return Math.Clamp(Math.Round((double)used / total * 100d, 1), 0, 100);
        }

        private static double? ParseDouble(string? value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? Math.Round(result, 2)
                : null;
        }

        private static long ParseLong(string? value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private readonly record struct CpuSample(long Total, long Idle);
    }

    public class SystemMetricsResponse
    {
        public DateTime CapturedAtUtc { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }
        public int ProcessCount { get; set; }
        public double? UptimeSeconds { get; set; }
        public CpuMetric Cpu { get; set; } = new();
        public MemoryMetric Memory { get; set; } = new();
        public DiskMetric Disk { get; set; } = new();
        public NetworkMetric Network { get; set; } = new();
    }

    public class CpuMetric
    {
        public double? UsagePercent { get; set; }
        public double? Load1 { get; set; }
        public double? Load5 { get; set; }
        public double? Load15 { get; set; }
    }

    public class MemoryMetric
    {
        public long TotalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public double UsagePercent { get; set; }
        public long SwapTotalBytes { get; set; }
        public long SwapUsedBytes { get; set; }
        public long SwapFreeBytes { get; set; }
        public double SwapUsagePercent { get; set; }
    }

    public class DiskMetric
    {
        public string Path { get; set; } = "/";
        public string? FileSystem { get; set; }
        public long TotalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long FreeBytes { get; set; }
        public double UsagePercent { get; set; }
    }

    public class LoadAverageMetric
    {
        public double? Load1 { get; set; }
        public double? Load5 { get; set; }
        public double? Load15 { get; set; }
    }

    public class NetworkMetric
    {
        public long ReceivedBytes { get; set; }
        public long TransmittedBytes { get; set; }
    }
}
