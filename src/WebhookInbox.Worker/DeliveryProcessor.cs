using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure;

namespace WebhookInbox.Worker;

public interface IDeliveryProcessor
{
    Task<int> ProcessOnceAsync(CancellationToken ct);
}

public sealed class DeliveryProcessor(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<DeliveryOptions> options,
    IDateTimeProvider clock,
    ILogger<DeliveryProcessor> logger)
    : IDeliveryProcessor
{
    private readonly AppDbContext _db = db;
    private readonly IHttpClientFactory _http = httpClientFactory;
    private readonly DeliveryOptions _opts = options.Value;
    private readonly IDateTimeProvider _clock = clock;
    private readonly ILogger<DeliveryProcessor> _log = logger;

    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        // take candidates — new/failed events
        var candidates = await _db.Events
            .Where(e => e.Status == EventStatus.New || e.Status == EventStatus.Failed)
            .OrderBy(e => e.Id) // for SQLite tests (problem with dates)
            .Take(_opts.BatchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var endpoints = await _db.Endpoints.Where(x => x.IsActive).ToListAsync(ct);
        if (endpoints.Count == 0) return 0;

        var client = _http.CreateClient("delivery");
        var processed = 0;

        foreach (var ev in candidates)
        {
            foreach (var ep in endpoints)
            {
                // Check due for pair (Event, Endpoint)
                var stat = await GetAttemptStateAsync(ev.Id, ep.Id, ct);
                if (!stat.Due) continue;

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, ep.Url)
                    {
                        Content = new ByteArrayContent(ev.Payload ?? [])
                    };
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    var resp = await client.SendAsync(req, ct);
                    var respBody = await resp.Content.ReadAsStringAsync(ct);

                    _db.DeliveryAttempts.Add(new DeliveryAttempt
                    {
                        Id = Guid.NewGuid(),
                        EventId = ev.Id,
                        EndpointId = ep.Id,
                        Try = stat.NextTryNumber,
                        SentAt = _clock.UtcNow,
                        ResponseCode = (int)resp.StatusCode,
                        ResponseBody = Truncate(respBody, 10_000),
                        Success = resp.IsSuccessStatusCode,
                        NextAttemptAt = resp.IsSuccessStatusCode ? null
                                                                 : ComputeNextAttemptAt(stat.NextTryNumber)
                    });

                    ev.Status = resp.IsSuccessStatusCode
                        ? EventStatus.Dispatched
                        : (IsExhausted(stat.NextTryNumber) ? EventStatus.DeadLetter : EventStatus.Failed);

                    await _db.SaveChangesAsync(ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Delivery error (exception) for Event {EventId} -> {Url}", ev.Id, ep.Url);

                    _db.DeliveryAttempts.Add(new DeliveryAttempt
                    {
                        Id = Guid.NewGuid(),
                        EventId = ev.Id,
                        EndpointId = ep.Id,
                        Try = stat.NextTryNumber,
                        SentAt = _clock.UtcNow,
                        ResponseCode = null,
                        ResponseBody = $"exception:{ex.GetType().Name}",
                        Success = false,
                        NextAttemptAt = ComputeNextAttemptAt(stat.NextTryNumber)
                    });

                    ev.Status = IsExhausted(stat.NextTryNumber) ? EventStatus.DeadLetter : EventStatus.Failed;
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        return processed;
    }

    private async Task<(bool Due, int NextTryNumber)> GetAttemptStateAsync(Guid eventId, Guid endpointId, CancellationToken ct)
    {
        var last = await _db.DeliveryAttempts
            .Where(a => a.EventId == eventId && a.EndpointId == endpointId)
            .OrderByDescending(a => a.Try)
            .FirstOrDefaultAsync(ct);

        if (last == null) return (true, 1);

        if (last.Success) return (false, last.Try + 1); // already delivered for current endpoint

        // Check next attempt deadline
        if (last.NextAttemptAt.HasValue)
        {
            var due = last.NextAttemptAt.Value <= _clock.UtcNow;
            return (due, last.Try + 1);
        }

        return (true, last.Try + 1);
    }

    private DateTimeOffset? ComputeNextAttemptAt(int tryNumber)
    {
        var index = tryNumber - 1;
        if (index < 0 || index >= _opts.BackoffSeconds.Length)
            return null; // DLQ

        return _clock.UtcNow.AddSeconds(_opts.BackoffSeconds[index]);
    }

    private bool IsExhausted(int tryNumber)
    {
        // if no delay or reach limit
        var index = tryNumber - 1;
        return index >= _opts.BackoffSeconds.Length || tryNumber >= _opts.MaxAttempts;
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
