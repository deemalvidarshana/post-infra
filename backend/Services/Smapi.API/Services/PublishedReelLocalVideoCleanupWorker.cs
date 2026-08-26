using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public class PublishedReelLocalVideoCleanupWorker : BackgroundService
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan PublishedVideoRetention = TimeSpan.FromMinutes(5);
        private const int BatchSize = 500;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PublishedReelLocalVideoCleanupWorker> _logger;

        public PublishedReelLocalVideoCleanupWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<PublishedReelLocalVideoCleanupWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupExpiredPublishedVideosAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected published Reel local video cleanup error.");
                }

                try
                {
                    await Task.Delay(CleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task CleanupExpiredPublishedVideosAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<ILocalVideoStorageService>();
            var now = DateTime.UtcNow;
            var completedBefore = now.Subtract(PublishedVideoRetention);

            var jobs = await dbContext.FacebookReelUploadJobs
                .Where(job => job.Status == FacebookReelUploadJobStatus.Published
                    && job.RetainUntil != null
                    && job.CompletedAt != null
                    && job.CompletedAt <= completedBefore
                    && job.S3Key != null
                    && job.S3Key != "")
                .OrderBy(job => job.CompletedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (jobs.Count == 0)
            {
                return;
            }

            foreach (var jobGroup in jobs
                .Where(job => !string.IsNullOrWhiteSpace(job.S3Key))
                .GroupBy(job => job.S3Key!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var storageKey = jobGroup.Key;
                if (await IsStorageKeyStillNeededAsync(dbContext, storageKey, completedBefore, cancellationToken))
                {
                    continue;
                }

                if (!DeleteLocalVideo(storage, storageKey))
                {
                    continue;
                }

                MarkPublishedJobsAsCleaned(jobGroup, now);
                await MarkSourcePostsAsLocalFilePurgedAsync(dbContext, storageKey, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static async Task<bool> IsStorageKeyStillNeededAsync(
            SmapiDbContext dbContext,
            string storageKey,
            DateTime completedBefore,
            CancellationToken cancellationToken)
        {
            return await dbContext.FacebookReelUploadJobs
                .AsNoTracking()
                .AnyAsync(job => job.S3Key == storageKey
                    && (
                        (job.Status != FacebookReelUploadJobStatus.Published
                            && job.Status != FacebookReelUploadJobStatus.Failed)
                        || (job.Status == FacebookReelUploadJobStatus.Published
                            && (!job.CompletedAt.HasValue || job.CompletedAt > completedBefore))),
                    cancellationToken);
        }

        private bool DeleteLocalVideo(ILocalVideoStorageService storage, string storageKey)
        {
            try
            {
                var localPath = storage.GetAbsolutePath(storageKey);
                if (!File.Exists(localPath))
                {
                    _logger.LogInformation(
                        "Published Reel local video already missing for storage key {StorageKey} at {LocalPath}.",
                        storageKey,
                        localPath);
                    return true;
                }

                File.Delete(localPath);
                _logger.LogInformation(
                    "Deleted published Reel local video after {RetentionMinutes} minutes: {LocalPath}.",
                    PublishedVideoRetention.TotalMinutes,
                    localPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete published Reel local video for storage key {StorageKey}.", storageKey);
                return false;
            }
        }

        private static void MarkPublishedJobsAsCleaned(IEnumerable<FacebookReelUploadJob> jobs, DateTime cleanedAt)
        {
            foreach (var job in jobs)
            {
                job.RetainUntil = null;
                job.UpdatedAt = cleanedAt;
            }
        }

        private static async Task MarkSourcePostsAsLocalFilePurgedAsync(
            SmapiDbContext dbContext,
            string storageKey,
            CancellationToken cancellationToken)
        {
            var posts = await dbContext.FacebookPostUrls
                .Where(post => post.S3Key == storageKey)
                .ToListAsync(cancellationToken);

            foreach (var post in posts)
            {
                post.S3UploadStatus = FacebookPostS3UploadStatus.NotUploaded;
                post.S3Bucket = null;
                post.S3Region = null;
                post.S3Key = null;
                post.S3UploadedAt = null;
                post.S3UploadError = "Local video file was deleted 5 minutes after Facebook publishing to save server storage.";
            }
        }
    }
}
