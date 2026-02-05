namespace BellNotification.Domain.Entities;

public class NotificationStatus
{
    public Guid NotificationId { get; set; }
    public string? TenantId { get; set; }
    public string UserId { get; set; } = default!;
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }

    // Navigation property
    public BellNotification Notification { get; set; } = default!;
}
