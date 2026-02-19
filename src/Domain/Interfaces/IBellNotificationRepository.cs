using BellNotification.Domain.Entities;

namespace BellNotification.Domain.Interfaces;

public interface IBellNotificationRepository
{
    Task<Entities.BellNotification?> GetByDedupeKeyAsync(string? tenantId, string userId, string dedupeKey,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<NotificationWithStatus>> GetNotificationsWithStatusAsync(string? tenantId, string userId, 
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<Entities.BellNotification> AddAsync(Entities.BellNotification notification,
        CancellationToken cancellationToken = default);
}
