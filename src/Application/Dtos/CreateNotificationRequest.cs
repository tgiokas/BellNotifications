namespace BellNotification.Application.Dtos;

public class CreateNotificationRequest
{
    public string? TenantId { get; set; }
    public string UserId { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    public string? Link { get; set; }
    public string? PayloadJson { get; set; }
    public string? Severity { get; set; }
    public string? SourceService { get; set; }
    public string? DedupeKey { get; set; }
}
