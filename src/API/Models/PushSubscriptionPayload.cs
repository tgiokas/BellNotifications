namespace BellNotification.API.Models;

public class PushSubscriptionPayload
{
    public string UserId { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}
