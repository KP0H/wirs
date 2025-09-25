using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
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
        // Выбираем «свежие» события без NextAttemptAt (ретраи будут в след. issue)
        var events = await _db.Events
            .Where(e => e.Status == EventStatus.New)
            .OrderBy(e => e.Id)
            .Take(_opts.BatchSize)
            .ToListAsync(ct);

        if (events.Count == 0) return 0;

        // Берём активные endpoints
        var endpoints = await _db.Endpoints.Where(x => x.IsActive).ToListAsync(ct);
        if (endpoints.Count == 0) return 0;

        var client = _http.CreateClient("delivery");
        var processed = 0;

        foreach (var ev in events)
        {
            foreach (var ep in endpoints)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, ep.Url)
                    {
                        Content = new ByteArrayContent(ev.Payload ?? [])
                    };

                    // прокидываем заголовки по минимуму (опционально)
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    var resp = await client.SendAsync(req, ct);
                    var respBody = await resp.Content.ReadAsStringAsync(ct);

                    var attempt = new DeliveryAttempt
                    {
                        Id = Guid.NewGuid(),
                        EventId = ev.Id,
                        EndpointId = ep.Id,
                        Try = await _db.DeliveryAttempts.CountAsync(a => a.EventId == ev.Id && a.EndpointId == ep.Id, ct) + 1,
                        SentAt = _clock.UtcNow,
                        ResponseCode = (int)resp.StatusCode,
                        ResponseBody = Truncate(respBody, 10_000),
                        Success = resp.IsSuccessStatusCode,
                        NextAttemptAt = null // будет в retry-issue
                    };
                    _db.DeliveryAttempts.Add(attempt);

                    // Простая логика статуса события (ретраи — позже)
                    if (resp.IsSuccessStatusCode)
                    {
                        ev.Status = EventStatus.Dispatched;
                    }
                    else
                    {
                        ev.Status = EventStatus.Failed;
                    }

                    await _db.SaveChangesAsync(ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Delivery failed for Event {EventId} -> Endpoint {Endpoint}", ev.Id, ep.Url);

                    _db.DeliveryAttempts.Add(new DeliveryAttempt
                    {
                        Id = Guid.NewGuid(),
                        EventId = ev.Id,
                        EndpointId = ep.Id,
                        Try = await _db.DeliveryAttempts.CountAsync(a => a.EventId == ev.Id && a.EndpointId == ep.Id, ct) + 1,
                        SentAt = _clock.UtcNow,
                        ResponseCode = null,
                        ResponseBody = $"exception:{ex.GetType().Name}",
                        Success = false,
                        NextAttemptAt = null
                    });
                    ev.Status = EventStatus.Failed;
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        return processed;
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
