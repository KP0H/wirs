namespace WebhookInbox.Worker;

public class DeliveryOptions
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int HttpTimeoutSeconds { get; set; } = 15;
}
