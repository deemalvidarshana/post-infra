using System.Threading.Channels;

namespace Smapi.API.Services
{
    public static class FacebookPostS3UploadStatus
    {
        public const string NotUploaded = "NotUploaded";
        public const string Queued = "Queued";
        public const string Downloading = "Downloading";
        public const string Downloaded = "Downloaded";
        public const string Uploading = "Uploading";
        public const string Uploaded = "Uploaded";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }

    public record FacebookPostS3UploadWorkItem(int PostId, string UserId, string PageId);

    public interface IFacebookPostS3DownloadCancellation
    {
        CancellationTokenSource CreateLinkedTokenSource(int postId, CancellationToken cancellationToken);

        void Cancel(IEnumerable<int> postIds);

        void Clear(int postId);
    }

    public interface IFacebookPostS3UploadQueue
    {
        ValueTask QueueAsync(FacebookPostS3UploadWorkItem workItem, CancellationToken cancellationToken);

        ValueTask<FacebookPostS3UploadWorkItem> DequeueAsync(CancellationToken cancellationToken);
    }

    public class FacebookPostS3UploadQueue : IFacebookPostS3UploadQueue
    {
        private readonly Channel<FacebookPostS3UploadWorkItem> _queue = Channel.CreateUnbounded<FacebookPostS3UploadWorkItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        public ValueTask QueueAsync(FacebookPostS3UploadWorkItem workItem, CancellationToken cancellationToken)
        {
            return _queue.Writer.WriteAsync(workItem, cancellationToken);
        }

        public ValueTask<FacebookPostS3UploadWorkItem> DequeueAsync(CancellationToken cancellationToken)
        {
            return _queue.Reader.ReadAsync(cancellationToken);
        }
    }

    public class FacebookPostS3DownloadCancellation : IFacebookPostS3DownloadCancellation
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, CancellationTokenSource> _tokens = new();

        public CancellationTokenSource CreateLinkedTokenSource(int postId, CancellationToken cancellationToken)
        {
            var nextTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return _tokens.AddOrUpdate(
                postId,
                nextTokenSource,
                (_, existingTokenSource) =>
                {
                    existingTokenSource.Cancel();
                    existingTokenSource.Dispose();
                    return nextTokenSource;
                });
        }

        public void Cancel(IEnumerable<int> postIds)
        {
            foreach (var postId in postIds.Distinct())
            {
                if (_tokens.TryGetValue(postId, out var tokenSource))
                {
                    tokenSource.Cancel();
                }
            }
        }

        public void Clear(int postId)
        {
            if (_tokens.TryRemove(postId, out var tokenSource))
            {
                tokenSource.Dispose();
            }
        }
    }
}
