using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebPush;
using BellNotification.Application.Dtos;
using BellNotification.Application.Interfaces;

namespace BellNotification.Infrastructure.Services;

public class WebPushService : IWebPushService
{
    private readonly string _vapidPublicKey;
    private readonly string _vapidPrivateKey;
    private readonly string _vapidSubject;
    private readonly ConcurrentDictionary<string, PushSubscriptionData> _subscriptions;
    private readonly ILogger<WebPushService> _logger;

    public WebPushService(
        ConcurrentDictionary<string, PushSubscriptionData> subscriptions,
        ILogger<WebPushService> logger)
    {
        _vapidPublicKey = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY")
            ?? throw new InvalidOperationException("VAPID_PUBLIC_KEY environment variable is not set");
        _vapidPrivateKey = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY")
            ?? throw new InvalidOperationException("VAPID_PRIVATE_KEY environment variable is not set");
        _vapidSubject = Environment.GetEnvironmentVariable("VAPID_SUBJECT") ?? "mailto:admin@cbs.gr";
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public string GetVapidPublicKey() => _vapidPublicKey;

    public async Task SendPushAsync(string? tenantId, string userId, NotificationListItemDto notification)
    {
        var key = GetSubscriptionKey(tenantId, userId);
        if (!_subscriptions.TryGetValue(key, out var subscriptionData))
        {
            _logger.LogDebug("No push subscription found for tenant: {TenantId}, user: {UserId}", tenantId, userId);
            return;
        }

        try
        {
            var pushSubscription = new PushSubscription(
                subscriptionData.Endpoint,
                subscriptionData.P256dh,
                subscriptionData.Auth);

            var vapidDetails = new VapidDetails(_vapidSubject, _vapidPublicKey, _vapidPrivateKey);

            var payload = JsonSerializer.Serialize(new
            {
                title = notification.Title,
                body = notification.Body,
                link = notification.Link,
                type = notification.Type,
                id = notification.Id.ToString(),
                createdAtUtc = notification.CreatedAtUtc
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var client = new WebPushClient();
            await client.SendNotificationAsync(pushSubscription, payload, vapidDetails);
            
            _logger.LogInformation("Web push notification sent successfully to tenant: {TenantId}, user: {UserId}", tenantId, userId);
        }
        catch (WebPushException ex)
        {
            _logger.LogWarning(ex, "Failed to send web push notification to tenant: {TenantId}, user: {UserId}. Status: {StatusCode}", 
                tenantId, userId, ex.StatusCode);
            
            // If subscription is gone (410 Gone), remove it
            if (ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _subscriptions.TryRemove(key, out _);
                _logger.LogInformation("Removed expired push subscription for tenant: {TenantId}, user: {UserId}", tenantId, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending web push notification to tenant: {TenantId}, user: {UserId}", 
                tenantId, userId);
        }
    }

    private static string GetSubscriptionKey(string? tenantId, string userId)
    {
        return $"{tenantId ?? "null"}:{userId}";
    }
}

public class PushSubscriptionData
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}
