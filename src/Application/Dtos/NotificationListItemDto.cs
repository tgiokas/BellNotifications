namespace BellNotification.Application.Dtos;

public class NotificationListItemDto
{
    public Guid Id { get; set; }
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
