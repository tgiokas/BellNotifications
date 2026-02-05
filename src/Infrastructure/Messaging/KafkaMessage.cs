namespace BellNotification.Infrastructure.Messaging;

public class KafkaMessage<TMessage> where TMessage : class
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public TMessage? Content { get; set; } = default(TMessage);
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
