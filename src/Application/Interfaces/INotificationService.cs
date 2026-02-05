using BellNotification.Application.Dtos;

namespace BellNotification.Application.Interfaces;

public interface INotificationService
{
    Task<UnreadCountResponse> GetUnreadCountAsync(string? tenantId, string userId, CancellationToken cancellationToken = default);
    Task<NotificationListResponse> GetNotificationsAsync(string? tenantId, string userId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(string? tenantId, string userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(string? tenantId, string userId, CancellationToken cancellationToken = default);
    Task DismissAsync(string? tenantId, string userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task<Guid> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default);
}
