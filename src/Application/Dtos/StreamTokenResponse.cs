namespace BellNotification.Application.Dtos;

public class StreamTokenResponse
{
    public string StreamToken { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
}
