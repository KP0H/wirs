namespace WebhookInbox.Api.RateLimiting;

public class RateLimitOptions
{
    /// Default RPM
    public int DefaultRequestsPerMinute { get; set; } = 60;

    /// Override by source source (github, stripe, test etc.)
    public List<SourceRateLimit> Sources { get; set; } = [];
}
