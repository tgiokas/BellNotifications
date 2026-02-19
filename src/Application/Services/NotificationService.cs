using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using BellNotification.Application.Dtos;
using BellNotification.Application.Interfaces;
using BellNotification.Domain.Interfaces;

namespace BellNotification.Application.Services;

public class NotificationService : INotificationService
{
    private readonly IBellNotificationRepository _notificationRepository;
    private readonly INotificationStatusRepository _statusRepository;    
    private readonly ISseConnectionManager _sseConnectionManager;
    private readonly IWebPushService? _webPushService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IBellNotificationRepository notificationRepository,
        INotificationStatusRepository statusRepository,        
        ISseConnectionManager sseConnectionManager,
        ILogger<NotificationService> logger,
        IWebPushService? webPushService = null)
    {
        _notificationRepository = notificationRepository;
        _statusRepository = statusRepository;       
        _sseConnectionManager = sseConnectionManager;
        _logger = logger;
        _webPushService = webPushService;
    }

    public async Task<UnreadCountResponse> GetUnreadCountAsync(string? tenantId, string userId,
        CancellationToken cancellationToken = default)
    {
        var count = await _statusRepository.GetUnreadCountAsync(tenantId, userId, cancellationToken);
        return new UnreadCountResponse { UnreadCount = count };
    }

    public async Task<NotificationListResponse> GetNotificationsAsync(string? tenantId, string userId, string? cursor, int limit, 
        CancellationToken cancellationToken = default)
    {
        // Decode cursor
        DateTime? cursorCreatedAt = null;
        Guid? cursorId = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            var cursorData = DecodeCursor(cursor);
            if (cursorData.HasValue)
            {
                cursorCreatedAt = cursorData.Value.CreatedAtUtc;
                cursorId = cursorData.Value.Id;
            }
        }

        // Get Notifications with status from repository
        var notificationsWithStatus = await _notificationRepository
            .GetNotificationsWithStatusAsync(
                tenantId, userId, cursorCreatedAt, cursorId, limit + 1, cancellationToken);

        // Map domain model to DTO 
        var items = notificationsWithStatus
            .Select(nws => new NotificationListItemDto
            {
                Id = nws.Id,
                Type = nws.Type,
                Title = nws.Title,
                Body = nws.Body,
                Link = nws.Link,
                Severity = nws.Severity,
                SourceService = nws.SourceService,
                CreatedAtUtc = nws.CreatedAtUtc,
                IsRead = nws.IsRead,
                IsDismissed = nws.IsDismissed
            })
            .ToList();

        // Pagination logic
        var hasNextPage = items.Count > limit;
        if (hasNextPage)
        {
            items.RemoveAt(items.Count - 1);
        }

        var response = new NotificationListResponse { Items = items };

        if (hasNextPage && items.Count > 0)
        {
            var lastItem = items.Last();
            response.NextCursor = EncodeCursor(lastItem.CreatedAtUtc, lastItem.Id);
        }

        return response;
    }

    public async Task MarkAsReadAsync(string? tenantId, string userId, Guid notificationId, 
        CancellationToken cancellationToken = default)
    {
        var status = await _statusRepository.GetByNotificationIdAsync(notificationId, tenantId, userId, cancellationToken);

        if (status == null)
        {
            throw new InvalidOperationException($"Notification status not found for notification {notificationId}");
        }

        if (status.ReadAtUtc == null)
        {            
            await _statusRepository.MarkAsReadAsync(notificationId, tenantId, userId, cancellationToken);

            // Broadcast updated unread count
            var unreadCount = await GetUnreadCountAsync(tenantId, userId, cancellationToken);
            await _sseConnectionManager.BroadcastUnreadCountAsync(tenantId, userId, unreadCount.UnreadCount, cancellationToken);
        }
    }

    public async Task MarkAllAsReadAsync(string? tenantId, string userId, 
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        
        // bulk update
        var updated = await _statusRepository.MarkAllAsReadAsync(tenantId, userId, now, cancellationToken);

        if (updated > 0)
        {
            // Broadcast updated unread count
            var unreadCount = await GetUnreadCountAsync(tenantId, userId, cancellationToken);
            await _sseConnectionManager.BroadcastUnreadCountAsync(tenantId, userId, unreadCount.UnreadCount, cancellationToken);
        }
    }

    public async Task DismissAsync(string? tenantId, string userId, Guid notificationId, 
        CancellationToken cancellationToken = default)
    {       
        var status = await _statusRepository.GetByNotificationIdAsync(notificationId, tenantId, userId, cancellationToken);

        if (status == null)
        {
            throw new InvalidOperationException($"Notification status not found for notification {notificationId}");
        }

        if (status.DismissedAtUtc == null)
        {            
            await _statusRepository.MarkAsDismissedAsync(notificationId, tenantId, userId, cancellationToken);

            // Broadcast updated unread count
            var unreadCount = await GetUnreadCountAsync(tenantId, userId, cancellationToken);
            await _sseConnectionManager.BroadcastUnreadCountAsync(tenantId, userId, unreadCount.UnreadCount, cancellationToken);
        }
    }

    public async Task<Guid> CreateNotificationAsync(CreateNotificationRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Check for deduplication
        if (!string.IsNullOrEmpty(request.DedupeKey))
        {            
            var existing = await _notificationRepository.GetByDedupeKeyAsync(
                request.TenantId, request.UserId, request.DedupeKey, cancellationToken);

            if (existing != null)
            {
                _logger.LogInformation("Duplicate notification skipped due to DedupeKey: {DedupeKey}", request.DedupeKey);
                return existing.Id;
            }
        }

        // Create notification entity
        var notification = new Domain.Entities.BellNotification
        {
            TenantId = request.TenantId,
            UserId = request.UserId,
            Type = request.Type,
            Title = request.Title,
            Body = request.Body,
            Link = request.Link,
            PayloadJson = request.PayloadJson,
            Severity = request.Severity,
            SourceService = request.SourceService,
            DedupeKey = request.DedupeKey,
            CreatedAtUtc = DateTime.UtcNow
        };        
        await _notificationRepository.AddAsync(notification, cancellationToken);

        // Create status entry
        var status = new Domain.Entities.NotificationStatus
        {
            NotificationId = notification.Id,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ReadAtUtc = null,
            DismissedAtUtc = null
        };       
        await _statusRepository.AddAsync(status, cancellationToken);

        // Broadcast to SSE clients
        var unreadCount = await GetUnreadCountAsync(request.TenantId, request.UserId, cancellationToken);
        await _sseConnectionManager.BroadcastUnreadCountAsync(request.TenantId, request.UserId, unreadCount.UnreadCount, cancellationToken);

        var notificationDto = new NotificationListItemDto
        {
            Id = notification.Id,
            Type = notification.Type,
            Title = notification.Title,
            Body = notification.Body,
            Link = notification.Link,
            Severity = notification.Severity,
            SourceService = notification.SourceService,
            CreatedAtUtc = notification.CreatedAtUtc,
            IsRead = false,
            IsDismissed = false
        };
        await _sseConnectionManager.BroadcastNotificationCreatedAsync(request.TenantId, request.UserId, notificationDto, cancellationToken);

        // Send Web Push notification if service is available and user is subscribed
        if (_webPushService != null)
        {
            try
            {
                await _webPushService.SendPushAsync(request.TenantId, request.UserId, notificationDto);
                _logger.LogDebug("Web push notification sent for notification {NotificationId} (tenant: {TenantId}, user: {UserId})", 
                    notification.Id, request.TenantId, request.UserId);
            }
            catch (Exception ex)
            {
                // Log but don't fail - Web Push is optional
                _logger.LogWarning(ex, "Failed to send web push notification for notification {NotificationId} (tenant: {TenantId}, user: {UserId})", 
                    notification.Id, request.TenantId, request.UserId);
            }
        }
        else
        {
            _logger.LogDebug("Web push service not available - skipping push notification for notification {NotificationId}", notification.Id);
        }

        return notification.Id;
    }

    private static string EncodeCursor(DateTime createdAtUtc, Guid id)
    {
        var cursorData = new
        {
            CreatedAtUtc = createdAtUtc.Ticks,
            Id = id.ToString()
        };
        var json = JsonSerializer.Serialize(cursorData);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static (DateTime CreatedAtUtc, Guid Id)? DecodeCursor(string cursor)
    {
        try
        {
            var bytes = Convert.FromBase64String(cursor);
            var json = Encoding.UTF8.GetString(bytes);
            var cursorData = JsonSerializer.Deserialize<CursorData>(json);
            if (cursorData != null)
            {
                return (new DateTime(cursorData.CreatedAtUtc), Guid.Parse(cursorData.Id));
            }
        }
        catch
        {
            // Invalid cursor, return null
        }
        return null;
    }

    private class CursorData
    {
        public long CreatedAtUtc { get; set; }
        public string Id { get; set; } = default!;
    }
}
