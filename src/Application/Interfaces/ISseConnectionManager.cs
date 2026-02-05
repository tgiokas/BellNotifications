using BellNotification.Application.Dtos;

namespace BellNotification.Application.Interfaces;

/// <summary>
/// Manages active SSE connections for broadcasting real-time updates.
/// </summary>
public interface ISseConnectionManager
{
    void RegisterConnection(string? tenantId, string userId, ISseClient client);
    void UnregisterConnection(string? tenantId, string userId, ISseClient client);
    Task BroadcastUnreadCountAsync(string? tenantId, string userId, int unreadCount, CancellationToken cancellationToken = default);
    Task BroadcastNotificationCreatedAsync(string? tenantId, string userId, NotificationListItemDto notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an SSE client connection.
/// </summary>
public interface ISseClient
{
    Task SendAsync(string eventType, object data, CancellationToken cancellationToken = default);
    bool IsConnected { get; }
}
