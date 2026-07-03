using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Smapi.API.Services;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BackupController : ControllerBase
    {
        private const string DbOnlyBackupType = "db-only";
        private const string FullBackupType = "full";
        private const int ProcessTimeoutMinutes = 20;
        private readonly IConfiguration _configuration;
        private readonly ILocalVideoStorageService _storage;
        private readonly ILogger<BackupController> _logger;

        public BackupController(
            IConfiguration configuration,
            ILocalVideoStorageService storage,
            ILogger<BackupController> logger)
        {
            _configuration = configuration;
            _storage = storage;
            _logger = logger;
        }

        [HttpGet("download")]
        public async Task<IActionResult> DownloadBackup(
            [FromQuery] string type = DbOnlyBackupType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                type = NormalizeBackupType(type);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }

            var includeStorage = type == FullBackupType;
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var dbDumpPath = Path.Combine(Path.GetTempPath(), $"smapi-db-{timestamp}-{Guid.NewGuid():N}.dump");
            var fileName = includeStorage
                ? $"sm-automate-full-backup-{timestamp}.zip"
                : $"sm-automate-db-backup-{timestamp}.zip";

            try
            {
                await CreateDatabaseDumpAsync(dbDumpPath, cancellationToken);

                Response.ContentType = "application/zip";
                Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
                Response.Headers.CacheControl = "no-store";

                var storageRoot = _storage.RootDirectory;
                var storageFiles = includeStorage && Directory.Exists(storageRoot)
                    ? Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories).ToList()
                    : new List<string>();

                await using var responseStream = Response.BodyWriter.AsStream();
                using var archive = new ZipArchive(responseStream, ZipArchiveMode.Create, leaveOpen: true);

                AddManifestEntry(archive, type, dbDumpPath, storageFiles);
                archive.CreateEntryFromFile(dbDumpPath, "database/smapi-db.dump", CompressionLevel.Optimal);

                if (includeStorage)
                {
                    AddStorageFiles(archive, storageRoot, storageFiles);
                }

                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate {BackupType} backup.", type);

                if (!Response.HasStarted)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new
                    {
                        success = false,
                        message = "Backup could not be generated. Check server logs for the exact error."
                    });
                }

                throw;
            }
            finally
            {
                TryDeleteFile(dbDumpPath);
            }
        }

        private async Task CreateDatabaseDumpAsync(string dbDumpPath, CancellationToken cancellationToken)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Default database connection string is not configured.");
            }

            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var host = string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host;
            var database = builder.Database;
            var username = builder.Username;

            if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("Database name and username are required for backup.");
            }

            var port = builder.Port > 0 ? builder.Port.ToString() : "5432";
            var startInfo = new ProcessStartInfo
            {
                FileName = "pg_dump",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("--host");
            startInfo.ArgumentList.Add(host);
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(port);
            startInfo.ArgumentList.Add("--username");
            startInfo.ArgumentList.Add(username);
            startInfo.ArgumentList.Add("--dbname");
            startInfo.ArgumentList.Add(database);
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("custom");
            startInfo.ArgumentList.Add("--file");
            startInfo.ArgumentList.Add(dbDumpPath);

            if (!string.IsNullOrWhiteSpace(builder.Password))
            {
                startInfo.Environment["PGPASSWORD"] = builder.Password;
            }

            await RunProcessAsync(startInfo, "pg_dump", cancellationToken);
        }

        private static async Task RunProcessAsync(
            ProcessStartInfo startInfo,
            string operationName,
            CancellationToken cancellationToken)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(ProcessTimeoutMinutes));
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"{operationName} could not be started.");
            }

            var stderrTask = process.StandardError.ReadToEndAsync(linkedCancellation.Token);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCancellation.Token);

            try
            {
                await process.WaitForExitAsync(linkedCancellation.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                TryKillProcess(process);
                throw new TimeoutException($"{operationName} timed out after {ProcessTimeoutMinutes} minutes.");
            }

            var stderr = await stderrTask;
            _ = await stdoutTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{operationName} failed with exit code {process.ExitCode}: {stderr}");
            }
        }

        private void AddManifestEntry(
            ZipArchive archive,
            string backupType,
            string dbDumpPath,
            IReadOnlyCollection<string> storageFiles)
        {
            var manifest = new
            {
                app = "sm-automate",
                backupType,
                createdAtUtc = DateTime.UtcNow,
                database = new
                {
                    format = "postgres-custom",
                    path = "database/smapi-db.dump",
                    bytes = new FileInfo(dbDumpPath).Length
                },
                storage = new
                {
                    included = backupType == FullBackupType,
                    path = backupType == FullBackupType ? "storage/" : null,
                    fileCount = storageFiles.Count,
                    bytes = storageFiles.Sum(path => new FileInfo(path).Length)
                },
                notes = backupType == DbOnlyBackupType
                    ? "Database only backup. Local video files are not included and can be downloaded again from source links when available."
                    : "Full backup with database and local downloaded videos."
            };

            var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var stream = entry.Open();
            JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private static void AddStorageFiles(
            ZipArchive archive,
            string storageRoot,
            IEnumerable<string> storageFiles)
        {
            var normalizedRoot = Path.GetFullPath(storageRoot);

            foreach (var filePath in storageFiles)
            {
                var relativePath = Path.GetRelativePath(normalizedRoot, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');

                if (string.IsNullOrWhiteSpace(relativePath)
                    || relativePath.StartsWith("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(relativePath))
                {
                    continue;
                }

                archive.CreateEntryFromFile(filePath, $"storage/{relativePath}", CompressionLevel.NoCompression);
            }
        }

        private static string NormalizeBackupType(string? type)
        {
            type = string.IsNullOrWhiteSpace(type) ? DbOnlyBackupType : type.Trim().ToLowerInvariant();

            return type switch
            {
                DbOnlyBackupType => DbOnlyBackupType,
                FullBackupType => FullBackupType,
                _ => throw new ArgumentException("Backup type must be db-only or full.", nameof(type))
            };
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
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
                // Best-effort process cleanup.
            }
        }
    }
}
