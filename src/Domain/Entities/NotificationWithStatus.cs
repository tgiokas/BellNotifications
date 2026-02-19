namespace BellNotification.Domain.Entities;

public class NotificationWithStatus
{
    public Guid Id { get; set; }
    public string? TenantId { get; set; }
    public string UserId { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    public string? Link { get; set; }
    public string? Severity { get; set; }
    public string? SourceService { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
    public bool IsDismissed { get; set; }
}
