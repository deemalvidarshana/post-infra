using System.Globalization;

namespace Smapi.API.Services
{
    public record LocalVideoStorageResult(string Key, string AbsolutePath, string? PublicUrl);

    public interface ILocalVideoStorageService
    {
        string RootDirectory { get; }

        string? PublicBaseUrl { get; }

        string BuildStorageKey(string pageName, string pageId, string category, int itemId);

        Task<LocalVideoStorageResult> StoreAsync(
            string localFilePath,
            string storageKey,
            CancellationToken cancellationToken);

        Task<string> CreateReadUrlAsync(string storageKey, CancellationToken cancellationToken);

        string GetAbsolutePath(string storageKey);
    }

    public class LocalVideoStorageService : ILocalVideoStorageService
    {
        private readonly string _readRoutePrefix;

        public LocalVideoStorageService(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var configuredRoot = configuration["LocalStorage:RootPath"];
            if (string.IsNullOrWhiteSpace(configuredRoot))
            {
                configuredRoot = Path.Combine("..", "..", "..", "downloads");
            }

            RootDirectory = Path.GetFullPath(
                Path.IsPathRooted(configuredRoot)
                    ? configuredRoot
                    : Path.Combine(environment.ContentRootPath, configuredRoot));

            PublicBaseUrl = string.IsNullOrWhiteSpace(configuration["LocalStorage:PublicBaseUrl"])
                ? null
                : configuration["LocalStorage:PublicBaseUrl"]!.Trim().TrimEnd('/');

            _readRoutePrefix = string.IsNullOrWhiteSpace(configuration["LocalStorage:ReadRoutePrefix"])
                ? "/api/smapi/FacebookS3Uploads/local"
                : configuration["LocalStorage:ReadRoutePrefix"]!.Trim().TrimEnd('/');
        }

        public string RootDirectory { get; }

        public string? PublicBaseUrl { get; }

        public string BuildStorageKey(string pageName, string pageId, string category, int itemId)
        {
            var pageSegment = ToSafePathSegment(
                string.Join(
                    "-",
                    new[] { FirstNonEmpty(pageName, "facebook-page"), pageId }
                        .Where(value => !string.IsNullOrWhiteSpace(value))));
            var categorySegment = ToSafePathSegment(category);
            var dateSegment = DateTime.UtcNow.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

            return $"{pageSegment}/{categorySegment}/{dateSegment}/{itemId}/video.mp4";
        }

        public async Task<LocalVideoStorageResult> StoreAsync(
            string localFilePath,
            string storageKey,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
            {
                throw new FileNotFoundException("Downloaded video file was not found.", localFilePath);
            }

            var key = NormalizeStorageKey(storageKey);
            var destinationPath = GetAbsolutePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            await using (var source = File.OpenRead(localFilePath))
            await using (var destination = File.Create(destinationPath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            return new LocalVideoStorageResult(key, destinationPath, BuildPublicUrl(key));
        }

        public Task<string> CreateReadUrlAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = NormalizeStorageKey(storageKey);
            return Task.FromResult(BuildLocalReadUrl(key));
        }

        public string GetAbsolutePath(string storageKey)
        {
            var key = NormalizeStorageKey(storageKey);
            var relativePath = key.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
            var rootWithSeparator = RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Local storage key points outside the configured download folder.");
            }

            return fullPath;
        }

        private string? BuildPublicUrl(string storageKey)
        {
            if (string.IsNullOrWhiteSpace(PublicBaseUrl))
            {
                return null;
            }

            return $"{PublicBaseUrl}{BuildLocalReadUrl(storageKey, useFrontendProxyPrefix: false)}";
        }

        private string BuildLocalReadUrl(string storageKey, bool useFrontendProxyPrefix = true)
        {
            var routePrefix = useFrontendProxyPrefix
                ? _readRoutePrefix
                : "/api/FacebookS3Uploads/local";

            return $"{routePrefix}/{EscapeStorageKey(storageKey)}";
        }

        private static string NormalizeStorageKey(string storageKey)
        {
            storageKey = storageKey.Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(storageKey)
                || storageKey.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                throw new InvalidOperationException("A valid local storage key is required.");
            }

            return storageKey;
        }

        private static string EscapeStorageKey(string storageKey)
        {
            return string.Join('/', storageKey.Split('/').Select(Uri.EscapeDataString));
        }

        private static string ToSafePathSegment(string value)
        {
            value = value.Trim();
            var characters = value.Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');

            var safeValue = string.Concat(characters).Trim('-');
            while (safeValue.Contains("--", StringComparison.Ordinal))
            {
                safeValue = safeValue.Replace("--", "-", StringComparison.Ordinal);
            }

            return string.IsNullOrWhiteSpace(safeValue) ? "facebook-page" : safeValue;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }
    }
}
