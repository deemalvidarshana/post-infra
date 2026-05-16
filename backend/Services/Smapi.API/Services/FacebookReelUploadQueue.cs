using System.Threading.Channels;
using Smapi.API.Models.DTOs;

namespace Smapi.API.Services
{
    public record FacebookReelUploadWorkItem(int JobId, CreateFacebookReelUploadJobRequest Request);

    public interface IFacebookReelUploadQueue
    {
        ValueTask QueueAsync(FacebookReelUploadWorkItem workItem, CancellationToken cancellationToken);

        ValueTask<FacebookReelUploadWorkItem> DequeueAsync(CancellationToken cancellationToken);
    }

    public class FacebookReelUploadQueue : IFacebookReelUploadQueue
    {
        private readonly Channel<FacebookReelUploadWorkItem> _queue = Channel.CreateUnbounded<FacebookReelUploadWorkItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        public ValueTask QueueAsync(FacebookReelUploadWorkItem workItem, CancellationToken cancellationToken)
        {
            return _queue.Writer.WriteAsync(workItem, cancellationToken);
        }

        public ValueTask<FacebookReelUploadWorkItem> DequeueAsync(CancellationToken cancellationToken)
        {
            return _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}
