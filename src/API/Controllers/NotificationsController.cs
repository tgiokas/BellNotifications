using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BellNotification.Application.Dtos;
using BellNotification.Application.Interfaces;
using System.Security.Claims;

namespace BellNotification.API.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationService notificationService,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
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

    private (string? TenantId, string UserId) GetUserContext()
    {
        var userId = User.FindFirstValue("sub") 
            ?? User.FindFirstValue("user_id") 
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        var tenantId = User.FindFirstValue("tenant_id");

        return (tenantId, userId);
    }
}
