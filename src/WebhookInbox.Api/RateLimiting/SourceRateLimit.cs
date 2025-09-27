namespace WebhookInbox.Api.RateLimiting;

public class SourceRateLimit
{
    public string Source { get; set; } = default!;

    public int RequestsPerMinute { get; set; }
}
