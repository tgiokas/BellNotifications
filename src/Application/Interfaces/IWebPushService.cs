using BellNotification.Application.Dtos;

namespace BellNotification.Application.Interfaces;

public interface IWebPushService
{
    string GetVapidPublicKey();
    Task SendPushAsync(string? tenantId, string userId, NotificationListItemDto notification);
}
