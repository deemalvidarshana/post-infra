using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public interface IFacebookCommentReplyProcessor
    {
        Task ProcessQueuedAsync(int eventId, CancellationToken cancellationToken);

        Task<FacebookCommentEvent> PublishApprovedAsync(
            int eventId,
            string? overrideReply,
            CancellationToken cancellationToken);
    }

    public class FacebookCommentReplyProcessor : IFacebookCommentReplyProcessor
    {
        private readonly SmapiDbContext _context;
        private readonly IFacebookCommentReplyGenerator _replyGenerator;
        private readonly IFacebookCommentsPublisher _publisher;

        public FacebookCommentReplyProcessor(
            SmapiDbContext context,
            IFacebookCommentReplyGenerator replyGenerator,
            IFacebookCommentsPublisher publisher)
        {
            _context = context;
            _replyGenerator = replyGenerator;
            _publisher = publisher;
        }

        public async Task ProcessQueuedAsync(int eventId, CancellationToken cancellationToken)
        {
            var commentEvent = await _context.FacebookCommentEvents
                .FirstOrDefaultAsync(item => item.Id == eventId, cancellationToken);

            if (commentEvent is null || commentEvent.Status != FacebookCommentEventStatus.Queued)
            {
                return;
            }

            try
            {
                commentEvent.Status = FacebookCommentEventStatus.Processing;
                commentEvent.Attempts += 1;
                commentEvent.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                var context = await BuildContextAsync(commentEvent, cancellationToken);
                var reply = await _replyGenerator.GenerateReplyAsync(context, cancellationToken);

                commentEvent.GeneratedReply = reply;
                commentEvent.UpdatedAt = DateTime.UtcNow;

                if (context.Setting.Mode == FacebookAutoReplyMode.ManualApproval)
                {
                    commentEvent.Status = FacebookCommentEventStatus.PendingApproval;
                    commentEvent.ProcessedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                    return;
                }

                var replyId = await _publisher.ReplyToCommentAsync(
                    commentEvent.CommentId,
                    context.Page.AccessToken,
                    reply,
                    context.Setting.GraphApiVersion,
                    commentEvent.CommentAuthorId,
                    commentEvent.CommentAuthorName,
                    cancellationToken);

                commentEvent.ReplyCommentId = replyId;
                commentEvent.Status = FacebookCommentEventStatus.Replied;
                commentEvent.ProcessedAt = DateTime.UtcNow;
                commentEvent.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                commentEvent.Status = FacebookCommentEventStatus.Failed;
                commentEvent.ErrorMessage = TrimForLog(ex.Message);
                commentEvent.ProcessedAt = DateTime.UtcNow;
                commentEvent.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<FacebookCommentEvent> PublishApprovedAsync(
            int eventId,
            string? overrideReply,
            CancellationToken cancellationToken)
        {
            var commentEvent = await _context.FacebookCommentEvents
                .FirstOrDefaultAsync(item => item.Id == eventId, cancellationToken)
                ?? throw new InvalidOperationException("Comment event was not found.");

            if (commentEvent.Status == FacebookCommentEventStatus.Replied)
            {
                return commentEvent;
            }

            var context = await BuildContextAsync(commentEvent, cancellationToken);
            var reply = FirstNonEmpty(overrideReply, commentEvent.GeneratedReply)
                ?? await _replyGenerator.GenerateReplyAsync(context, cancellationToken);

            var replyId = await _publisher.ReplyToCommentAsync(
                commentEvent.CommentId,
                context.Page.AccessToken,
                reply,
                context.Setting.GraphApiVersion,
                commentEvent.CommentAuthorId,
                commentEvent.CommentAuthorName,
                cancellationToken);

            commentEvent.GeneratedReply = reply;
            commentEvent.ReplyCommentId = replyId;
            commentEvent.Status = FacebookCommentEventStatus.Replied;
            commentEvent.ErrorMessage = null;
            commentEvent.SkipReason = null;
            commentEvent.ProcessedAt = DateTime.UtcNow;
            commentEvent.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return commentEvent;
        }

        private async Task<FacebookCommentReplyContext> BuildContextAsync(
            FacebookCommentEvent commentEvent,
            CancellationToken cancellationToken)
        {
            var page = await _context.FacebookPages
                .AsNoTracking()
                .OrderByDescending(item => item.ConnectedAt)
                .FirstOrDefaultAsync(
                    item => item.UserId == commentEvent.UserId && item.PageId == commentEvent.PageId,
                    cancellationToken)
                ?? await _context.FacebookPages
                    .AsNoTracking()
                    .OrderByDescending(item => item.ConnectedAt)
                    .FirstOrDefaultAsync(item => item.PageId == commentEvent.PageId, cancellationToken)
                ?? throw new InvalidOperationException("Connected Facebook Page was not found.");

            var setting = await _context.FacebookAutoReplySettings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.UserId == page.UserId && item.PageId == page.PageId,
                    cancellationToken)
                ?? throw new InvalidOperationException("Auto reply settings were not found for this page.");

            if (!setting.Enabled)
            {
                throw new InvalidOperationException("Auto reply is disabled for this page.");
            }

            if (string.IsNullOrWhiteSpace(page.AccessToken))
            {
                throw new InvalidOperationException("Facebook Page access token is missing.");
            }

            if (string.IsNullOrWhiteSpace(commentEvent.CommentText))
            {
                throw new InvalidOperationException("Comment text is empty.");
            }

            return new FacebookCommentReplyContext(page, setting, commentEvent);
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private static string TrimForLog(string value)
        {
            value = value.Trim();
            return value.Length <= 2000 ? value : value[..2000];
        }
    }
}
