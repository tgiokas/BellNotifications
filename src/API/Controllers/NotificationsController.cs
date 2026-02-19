using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BellNotification.Application.Dtos;
using BellNotification.Application.Interfaces;
using BellNotification.API.Models;
using BellNotification.Infrastructure.Services;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace BellNotification.API.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IWebPushService _webPushService;
    private readonly ConcurrentDictionary<string, PushSubscriptionData> _subscriptions;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationService notificationService,
        IWebPushService webPushService,
        ConcurrentDictionary<string, PushSubscriptionData> subscriptions,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _webPushService = webPushService;
        _subscriptions = subscriptions;
        _logger = logger;
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount(CancellationToken cancellationToken)
    {
        var (tenantId, userId) = GetUserContext();
        var result = await _notificationService.GetUnreadCountAsync(tenantId, userId, cancellationToken);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<NotificationListResponse>> GetNotifications(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var (tenantId, userId) = GetUserContext();
        var result = await _notificationService.GetNotificationsAsync(tenantId, userId, cursor, limit, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var (tenantId, userId) = GetUserContext();
        await _notificationService.MarkAsReadAsync(tenantId, userId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var (tenantId, userId) = GetUserContext();
        await _notificationService.MarkAllAsReadAsync(tenantId, userId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken cancellationToken)
    {
        var (tenantId, userId) = GetUserContext();
        await _notificationService.DismissAsync(tenantId, userId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> CreateNotification(
        [FromBody] CreateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        // Check if internal endpoint is enabled
        var allowInternalCreationStr = Environment.GetEnvironmentVariable("FEATURES_ALLOW_INTERNAL_NOTIFICATION_CREATION");
        var allowInternalCreation = bool.TryParse(allowInternalCreationStr, out var allow) && allow;

        if (!allowInternalCreation)
        {
            return Forbid();
        }

        var id = await _notificationService.CreateNotificationAsync(request, cancellationToken);
        return Ok(new { id });
    }

    // ─── Web Push Subscription Endpoints ────────────────────────────────────

    [HttpPost("push-subscription")]
    public IActionResult SavePushSubscription([FromBody] PushSubscriptionPayload payload)
    {
        var (tenantId, userId) = GetUserContext();
        
        // Override userId and tenantId from token for security
        payload.UserId = userId;
        payload.TenantId = tenantId;

        var key = GetSubscriptionKey(tenantId, userId);
        _subscriptions[key] = new PushSubscriptionData
        {
            Endpoint = payload.Endpoint,
            P256dh = payload.P256dh,
            Auth = payload.Auth
        };

        _logger.LogInformation("Push subscription saved for tenant: {TenantId}, user: {UserId}", tenantId, userId);
        return Ok(new { message = "Subscription saved" });
    }

    [HttpDelete("push-subscription")]
    public IActionResult DeletePushSubscription()
    {
        var (tenantId, userId) = GetUserContext();
        var key = GetSubscriptionKey(tenantId, userId);
        
        if (_subscriptions.TryRemove(key, out _))
        {
            _logger.LogInformation("Push subscription removed for tenant: {TenantId}, user: {UserId}", tenantId, userId);
        }

        return NoContent();
    }

    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    public IActionResult GetVapidPublicKey()
    {
        return Ok(new { publicKey = _webPushService.GetVapidPublicKey() });
    }

    // ─── Test Endpoints ──────────────────────────────────────────────────────

    [HttpPost("test-push")]
    public async Task<IActionResult> SendTestPush(CancellationToken cancellationToken)
    {
        var (tenantId, userId) = GetUserContext();

        var notification = new NotificationListItemDto
        {
            Id = Guid.NewGuid(),
            Title = "Νέο έγγραφο για έγκριση",
            Body = "Το έγγραφο 'Σύμβαση 2024-001' απαιτεί την έγκρισή σας.",
            Type = "APPROVAL_NEEDED",
            Link = "/documents/test-001",
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false,
            IsDismissed = false
        };

        // Send via Web Push if subscribed
        var pushSent = false;
        var key = GetSubscriptionKey(tenantId, userId);
        if (_subscriptions.ContainsKey(key))
        {
            try
            {
                await _webPushService.SendPushAsync(tenantId, userId, notification);
                pushSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send test push notification");
            }
        }

        return Ok(new
        {
            message = "Test notification sent",
            pushSent = pushSent
        });
    }

    [HttpPost("simulate-event")]
    public async Task<IActionResult> SimulateEvent(
        [FromQuery] string eventType = "APPROVAL_NEEDED",
        CancellationToken cancellationToken = default)
    {
        var (tenantId, userId) = GetUserContext();

        var templates = new Dictionary<string, NotificationListItemDto>
        {
            ["DOCUMENT_SUBMITTED"] = new()
            {
                Id = Guid.NewGuid(),
                Title = "Νέο έγγραφο υποβλήθηκε",
                Body = "Ο χρήστης Γεωργίου υπέβαλε νέο έγγραφο για επεξεργασία.",
                Type = "DOCUMENT_SUBMITTED",
                Link = "/documents/submitted-001",
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false,
                IsDismissed = false
            },
            ["APPROVAL_NEEDED"] = new()
            {
                Id = Guid.NewGuid(),
                Title = "Απαιτείται έγκριση",
                Body = "Η αίτηση #2024-089 αναμένει την έγκρισή σας.",
                Type = "APPROVAL_NEEDED",
                Link = "/documents/pending-089",
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false,
                IsDismissed = false
            },
            ["DOCUMENT_APPROVED"] = new()
            {
                Id = Guid.NewGuid(),
                Title = "Έγγραφο εγκρίθηκε ✓",
                Body = "Η σύμβαση 'CBS-2024-022' εγκρίθηκε επιτυχώς.",
                Type = "DOCUMENT_APPROVED",
                Link = "/documents/approved-022",
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false,
                IsDismissed = false
            },
            ["DOCUMENT_REJECTED"] = new()
            {
                Id = Guid.NewGuid(),
                Title = "Έγγραφο απορρίφθηκε",
                Body = "Η αίτηση #2024-091 απορρίφθηκε. Απαιτείται διόρθωση.",
                Type = "DOCUMENT_REJECTED",
                Link = "/documents/rejected-091",
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false,
                IsDismissed = false
            }
        };

        if (!templates.TryGetValue(eventType, out var notification))
        {
            return BadRequest(new { error = "Unknown event type. Valid types: DOCUMENT_SUBMITTED, APPROVAL_NEEDED, DOCUMENT_APPROVED, DOCUMENT_REJECTED" });
        }

        // Send via Web Push if subscribed
        var key = GetSubscriptionKey(tenantId, userId);
        if (_subscriptions.ContainsKey(key))
        {
            try
            {
                await _webPushService.SendPushAsync(tenantId, userId, notification);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send simulated push notification");
            }
        }

        return Ok(notification);
    }

    private (string? TenantId, string UserId) GetUserContext()
    {
        var userId = User.FindFirstValue("sub") 
            ?? User.FindFirstValue("user_id") 
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        var tenantId = User.FindFirstValue("tenant_id");

        return (tenantId, userId);
    }

    private static string GetSubscriptionKey(string? tenantId, string userId)
    {
        return $"{tenantId ?? "null"}:{userId}";
    }
}
