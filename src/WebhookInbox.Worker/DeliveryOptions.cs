namespace WebhookInbox.Worker;

public class DeliveryOptions
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int HttpTimeoutSeconds { get; set; } = 15;

    public int InlineRetryCount { get; set; } = 2;

    public int[] BackoffSeconds { get; set; } = new[] { 60, 300, 900, 3600, 21600 };

    public int MaxAttempts { get; set; } = 6;
}
