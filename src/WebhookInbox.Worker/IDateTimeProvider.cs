namespace WebhookInbox.Worker;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
