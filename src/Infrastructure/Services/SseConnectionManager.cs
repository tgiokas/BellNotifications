using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using BellNotification.Application.Dtos;
using BellNotification.Application.Interfaces;
using System.Text.Json;

namespace BellNotification.Infrastructure.Services;

public class SseConnectionManager : ISseConnectionManager
{
    private readonly ConcurrentDictionary<string, List<ISseClient>> _connections = new();
    private readonly ILogger<SseConnectionManager> _logger;

    public SseConnectionManager(ILogger<SseConnectionManager> logger)
    {
        _logger = logger;
    }

    public void RegisterConnection(string? tenantId, string userId, ISseClient client)
    {
        var key = GetConnectionKey(tenantId, userId);
        _connections.AddOrUpdate(
            key,
            new List<ISseClient> { client },
            (k, existing) =>
            {
                lock (existing)
                {
                    existing.Add(client);
                    return existing;
                }
            });

        _logger.LogInformation("Registered SSE connection for tenant: {TenantId}, user: {UserId}. Total connections: {Count}",
            tenantId ?? "null", userId, _connections[key].Count);
    }

    public void UnregisterConnection(string? tenantId, string userId, ISseClient client)
    {
        var key = GetConnectionKey(tenantId, userId);
        if (_connections.TryGetValue(key, out var clients))
        {
            lock (clients)
            {
                clients.Remove(client);
                if (clients.Count == 0)
                {
                    _connections.TryRemove(key, out _);
                }
            }

            _logger.LogInformation("Unregistered SSE connection for tenant: {TenantId}, user: {UserId}. Remaining connections: {Count}",
                tenantId ?? "null", userId, clients.Count);
        }
    }

    public async Task BroadcastUnreadCountAsync(string? tenantId, string userId, int unreadCount, CancellationToken cancellationToken = default)
    {
        var key = GetConnectionKey(tenantId, userId);
        if (!_connections.TryGetValue(key, out var clients))
        {
            return;
        }

        var data = new { unreadCount };
        var tasks = new List<Task>();

        lock (clients)
        {
            foreach (var client in clients.ToList())
            {
                if (client.IsConnected)
                {
                    tasks.Add(client.SendAsync("unread_count", data, cancellationToken));
                }
                else
                {
                    clients.Remove(client);
                }
            }
        }

        await Task.WhenAll(tasks);
    }

    public async Task BroadcastNotificationCreatedAsync(string? tenantId, string userId, NotificationListItemDto notification, CancellationToken cancellationToken = default)
    {
        var key = GetConnectionKey(tenantId, userId);
        if (!_connections.TryGetValue(key, out var clients))
        {
            return;
        }

        var tasks = new List<Task>();

        lock (clients)
        {
            foreach (var client in clients.ToList())
            {
                if (client.IsConnected)
                {
                    tasks.Add(client.SendAsync("notification_created", notification, cancellationToken));
                }
                else
                {
                    clients.Remove(client);
                }
            }
        }

        await Task.WhenAll(tasks);
    }

    private static string GetConnectionKey(string? tenantId, string userId)
    {
        return $"{tenantId ?? "null"}:{userId}";
    }
}
