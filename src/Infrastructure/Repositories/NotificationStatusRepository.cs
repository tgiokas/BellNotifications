using Microsoft.EntityFrameworkCore;

using BellNotification.Domain.Entities;
using BellNotification.Domain.Interfaces;
using BellNotification.Infrastructure.Database;

namespace BellNotification.Infrastructure.Repositories;

public class NotificationStatusRepository : INotificationStatusRepository
{
    private readonly ApplicationDbContext _dbContext;

    public NotificationStatusRepository(ApplicationDbContext context)
    {
        _dbContext = context;
    }

    public async Task<NotificationStatus?> GetByNotificationIdAsync(Guid notificationId, string? tenantId, string userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.NotificationId == notificationId &&
                s.TenantId == tenantId &&
                s.UserId == userId,
                cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(string? tenantId, string userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationStatuses
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.UserId == userId && s.ReadAtUtc == null && s.DismissedAtUtc == null)
            .CountAsync(cancellationToken);
    }

    public async Task<NotificationStatus> AddAsync(NotificationStatus status,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.NotificationStatuses.AddAsync(status, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return status;
    }

    public async Task MarkAsReadAsync(Guid notificationId, string? tenantId, string userId,
        CancellationToken cancellationToken = default)
    {
        var status = await _dbContext.NotificationStatuses
            .FirstOrDefaultAsync(s =>
                s.NotificationId == notificationId &&
                s.TenantId == tenantId &&
                s.UserId == userId,
                cancellationToken);

        if (status != null && status.ReadAtUtc == null)
        {
            status.ReadAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> MarkAllAsReadAsync(string? tenantId, string userId, DateTime readAtUtc,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationStatuses
            .Where(s => s.TenantId == tenantId && s.UserId == userId && s.ReadAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.ReadAtUtc, readAtUtc), cancellationToken);
    }

    public async Task MarkAsDismissedAsync(Guid notificationId, string? tenantId, string userId,
        CancellationToken cancellationToken = default)
    {
        var status = await _dbContext.NotificationStatuses
            .FirstOrDefaultAsync(s =>
                s.NotificationId == notificationId &&
                s.TenantId == tenantId &&
                s.UserId == userId,
                cancellationToken);

        if (status != null && status.DismissedAtUtc == null)
        {
            status.DismissedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

}
