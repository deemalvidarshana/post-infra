using System.Text.Json;
using Smapi.API.Data;
using Smapi.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Services
{
    public interface IFacebookWebhookReceiver
    {
        Task<int> ReceiveAsync(
            string rawPayload,
            FacebookMetaApp? metaApp,
            CancellationToken cancellationToken);
    }

    public class FacebookWebhookReceiver : IFacebookWebhookReceiver
    {
        private const string UnknownUserId = "__unknown__";
        private readonly SmapiDbContext _context;
        private readonly ILogger<FacebookWebhookReceiver> _logger;

        public FacebookWebhookReceiver(
            SmapiDbContext context,
            ILogger<FacebookWebhookReceiver> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> ReceiveAsync(
            string rawPayload,
            FacebookMetaApp? metaApp,
            CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(rawPayload);
            if (!document.RootElement.TryGetProperty("entry", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var savedCount = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                var entryPageId = GetString(entry, "id");
                if (!entry.TryGetProperty("changes", out var changes)
                    || changes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    var field = GetString(change, "field");
                    if (!string.Equals(field, "feed", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!change.TryGetProperty("value", out var value)
                        || value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var item = GetString(value, "item");
                    if (!string.Equals(item, "comment", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var pageId = FirstNonEmpty(GetString(value, "page_id"), entryPageId);
                    var commentId = FirstNonEmpty(
                        GetString(value, "comment_id"),
                        GetString(value, "commentId"),
                        GetString(value, "id"));

                    if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(commentId))
                    {
                        _logger.LogWarning("Facebook webhook comment event skipped because page_id or comment_id was missing.");
                        continue;
                    }

                    var exists = await _context.FacebookCommentEvents
                        .AsNoTracking()
                        .AnyAsync(item => item.PageId == pageId && item.CommentId == commentId, cancellationToken);

                    if (exists)
                    {
                        continue;
                    }

                    var pageQuery = _context.FacebookPages.AsNoTracking();
                    if (metaApp is not null)
                    {
                        pageQuery = pageQuery.Where(item => item.UserId == metaApp.UserId);
                    }

                    pageQuery = metaApp is null
                        ? pageQuery.OrderByDescending(item => item.ConnectedAt)
                        : pageQuery
                            .OrderByDescending(item => item.FacebookMetaAppId == metaApp.Id)
                            .ThenByDescending(item => item.ConnectedAt);

                    var page = await pageQuery.FirstOrDefaultAsync(item => item.PageId == pageId, cancellationToken);

                    var userId = page?.UserId ?? metaApp?.UserId ?? UnknownUserId;
                    var setting = page is null
                        ? null
                        : await _context.FacebookAutoReplySettings
                            .AsNoTracking()
                            .FirstOrDefaultAsync(
                                item => item.UserId == page.UserId && item.PageId == page.PageId,
                                cancellationToken);

                    var authorId = FirstNonEmpty(
                        GetNestedString(value, "from", "id"),
                        GetString(value, "sender_id"),
                        GetString(value, "senderId"));
                    var commentText = FirstNonEmpty(
                        GetString(value, "message"),
                        GetString(value, "text"),
                        GetString(value, "comment"));
                    var verb = FirstNonEmpty(GetString(value, "verb"), "add")!;
                    var postId = FirstNonEmpty(GetString(value, "post_id"), GetString(value, "postId"));
                    var parentCommentId = FirstNonEmpty(GetString(value, "parent_id"), GetString(value, "parentCommentId"));

                    var eventStatus = FacebookCommentEventStatus.Queued;
                    string? skipReason = null;

                    if (page is null)
                    {
                        eventStatus = FacebookCommentEventStatus.Skipped;
                        skipReason = "Facebook Page is not connected in SM Automate.";
                    }
                    else if (setting is null || !setting.Enabled)
                    {
                        eventStatus = FacebookCommentEventStatus.Skipped;
                        skipReason = "Auto reply is disabled for this page.";
                    }
                    else if (!string.Equals(verb, "add", StringComparison.OrdinalIgnoreCase))
                    {
                        eventStatus = FacebookCommentEventStatus.Skipped;
                        skipReason = $"Webhook verb '{verb}' is not an add event.";
                    }
                    else if (string.IsNullOrWhiteSpace(commentText))
                    {
                        eventStatus = FacebookCommentEventStatus.Skipped;
                        skipReason = "Comment text was empty.";
                    }
                    else if (!string.IsNullOrWhiteSpace(authorId)
                        && string.Equals(authorId, page.PageId, StringComparison.OrdinalIgnoreCase))
                    {
                        eventStatus = FacebookCommentEventStatus.Skipped;
                        skipReason = "Ignored the page's own comment to avoid reply loops.";
                    }
                    else if (ContainsKeyword(commentText, setting.IgnoreKeywords))
                    {
                        eventStatus = FacebookCommentEventStatus.Skipped;
                        skipReason = "Matched an ignore keyword.";
                    }
                    else if (ContainsKeyword(commentText, setting.EscalationKeywords))
                    {
                        eventStatus = FacebookCommentEventStatus.PendingApproval;
                        skipReason = "Matched an escalation keyword; waiting for manual approval.";
                    }
                    else if (!string.IsNullOrWhiteSpace(postId)
                        && await HasReachedDailyPostLimitAsync(setting, postId, cancellationToken))
                    {
                        eventStatus = FacebookCommentEventStatus.Skipped;
                        skipReason = "Reached the configured daily reply limit for this post.";
                    }

                    _context.FacebookCommentEvents.Add(new FacebookCommentEvent
                    {
                        UserId = userId,
                        PageId = pageId,
                        PostId = postId,
                        CommentId = commentId,
                        ParentCommentId = parentCommentId,
                        CommentText = commentText,
                        CommentAuthorId = authorId,
                        CommentAuthorName = FirstNonEmpty(
                            GetNestedString(value, "from", "name"),
                            GetString(value, "sender_name"),
                            GetString(value, "senderName")),
                        Verb = verb,
                        Status = eventStatus,
                        SkipReason = skipReason,
                        RawPayload = value.GetRawText(),
                        ReceivedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    savedCount++;
                }
            }

            if (savedCount > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return savedCount;
        }

        private async Task<bool> HasReachedDailyPostLimitAsync(
            FacebookAutoReplySetting setting,
            string postId,
            CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var repliedToday = await _context.FacebookCommentEvents
                .AsNoTracking()
                .Where(item => item.UserId == setting.UserId
                    && item.PageId == setting.PageId
                    && item.PostId == postId
                    && item.ReceivedAt >= today
                    && (item.Status == FacebookCommentEventStatus.Replied
                        || item.Status == FacebookCommentEventStatus.PendingApproval
                        || item.Status == FacebookCommentEventStatus.Queued
                        || item.Status == FacebookCommentEventStatus.Processing))
                .CountAsync(cancellationToken);

            return repliedToday >= setting.MaxRepliesPerPostPerDay;
        }

        private static bool ContainsKeyword(string text, string? csvKeywords)
        {
            if (string.IsNullOrWhiteSpace(csvKeywords))
            {
                return false;
            }

            return csvKeywords
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                }
                : null;
        }

        private static string? GetNestedString(JsonElement element, string objectName, string propertyName)
        {
            return element.TryGetProperty(objectName, out var nested)
                && nested.ValueKind == JsonValueKind.Object
                    ? GetString(nested, propertyName)
                    : null;
        }
    }
}
