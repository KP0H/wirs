namespace WebhookInbox.Api.Idempotency;

public interface IIdempotencyStore
{
    /// <summary>
    /// Try to reserve a key with eventId. 
    /// Returns existing eventId if key is already present; otherwise stores the provided eventId with TTL.
    /// </summary>
    Task<(bool Created, Guid EventId)> TryReserveAsync(string key, Guid newEventId, TimeSpan ttl, CancellationToken ct);
}
