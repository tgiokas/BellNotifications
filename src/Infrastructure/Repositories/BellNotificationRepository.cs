using Microsoft.EntityFrameworkCore;

using BellNotification.Domain.Entities;
using BellNotification.Domain.Interfaces;
using BellNotification.Infrastructure.Database;

namespace BellNotification.Infrastructure.Repositories;

public class BellNotificationRepository : IBellNotificationRepository
{
    private readonly ApplicationDbContext _dbContext;

    public BellNotificationRepository(ApplicationDbContext context)
    {
        _dbContext = context;
    }

    public async Task<Domain.Entities.BellNotification?> GetByDedupeKeyAsync(string? tenantId, string userId, string dedupeKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.BellNotifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n =>
                n.TenantId == tenantId &&
                n.UserId == userId &&
                n.DedupeKey == dedupeKey,
                cancellationToken);
    }

    public async Task<IEnumerable<NotificationWithStatus>> GetNotificationsWithStatusAsync(string? tenantId, string userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.BellNotifications
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.UserId == userId)
            .Join(
                _dbContext.NotificationStatuses,
                n => new { n.Id, n.TenantId, n.UserId },
                s => new { Id = s.NotificationId, s.TenantId, s.UserId },
                (n, s) => new { Notification = n, Status = s }
            );

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            baseQuery = baseQuery.Where(x =>
                x.Notification.CreatedAtUtc < cursorCreatedAt.Value ||
                (x.Notification.CreatedAtUtc == cursorCreatedAt.Value &&
                 x.Notification.Id.CompareTo(cursorId.Value) < 0));
        }

        // Single query execution
        return await baseQuery
            .OrderByDescending(x => x.Notification.CreatedAtUtc)
            .ThenByDescending(x => x.Notification.Id)
            .Take(limit)
            .Select(x => new NotificationWithStatus
            {
                Id = x.Notification.Id,
                TenantId = x.Notification.TenantId,
                UserId = x.Notification.UserId,
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
    }

    public async Task<Domain.Entities.BellNotification> AddAsync(Domain.Entities.BellNotification notification,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.BellNotifications.AddAsync(notification, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return notification;
    }

}
