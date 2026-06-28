using System.Security.Cryptography;
using System.Text;
using Smapi.API.Data;
using Smapi.API.Models;
using Smapi.API.Models.DTOs;
using Smapi.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Smapi.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FacebookWebhooksController : ControllerBase
    {
        private readonly SmapiDbContext _context;
        private readonly IFacebookWebhookReceiver _receiver;
        private readonly IFacebookCommentReplyProcessor _replyProcessor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FacebookWebhooksController> _logger;

        public FacebookWebhooksController(
            SmapiDbContext context,
            IFacebookWebhookReceiver receiver,
            IFacebookCommentReplyProcessor replyProcessor,
            IConfiguration configuration,
            ILogger<FacebookWebhooksController> logger)
        {
            _context = context;
            _receiver = receiver;
            _replyProcessor = replyProcessor;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("meta")]
        public Task<IActionResult> VerifyMetaWebhook(
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            return VerifyMetaWebhookInternalAsync(null, mode, verifyToken, challenge);
        }

        [HttpGet("meta/{webhookKey}")]
        public Task<IActionResult> VerifyMetaWebhookForApp(
            string webhookKey,
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            return VerifyMetaWebhookInternalAsync(webhookKey, mode, verifyToken, challenge);
        }

        [HttpPost("meta")]
        public Task<IActionResult> ReceiveMetaWebhook(CancellationToken cancellationToken)
        {
            return ReceiveMetaWebhookInternalAsync(null, cancellationToken);
        }

        [HttpPost("meta/{webhookKey}")]
        public Task<IActionResult> ReceiveMetaWebhookForApp(string webhookKey, CancellationToken cancellationToken)
        {
            return ReceiveMetaWebhookInternalAsync(webhookKey, cancellationToken);
        }

        private async Task<IActionResult> VerifyMetaWebhookInternalAsync(
            string? webhookKey,
            string? mode,
            string? verifyToken,
            string? challenge)
        {
            var metaApp = await ResolveMetaAppAsync(webhookKey, HttpContext.RequestAborted);
            var configuredVerifyToken = metaApp?.VerifyToken ?? _configuration["FacebookWebhook:VerifyToken"];
            if (string.IsNullOrWhiteSpace(configuredVerifyToken))
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    "Facebook webhook verify token is not configured.");
            }

            if (string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase)
                && string.Equals(verifyToken, configuredVerifyToken, StringComparison.Ordinal))
            {
                return Content(challenge ?? string.Empty, "text/plain", Encoding.UTF8);
            }

            return Unauthorized("Facebook webhook verification failed.");
        }

        private async Task<IActionResult> ReceiveMetaWebhookInternalAsync(
            string? webhookKey,
            CancellationToken cancellationToken)
        {
            var metaApp = await ResolveMetaAppAsync(webhookKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(webhookKey) && metaApp is null)
            {
                return NotFound(new { success = false, message = "Meta App webhook key was not found." });
            }

            using var payloadStream = new MemoryStream();
            await Request.Body.CopyToAsync(payloadStream, cancellationToken);
            var payloadBytes = payloadStream.ToArray();
            var rawPayload = Encoding.UTF8.GetString(payloadBytes);

            if (string.IsNullOrWhiteSpace(rawPayload))
            {
                return BadRequest(new { success = false, message = "Webhook payload was empty." });
            }

            if (!IsValidMetaSignature(payloadBytes, metaApp))
            {
                return Unauthorized(new { success = false, message = "Invalid Facebook webhook signature." });
            }

            try
            {
                var savedCount = await _receiver.ReceiveAsync(rawPayload, metaApp, cancellationToken);
                return Ok(new { success = true, savedCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Facebook webhook payload processing failed.");
                return Ok(new { success = true, savedCount = 0 });
            }
        }

        [HttpGet("settings/{userId}/{pageId}")]
        public async Task<ActionResult<FacebookAutoReplySettingResponse>> GetAutoReplySettings(
            string userId,
            string pageId,
            CancellationToken cancellationToken)
        {
            userId = userId.Trim();
            pageId = pageId.Trim();

            var setting = await _context.FacebookAutoReplySettings
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId && item.PageId == pageId, cancellationToken);

            return Ok(setting is null
                ? BuildDefaultSettingResponse(userId, pageId)
                : ToResponse(setting));
        }

        [HttpPut("settings")]
        public async Task<IActionResult> SaveAutoReplySettings(
            [FromBody] FacebookAutoReplySettingRequest request,
            CancellationToken cancellationToken)
        {
            request.UserId = request.UserId.Trim();
            request.PageId = request.PageId.Trim();

            if (string.IsNullOrWhiteSpace(request.UserId)
                || string.IsNullOrWhiteSpace(request.PageId))
            {
                return BadRequest(new { success = false, message = "User ID and Facebook Page ID are required." });
            }

            var pageExists = await _context.FacebookPages
                .AsNoTracking()
                .AnyAsync(item => item.UserId == request.UserId && item.PageId == request.PageId, cancellationToken);
            if (!pageExists)
            {
                return BadRequest(new { success = false, message = "Connect this Facebook Page before enabling auto replies." });
            }

            var setting = await _context.FacebookAutoReplySettings
                .FirstOrDefaultAsync(item => item.UserId == request.UserId && item.PageId == request.PageId, cancellationToken);
            var now = DateTime.UtcNow;

            if (setting is null)
            {
                setting = new FacebookAutoReplySetting
                {
                    UserId = request.UserId,
                    PageId = request.PageId,
                    CreatedAt = now
                };
                _context.FacebookAutoReplySettings.Add(setting);
            }

            setting.Enabled = request.Enabled;
            setting.Mode = NormalizeMode(request.Mode);
            setting.Prompt = NormalizePrompt(request.Prompt);
            setting.Tone = string.IsNullOrWhiteSpace(request.Tone) ? "Friendly" : request.Tone.Trim();
            setting.Language = string.IsNullOrWhiteSpace(request.Language) ? "Sinhala/English" : request.Language.Trim();
            setting.MaxRepliesPerPostPerDay = Math.Clamp(request.MaxRepliesPerPostPerDay, 1, 100);
            setting.IgnoreKeywords = NormalizeNullable(request.IgnoreKeywords);
            setting.EscalationKeywords = NormalizeNullable(request.EscalationKeywords);
            setting.GraphApiVersion = NormalizeGraphApiVersion(request.GraphApiVersion);
            setting.UpdatedAt = now;

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Facebook auto reply settings saved.",
                settings = ToResponse(setting)
            });
        }

        [HttpGet("events")]
        public async Task<ActionResult<IEnumerable<FacebookCommentEventResponse>>> GetCommentEvents(
            [FromQuery] string? userId,
            [FromQuery] string? pageId,
            [FromQuery] string? status,
            [FromQuery] int take = 100,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 200);
            userId = NormalizeNullable(userId);
            pageId = NormalizeNullable(pageId);
            status = NormalizeNullable(status);

            var query = _context.FacebookCommentEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(item => item.UserId == userId);
            }

            if (!string.IsNullOrWhiteSpace(pageId))
            {
                query = query.Where(item => item.PageId == pageId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(item => item.Status == status);
            }

            var events = await query
                .OrderByDescending(item => item.ReceivedAt)
                .Take(take)
                .ToListAsync(cancellationToken);

            return Ok(events.Select(ToResponse));
        }

        [HttpPost("events/{id:int}/approve")]
        public async Task<ActionResult<FacebookCommentEventResponse>> ApproveCommentReply(
            int id,
            [FromBody] FacebookCommentReplyRequest? request,
            CancellationToken cancellationToken)
        {
            try
            {
                var commentEvent = await _replyProcessor.PublishApprovedAsync(
                    id,
                    request?.Reply,
                    cancellationToken);

                return Ok(ToResponse(commentEvent));
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("events/{id:int}/retry")]
        public async Task<ActionResult<FacebookCommentEventResponse>> RetryCommentReply(
            int id,
            CancellationToken cancellationToken)
        {
            var commentEvent = await _context.FacebookCommentEvents
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (commentEvent is null)
            {
                return NotFound(new { success = false, message = "Comment event was not found." });
            }

            if (commentEvent.Status == FacebookCommentEventStatus.Replied)
            {
                return Conflict(new { success = false, message = "This comment already has a published reply." });
            }

            commentEvent.Status = FacebookCommentEventStatus.Queued;
            commentEvent.ErrorMessage = null;
            commentEvent.SkipReason = null;
            commentEvent.ProcessedAt = null;
            commentEvent.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(ToResponse(commentEvent));
        }

        [HttpPost("events/{id:int}/skip")]
        public async Task<ActionResult<FacebookCommentEventResponse>> SkipCommentReply(
            int id,
            CancellationToken cancellationToken)
        {
            var commentEvent = await _context.FacebookCommentEvents
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (commentEvent is null)
            {
                return NotFound(new { success = false, message = "Comment event was not found." });
            }

            if (commentEvent.Status == FacebookCommentEventStatus.Replied)
            {
                return Conflict(new { success = false, message = "This comment already has a published reply." });
            }

            commentEvent.Status = FacebookCommentEventStatus.Skipped;
            commentEvent.SkipReason = "Skipped manually.";
            commentEvent.ProcessedAt = DateTime.UtcNow;
            commentEvent.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(ToResponse(commentEvent));
        }

        private async Task<FacebookMetaApp?> ResolveMetaAppAsync(
            string? webhookKey,
            CancellationToken cancellationToken)
        {
            webhookKey = NormalizeNullable(webhookKey);
            if (string.IsNullOrWhiteSpace(webhookKey))
            {
                return null;
            }

            return await _context.FacebookMetaApps
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.WebhookKey == webhookKey, cancellationToken);
        }

        private bool IsValidMetaSignature(byte[] payloadBytes, FacebookMetaApp? metaApp)
        {
            var appSecret = metaApp?.AppSecret ?? _configuration["FacebookWebhook:AppSecret"];
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                return true;
            }

            var signatureHeader = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(signatureHeader)
                || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var providedSignature = signatureHeader["sha256=".Length..].Trim();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret.Trim()));
            var computedHash = hmac.ComputeHash(payloadBytes);
            var computedSignature = Convert.ToHexString(computedHash).ToLowerInvariant();

            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(computedSignature),
                Encoding.ASCII.GetBytes(providedSignature.ToLowerInvariant()));
        }

        private static FacebookAutoReplySettingResponse BuildDefaultSettingResponse(string userId, string pageId)
        {
            return new FacebookAutoReplySettingResponse
            {
                UserId = userId,
                PageId = pageId,
                Enabled = false,
                Mode = FacebookAutoReplyMode.ManualApproval,
                Prompt = FacebookAutoReplySetting.DefaultPrompt,
                Tone = "Friendly",
                Language = "Sinhala/English",
                MaxRepliesPerPostPerDay = 20,
                GraphApiVersion = "v24.0"
            };
        }

        private static FacebookAutoReplySettingResponse ToResponse(FacebookAutoReplySetting setting)
        {
            return new FacebookAutoReplySettingResponse
            {
                UserId = setting.UserId,
                PageId = setting.PageId,
                Enabled = setting.Enabled,
                Mode = setting.Mode,
                Prompt = setting.Prompt,
                Tone = setting.Tone,
                Language = setting.Language,
                MaxRepliesPerPostPerDay = setting.MaxRepliesPerPostPerDay,
                IgnoreKeywords = setting.IgnoreKeywords,
                EscalationKeywords = setting.EscalationKeywords,
                GraphApiVersion = setting.GraphApiVersion,
                UpdatedAt = setting.UpdatedAt
            };
        }

        private static FacebookCommentEventResponse ToResponse(FacebookCommentEvent commentEvent)
        {
            return new FacebookCommentEventResponse
            {
                Id = commentEvent.Id,
                UserId = commentEvent.UserId,
                PageId = commentEvent.PageId,
                PostId = commentEvent.PostId,
                CommentId = commentEvent.CommentId,
                ParentCommentId = commentEvent.ParentCommentId,
                CommentText = commentEvent.CommentText,
                CommentAuthorId = commentEvent.CommentAuthorId,
                CommentAuthorName = commentEvent.CommentAuthorName,
                Verb = commentEvent.Verb,
                Status = commentEvent.Status,
                GeneratedReply = commentEvent.GeneratedReply,
                ReplyCommentId = commentEvent.ReplyCommentId,
                SkipReason = commentEvent.SkipReason,
                ErrorMessage = commentEvent.ErrorMessage,
                Attempts = commentEvent.Attempts,
                ReceivedAt = commentEvent.ReceivedAt,
                UpdatedAt = commentEvent.UpdatedAt,
                ProcessedAt = commentEvent.ProcessedAt
            };
        }

        private static string NormalizeMode(string? mode)
        {
            return string.Equals(mode, FacebookAutoReplyMode.Auto, StringComparison.OrdinalIgnoreCase)
                ? FacebookAutoReplyMode.Auto
                : FacebookAutoReplyMode.ManualApproval;
        }

        private static string NormalizePrompt(string? prompt)
        {
            prompt = prompt?.Trim();
            return string.IsNullOrWhiteSpace(prompt)
                ? FacebookAutoReplySetting.DefaultPrompt
                : prompt;
        }

        private static string NormalizeGraphApiVersion(string? graphApiVersion)
        {
            graphApiVersion = graphApiVersion?.Trim();
            if (string.IsNullOrWhiteSpace(graphApiVersion))
            {
                return "v24.0";
            }

            return graphApiVersion.StartsWith('v') ? graphApiVersion : $"v{graphApiVersion}";
        }

        private static string? NormalizeNullable(string? value)
        {
            value = value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
