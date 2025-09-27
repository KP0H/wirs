using StackExchange.Redis;

namespace WebhookInbox.Api.Idempotency;

public sealed class RedisIdempotencyStore(IConnectionMultiplexer mux) : IIdempotencyStore
{
    private readonly IDatabase _db = mux.GetDatabase();

    public async Task<(bool Created, Guid EventId)> TryReserveAsync(string key, Guid newEventId, TimeSpan ttl, CancellationToken ct)
    {
        // value = eventId string
        var value = newEventId.ToString("D");

        // SET key value NX EX=ttl
        var created = await _db.StringSetAsync(key, value, ttl, When.NotExists);
        if (created) return (true, newEventId);

        var existing = await _db.StringGetAsync(key);
        if (existing.HasValue && Guid.TryParse(existing!, out var existingId))
            return (false, existingId);

        // edge: key exists but not parseable → overwrite to safe value (rare)
        await _db.StringSetAsync(key, value, ttl);
        return (true, newEventId);
    }
}
