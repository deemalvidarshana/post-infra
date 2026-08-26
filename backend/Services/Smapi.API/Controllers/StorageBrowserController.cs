using Smapi.API.Data;
using Smapi.API.Models;
using Smapi.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StorageBrowserController : ControllerBase
    {
        private readonly SmapiDbContext _context;
        private readonly ILocalVideoStorageService _storage;
        private readonly ILogger<StorageBrowserController> _logger;

        public StorageBrowserController(
            SmapiDbContext context,
            ILocalVideoStorageService storage,
            ILogger<StorageBrowserController> logger)
        {
            _context = context;
            _storage = storage;
            _logger = logger;
        }

        [HttpGet]
        public ActionResult<StorageBrowserResponse> Browse([FromQuery] string? path)
        {
            var resolved = ResolvePath(path);
            if (!Directory.Exists(resolved.AbsolutePath))
            {
                return NotFound(new { success = false, message = "Storage folder was not found." });
            }

            var directory = new DirectoryInfo(resolved.AbsolutePath);
            var entries = new List<StorageBrowserEntryResponse>();

            foreach (var childDirectory in SafeEnumerateDirectories(directory))
            {
                entries.Add(new StorageBrowserEntryResponse
                {
                    Name = childDirectory.Name,
                    RelativePath = CombineRelativePath(resolved.RelativePath, childDirectory.Name),
                    Kind = StorageBrowserEntryKind.Folder,
                    SizeBytes = GetDirectorySize(childDirectory),
                    ModifiedAtUtc = childDirectory.LastWriteTimeUtc,
                    ChildCount = SafeEnumerateFileSystemEntries(childDirectory).Count()
                });
            }

            foreach (var childFile in SafeEnumerateFiles(directory))
            {
                entries.Add(new StorageBrowserEntryResponse
                {
                    Name = childFile.Name,
                    RelativePath = CombineRelativePath(resolved.RelativePath, childFile.Name),
                    Kind = StorageBrowserEntryKind.File,
                    SizeBytes = childFile.Length,
                    ModifiedAtUtc = childFile.LastWriteTimeUtc,
                    ChildCount = null
                });
            }

            entries = entries
                .OrderByDescending(entry => entry.Kind == StorageBrowserEntryKind.Folder)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(new StorageBrowserResponse
            {
                RootName = new DirectoryInfo(_storage.RootDirectory).Name,
                CurrentPath = resolved.RelativePath,
                ParentPath = GetParentPath(resolved.RelativePath),
                SizeBytes = GetDirectorySize(directory),
                Entries = entries
            });
        }

        [HttpDelete("entry")]
        public async Task<IActionResult> DeleteEntry([FromQuery] string? path, CancellationToken cancellationToken)
        {
            var resolved = ResolvePath(path);
            if (string.IsNullOrWhiteSpace(resolved.RelativePath))
            {
                return BadRequest(new { success = false, message = "The storage root folder cannot be deleted from the UI." });
            }

            var deletedKeys = new List<string>();
            var deletedKind = "file";

            if (System.IO.File.Exists(resolved.AbsolutePath))
            {
                deletedKeys.Add(resolved.RelativePath);
                System.IO.File.Delete(resolved.AbsolutePath);
            }
            else if (Directory.Exists(resolved.AbsolutePath))
            {
                deletedKind = "folder";
                deletedKeys.AddRange(Directory
                    .EnumerateFiles(resolved.AbsolutePath, "*", SearchOption.AllDirectories)
                    .Select(file => ToStorageKey(file)));

                Directory.Delete(resolved.AbsolutePath, recursive: true);
            }
            else
            {
                return NotFound(new { success = false, message = "Storage item was not found. It may already have been deleted." });
            }

            var affectedPostCount = await MarkSourcePostsAsMissingAsync(deletedKeys, cancellationToken);
            var affectedJobCount = await MarkUploadJobsAsMissingAsync(deletedKeys, cancellationToken);

            return Ok(new
            {
                success = true,
                message = $"Deleted {deletedKind} '{resolved.RelativePath}' from local video storage.",
                deletedKeyCount = deletedKeys.Count,
                affectedPostCount,
                affectedJobCount
            });
        }

        private async Task<int> MarkSourcePostsAsMissingAsync(
            IReadOnlyCollection<string> storageKeys,
            CancellationToken cancellationToken)
        {
            if (storageKeys.Count == 0)
            {
                return 0;
            }

            var posts = await _context.FacebookPostUrls
                .Where(post => post.S3Key != null && storageKeys.Contains(post.S3Key))
                .ToListAsync(cancellationToken);

            foreach (var post in posts)
            {
                post.S3UploadStatus = FacebookPostS3UploadStatus.NotUploaded;
                post.S3Bucket = null;
                post.S3Region = null;
                post.S3Key = null;
                post.S3UploadedAt = null;
                post.S3UploadError = "Local video file was manually deleted from Storage Manager.";
            }

            await _context.SaveChangesAsync(cancellationToken);
            return posts.Count;
        }

        private async Task<int> MarkUploadJobsAsMissingAsync(
            IReadOnlyCollection<string> storageKeys,
            CancellationToken cancellationToken)
        {
            if (storageKeys.Count == 0)
            {
                return 0;
            }

            var jobs = await _context.FacebookReelUploadJobs
                .Where(job => job.S3Key != null && storageKeys.Contains(job.S3Key))
                .ToListAsync(cancellationToken);

            foreach (var job in jobs)
            {
                job.UpdatedAt = DateTime.UtcNow;

                if (job.Status == FacebookReelUploadJobStatus.Published)
                {
                    job.RetainUntil = null;
                    continue;
                }

                job.S3Bucket = null;
                job.S3Region = null;
                job.S3EndpointUrl = null;
                job.S3Key = null;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return jobs.Count;
        }

        private ResolvedStoragePath ResolvePath(string? relativePath)
        {
            relativePath = NormalizeRelativePath(relativePath);
            var absolutePath = string.IsNullOrWhiteSpace(relativePath)
                ? Path.GetFullPath(_storage.RootDirectory)
                : Path.GetFullPath(Path.Combine(_storage.RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            var root = Path.GetFullPath(_storage.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootWithSeparator = root + Path.DirectorySeparatorChar;

            if (!absolutePath.Equals(root, StringComparison.OrdinalIgnoreCase)
                && !absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Storage path points outside the configured video folder.");
            }

            return new ResolvedStoragePath(relativePath, absolutePath);
        }

        private string ToStorageKey(string absolutePath)
        {
            var root = Path.GetFullPath(_storage.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(absolutePath);

            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Storage path points outside the configured video folder.");
            }

            return fullPath[root.Length..].Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string NormalizeRelativePath(string? relativePath)
        {
            relativePath = (relativePath ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Any(segment => segment is "." or ".."))
            {
                throw new InvalidOperationException("A valid storage path is required.");
            }

            return string.Join('/', segments);
        }

        private static string CombineRelativePath(string currentPath, string childName)
        {
            return string.IsNullOrWhiteSpace(currentPath) ? childName : $"{currentPath}/{childName}";
        }

        private static string? GetParentPath(string currentPath)
        {
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return null;
            }

            var slashIndex = currentPath.LastIndexOf('/');
            return slashIndex < 0 ? string.Empty : currentPath[..slashIndex];
        }

        private long GetDirectorySize(DirectoryInfo directory)
        {
            try
            {
                return directory
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(file => !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    .Sum(file => SafeGetFileLength(file));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not calculate directory size for {Directory}.", directory.FullName);
                return 0;
            }
        }

        private static long SafeGetFileLength(FileInfo file)
        {
            try
            {
                return file.Length;
            }
            catch
            {
                return 0;
            }
        }

        private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory)
        {
            try
            {
                return directory.EnumerateDirectories().Where(item => !item.Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList();
            }
            catch
            {
                return Enumerable.Empty<DirectoryInfo>();
            }
        }

        private static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo directory)
        {
            try
            {
                return directory.EnumerateFiles().Where(item => !item.Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList();
            }
            catch
            {
                return Enumerable.Empty<FileInfo>();
            }
        }

        private static IEnumerable<FileSystemInfo> SafeEnumerateFileSystemEntries(DirectoryInfo directory)
        {
            try
            {
                return directory.EnumerateFileSystemInfos().Where(item => !item.Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList();
            }
            catch
            {
                return Enumerable.Empty<FileSystemInfo>();
            }
        }

        private record ResolvedStoragePath(string RelativePath, string AbsolutePath);
    }

    public static class StorageBrowserEntryKind
    {
        public const string Folder = "folder";
        public const string File = "file";
    }

    public class StorageBrowserResponse
    {
        public string RootName { get; set; } = "downloads";

        public string CurrentPath { get; set; } = string.Empty;

        public string? ParentPath { get; set; }

        public long SizeBytes { get; set; }

        public List<StorageBrowserEntryResponse> Entries { get; set; } = new();
    }

    public class StorageBrowserEntryResponse
    {
        public string Name { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public string Kind { get; set; } = StorageBrowserEntryKind.File;

        public long SizeBytes { get; set; }

        public DateTime ModifiedAtUtc { get; set; }

        public int? ChildCount { get; set; }
    }
}
