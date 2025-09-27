using System.Collections.Concurrent;

namespace WebhookInbox.Api.Idempotency;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (Guid Id, DateTimeOffset ExpireAt)> _map = new();

    public Task<(bool Created, Guid EventId)> TryReserveAsync(string key, Guid newEventId, TimeSpan ttl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        _map.TryGetValue(key, out var existing);

        if (existing != default && existing.ExpireAt > now)
            return Task.FromResult((false, existing.Id));

        var value = (newEventId, now.Add(ttl));
        _map[key] = value;
        return Task.FromResult((true, newEventId));
    }
}
