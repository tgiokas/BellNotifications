using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BellNotification.Application.Dtos;
using BellNotification.Application.Interfaces;
using BellNotificationEntity = BellNotification.Domain.Entities.BellNotification;
using NotificationStatusEntity = BellNotification.Domain.Entities.NotificationStatus;
using System.Text;
using System.Text.Json;

namespace BellNotification.Application.Services;

public class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<NotificationService> _logger;
    private readonly ISseConnectionManager _sseConnectionManager;

    public NotificationService(
        IApplicationDbContext context,
        ILogger<NotificationService> logger,
        ISseConnectionManager sseConnectionManager)
    {
        _context = context;
        _logger = logger;
        _sseConnectionManager = sseConnectionManager;
    }

    public async Task<UnreadCountResponse> GetUnreadCountAsync(string? tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var count = await _context.NotificationStatuses
            .Where(s => s.TenantId == tenantId && s.UserId == userId && s.ReadAtUtc == null && s.DismissedAtUtc == null)
            .CountAsync(cancellationToken);

        return new UnreadCountResponse { UnreadCount = count };
    }

    public async Task<NotificationListResponse> GetNotificationsAsync(string? tenantId, string userId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        var baseQuery = _context.BellNotifications
            .Where(n => n.TenantId == tenantId && n.UserId == userId)
            .Join(
                _context.NotificationStatuses,
                n => new { n.Id, n.TenantId, n.UserId },
                s => new { Id = s.NotificationId, s.TenantId, s.UserId },
                (n, s) => new { Notification = n, Status = s }
            );

        // Apply cursor-based pagination before ordering
        if (!string.IsNullOrEmpty(cursor))
        {
            var cursorData = DecodeCursor(cursor);
            if (cursorData.HasValue)
            {
                var (cursorCreatedAt, cursorId) = cursorData.Value;
                baseQuery = baseQuery.Where(x => 
                    x.Notification.CreatedAtUtc < cursorCreatedAt ||
                    (x.Notification.CreatedAtUtc == cursorCreatedAt && x.Notification.Id.CompareTo(cursorId) < 0));
            }
        }

        var query = baseQuery
            .OrderByDescending(x => x.Notification.CreatedAtUtc)
            .ThenByDescending(x => x.Notification.Id);

        var items = await query
            .Take(limit + 1) // Take one extra to check if there's a next page
            .Select(x => new NotificationListItemDto
            {
                Id = x.Notification.Id,
                Type = x.Notification.Type,
                Title = x.Notification.Title,
                Body = x.Notification.Body,
                Link = x.Notification.Link,
                Severity = x.Notification.Severity,
                SourceService = x.Notification.SourceService,
                CreatedAtUtc = x.Notification.CreatedAtUtc,
                IsRead = x.Status.ReadAtUtc != null,
                IsDismissed = x.Status.DismissedAtUtc != null
            })
            .ToListAsync(cancellationToken);

        var hasNextPage = items.Count > limit;
        if (hasNextPage)
        {
            items.RemoveAt(items.Count - 1);
        }

        var response = new NotificationListResponse
        {
            Items = items
        };

        if (hasNextPage && items.Count > 0)
        {
            var lastItem = items.Last();
            response.NextCursor = EncodeCursor(lastItem.CreatedAtUtc, lastItem.Id);
        }

        return response;
    }

    public async Task MarkAsReadAsync(string? tenantId, string userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var status = await _context.NotificationStatuses
            .FirstOrDefaultAsync(s => 
                s.NotificationId == notificationId && 
                s.TenantId == tenantId && 
                s.UserId == userId, 
                cancellationToken);

        if (status == null)
        {
            throw new InvalidOperationException($"Notification status not found for notification {notificationId}");
        }

        if (status.ReadAtUtc == null)
        {
            status.ReadAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            // Broadcast updated unread count
            var unreadCount = await GetUnreadCountAsync(tenantId, userId, cancellationToken);
            await _sseConnectionManager.BroadcastUnreadCountAsync(tenantId, userId, unreadCount.UnreadCount, cancellationToken);
        }
    }

    public async Task MarkAllAsReadAsync(string? tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var updated = await _context.NotificationStatuses
            .Where(s => s.TenantId == tenantId && s.UserId == userId && s.ReadAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.ReadAtUtc, now), cancellationToken);

        if (updated > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);

            // Broadcast updated unread count
            var unreadCount = await GetUnreadCountAsync(tenantId, userId, cancellationToken);
            await _sseConnectionManager.BroadcastUnreadCountAsync(tenantId, userId, unreadCount.UnreadCount, cancellationToken);
        }
    }

    public async Task DismissAsync(string? tenantId, string userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var status = await _context.NotificationStatuses
            .FirstOrDefaultAsync(s => 
                s.NotificationId == notificationId && 
                s.TenantId == tenantId && 
                s.UserId == userId, 
                cancellationToken);

        if (status == null)
        {
            throw new InvalidOperationException($"Notification status not found for notification {notificationId}");
        }

        if (status.DismissedAtUtc == null)
        {
            status.DismissedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            // Broadcast updated unread count
            var unreadCount = await GetUnreadCountAsync(tenantId, userId, cancellationToken);
            await _sseConnectionManager.BroadcastUnreadCountAsync(tenantId, userId, unreadCount.UnreadCount, cancellationToken);
        }
    }

    public async Task<Guid> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        // Check for deduplication
        if (!string.IsNullOrEmpty(request.DedupeKey))
        {
            var existing = await _context.BellNotifications
                .FirstOrDefaultAsync(n => 
                    n.TenantId == request.TenantId && 
                    n.UserId == request.UserId && 
                    n.DedupeKey == request.DedupeKey, 
                    cancellationToken);

            if (existing != null)
            {
                _logger.LogInformation("Duplicate notification skipped due to DedupeKey: {DedupeKey}", request.DedupeKey);
                return existing.Id;
            }
        }

        var notification = new BellNotificationEntity
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

        _context.BellNotifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);

        // Create status entry
        var status = new NotificationStatusEntity
        {
            NotificationId = notification.Id,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ReadAtUtc = null,
            DismissedAtUtc = null
        };

        _context.NotificationStatuses.Add(status);
        await _context.SaveChangesAsync(cancellationToken);

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
