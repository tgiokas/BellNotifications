namespace BellNotification.Application.Dtos;

public class NotificationListResponse
{
    public List<NotificationListItemDto> Items { get; set; } = new();
    public string? NextCursor { get; set; }
}
