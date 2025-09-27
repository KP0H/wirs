namespace WebhookInbox.Api.Idempotency;

public class IdempotencyOptions
{
    public int KeyTtlSeconds { get; set; } = 24 * 60 * 60; // 24h
}
