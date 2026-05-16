using Smapi.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public class FacebookPostS3UploadWorker : BackgroundService
    {
        private readonly IFacebookPostS3UploadQueue _queue;
        private readonly IFacebookPostS3DownloadCancellation _downloadCancellation;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FacebookPostS3UploadWorker> _logger;

        public FacebookPostS3UploadWorker(
            IFacebookPostS3UploadQueue queue,
            IFacebookPostS3DownloadCancellation downloadCancellation,
            IServiceScopeFactory scopeFactory,
            ILogger<FacebookPostS3UploadWorker> logger)
        {
            _queue = queue;
            _downloadCancellation = downloadCancellation;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                FacebookPostS3UploadWorkItem workItem;
                try
                {
                    workItem = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await ProcessAsync(workItem, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected local download worker error for Facebook post {PostId}.", workItem.PostId);
                    await MarkFailedAsync(workItem.PostId, ex.Message, CancellationToken.None);
                }
            }
        }

        private async Task ProcessAsync(FacebookPostS3UploadWorkItem workItem, CancellationToken cancellationToken)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "smapi-post-local-downloads", workItem.PostId.ToString());
            using var downloadTokenSource = _downloadCancellation.CreateLinkedTokenSource(workItem.PostId, cancellationToken);
            var downloadToken = downloadTokenSource.Token;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
                var downloader = scope.ServiceProvider.GetRequiredService<IYtDlpVideoDownloader>();
                var storage = scope.ServiceProvider.GetRequiredService<ILocalVideoStorageService>();

                var post = await dbContext.FacebookPostUrls
                    .FirstAsync(item => item.Id == workItem.PostId && item.UserId == workItem.UserId, cancellationToken);

                if (post.S3UploadStatus != FacebookPostS3UploadStatus.Queued)
                {
                    return;
                }

                var page = await dbContext.FacebookPages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item => item.UserId == workItem.UserId && item.PageId == workItem.PageId,
                        cancellationToken);

                post.S3UploadStatus = FacebookPostS3UploadStatus.Downloading;
                post.S3UploadError = null;
                await dbContext.SaveChangesAsync(downloadToken);

                var sourceUrl = FirstNonEmpty(post.VideoUrl, post.PermalinkUrl);
                if (string.IsNullOrWhiteSpace(sourceUrl))
                {
                    throw new InvalidOperationException("This scraped post does not have a video URL or reel URL.");
                }

                var localVideoPath = await downloader.DownloadAsync(sourceUrl, tempDirectory, downloadToken);
                var storageKey = storage.BuildStorageKey(
                    FirstNonEmpty(page?.PageName, post.PageId, workItem.PageId) ?? "facebook-page",
                    workItem.PageId,
                    "scraped-reels",
                    post.Id);

                var storageResult = await storage.StoreAsync(
                    localVideoPath,
                    storageKey,
                    downloadToken);

                post.S3UploadStatus = FacebookPostS3UploadStatus.Downloaded;
                post.S3Bucket = null;
                post.S3Region = null;
                post.S3Key = storageResult.Key;
                post.S3UploadedAt = DateTime.UtcNow;
                post.S3UploadError = null;
                await dbContext.SaveChangesAsync(downloadToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Local download stopped for Facebook post {PostId}.", workItem.PostId);
                await MarkCancelledAsync(workItem.PostId, CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local download failed for Facebook post {PostId}.", workItem.PostId);
                await MarkFailedAsync(workItem.PostId, ex.Message, cancellationToken);
            }
            finally
            {
                _downloadCancellation.Clear(workItem.PostId);
                TryDeleteDirectory(tempDirectory);
            }
        }

        private async Task MarkFailedAsync(int postId, string errorMessage, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
            var post = await dbContext.FacebookPostUrls.FirstOrDefaultAsync(item => item.Id == postId, cancellationToken);

            if (post is null)
            {
                return;
            }

            post.S3UploadStatus = FacebookPostS3UploadStatus.Failed;
            post.S3UploadError = TrimForLog(errorMessage);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkCancelledAsync(int postId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
            var post = await dbContext.FacebookPostUrls.FirstOrDefaultAsync(item => item.Id == postId, cancellationToken);

            if (post is null)
            {
                return;
            }

            post.S3UploadStatus = FacebookPostS3UploadStatus.Cancelled;
            post.S3UploadError = "Download stopped by user.";
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private static void TryDeleteDirectory(string tempDirectory)
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Temporary files are cleaned best-effort.
            }
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }
    }
}
