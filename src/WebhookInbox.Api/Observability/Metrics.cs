using System.Diagnostics.Metrics;

namespace WebhookInbox.Api.Observability;

public static class Metrics
{
    public static readonly Meter Meter = new("WebhookInbox", "1.0.0");

    public static readonly Counter<long> EventsTotal =
        Meter.CreateCounter<long>("webhookinbox_events_total", unit: "{events}",
            description: "Number of events received by API");

    public static readonly Counter<long> SignatureValidationFailures =
        Meter.CreateCounter<long>("webhookinbox_signature_validation_failures_total", unit: "{errors}",
            description: "Number of signature validation failures");

    public static readonly Counter<long> IdempotentHits =
        Meter.CreateCounter<long>("webhookinbox_idempotent_hits_total", unit: "{hits}",
            description: "Number of idempotency dedup hits");

    public static readonly Counter<long> RateLimitBlocked =
        Meter.CreateCounter<long>("webhookinbox_rate_limit_blocked_total", unit: "{blocks}",
            description: "Requests blocked by API rate limiter");
}
