using BellNotification.Domain.Entities;

namespace BellNotification.Domain.Interfaces;

public interface INotificationStatusRepository
{
    Task<NotificationStatus?> GetByNotificationIdAsync(Guid notificationId, string? tenantId, string userId,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(string? tenantId, string userId,
        CancellationToken cancellationToken = default);

    Task<NotificationStatus> AddAsync(NotificationStatus status,
        CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(Guid notificationId, string? tenantId, string userId,
        CancellationToken cancellationToken = default);

    Task<int> MarkAllAsReadAsync(string? tenantId, string userId, DateTime readAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkAsDismissedAsync(Guid notificationId, string? tenantId, string userId,
        CancellationToken cancellationToken = default);
}
