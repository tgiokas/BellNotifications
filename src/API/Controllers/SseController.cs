using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using BellNotification.Application.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;

namespace BellNotification.API.Controllers;

[ApiController]
[Route("notifications")]
public class SseController : ControllerBase
{
    private readonly ISseConnectionManager _connectionManager;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SseController> _logger;

    public SseController(
        ISseConnectionManager connectionManager,
        INotificationService notificationService,
        ILogger<SseController> logger)
    {
        _connectionManager = connectionManager;
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpGet("stream")]
    public async Task Stream([FromQuery] string streamToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(streamToken))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("stream_token query parameter is required", cancellationToken);
            return;
        }

        // Validate stream token
        var (tenantId, userId) = ValidateStreamToken(streamToken);
        if (userId == null)
        {
            Response.StatusCode = 401;
            await Response.WriteAsync("Invalid stream token", cancellationToken);
            return;
        }

        // Set up SSE response
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.Body.FlushAsync(cancellationToken);

        // Create SSE client
        var sseClient = new SseClient(Response, _logger);
        _connectionManager.RegisterConnection(tenantId, userId, sseClient);

        try
        {
            // Send initial unread count
            var unreadCount = await _notificationService.GetUnreadCountAsync(tenantId, userId, cancellationToken);
            await sseClient.SendAsync("unread_count", new { unreadCount.UnreadCount }, cancellationToken);

            // Send keepalive pings every 25 seconds
            var keepaliveTask = KeepaliveLoop(sseClient, cancellationToken);

            // Wait for client disconnect
            await WaitForDisconnect(cancellationToken);

            await keepaliveTask;
        }
        catch (OperationCanceledException)
        {
            // Client disconnected, normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE stream for user {UserId}", userId);
        }
        finally
        {
            _connectionManager.UnregisterConnection(tenantId, userId, sseClient);
        }
    }

    private (string? TenantId, string? UserId) ValidateStreamToken(string token)
    {
        try
        {
            var signingKey = Environment.GetEnvironmentVariable("JWT_STREAM_TOKEN_SIGNING_KEY")
                ?? Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
                ?? throw new InvalidOperationException("Stream token signing key not configured");

            var key = Encoding.UTF8.GetBytes(signingKey);
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            var userId = principal.FindFirst("sub")?.Value ?? principal.FindFirst("user_id")?.Value;
            var tenantId = principal.FindFirst("tenant_id")?.Value;
            var purpose = principal.FindFirst("purpose")?.Value;

            if (purpose != "sse")
            {
                _logger.LogWarning("Stream token missing or invalid purpose claim");
                return (null, null);
            }

            return (tenantId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate stream token");
            return (null, null);
        }
    }

    private async Task KeepaliveLoop(SseClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && client.IsConnected)
        {
            await Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
            if (!cancellationToken.IsCancellationRequested && client.IsConnected)
            {
                await client.SendAsync("ping", new { timestamp = DateTime.UtcNow }, cancellationToken);
            }
        }
    }

    private async Task WaitForDisconnect(CancellationToken cancellationToken)
    {
        try
        {
            // Wait for cancellation (client disconnect)
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when client disconnects
        }
    }
}

// SSE client implementation
public class SseClient : ISseClient
{
    private readonly HttpResponse _response;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public SseClient(HttpResponse response, ILogger logger)
    {
        _response = response;
        _logger = logger;
    }

    public bool IsConnected => !_response.HttpContext.RequestAborted.IsCancellationRequested;

    public async Task SendAsync(string eventType, object data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await _response.WriteAsync($"event: {eventType}\n", cancellationToken);
            await _response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await _response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SSE event: {EventType}", eventType);
        }
    }
}
