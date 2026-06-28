using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public class FacebookCommentReplyWorker : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FacebookCommentReplyWorker> _logger;

        public FacebookCommentReplyWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<FacebookCommentReplyWorker> logger)
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
                    var eventId = await GetNextQueuedEventIdAsync(stoppingToken);
                    if (eventId.HasValue)
                    {
                        await ProcessAsync(eventId.Value, stoppingToken);
                        continue;
                    }

                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected Facebook comment reply worker loop error.");
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
        }

        private async Task<int?> GetNextQueuedEventIdAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmapiDbContext>();

            return await dbContext.FacebookCommentEvents
                .AsNoTracking()
                .Where(item => item.Status == FacebookCommentEventStatus.Queued)
                .OrderBy(item => item.ReceivedAt)
                .Select(item => (int?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task ProcessAsync(int eventId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IFacebookCommentReplyProcessor>();
            await processor.ProcessQueuedAsync(eventId, cancellationToken);
        }
    }
}
