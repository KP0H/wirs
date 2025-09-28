using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Text.Json;
using System.Threading.RateLimiting;
using WebhookInbox.Api.Idempotency;
using WebhookInbox.Api.RateLimiting;
using WebhookInbox.Api.Signatures;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<IdempotencyOptions>(builder.Configuration.GetSection("Idempotency"));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));
builder.Services.Configure<SignatureOptions>(builder.Configuration.GetSection("Signatures"));
builder.Services.AddSingleton<ISignatureValidator, SignatureValidator>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Redis (ConnectionMultiplexer)
var redisConn = builder.Configuration.GetValue<string>("REDIS__CONNECTION") ?? builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
    builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
}
else
{
    // fallback local without Redis (non production)
    builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
}

// ---- Services ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- Limiter ----
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Retry-After + log
    opt.OnRejected = async (ctx, token) =>
    {
        WebhookInbox.Api.Observability.Metrics.RateLimitBlocked.Add(1,
            new KeyValuePair<string, object?>("source", (ctx.HttpContext.Request.RouteValues["source"]?.ToString() ?? "unknown")));

        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "rate_limited" }, cancellationToken: token);
    };

    opt.AddPolicy("inbox-per-source", httpContext =>
    {
        var cfg = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var source = (httpContext.Request.RouteValues.TryGetValue("source", out var v) ? v?.ToString() : null) ?? "default";

        var rpm = cfg.Sources.FirstOrDefault(s => string.Equals(s.Source, source, StringComparison.OrdinalIgnoreCase))?.RequestsPerMinute
                  ?? cfg.DefaultRequestsPerMinute;

        var permits = Math.Max(rpm, 0);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"inbox:{source}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// Built-in health checks (readiness/liveness)
// TODO: Db/Redis checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("OK"));



// OpenTelemetry Metrics + Prometheus exporter
builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb.AddService(serviceName: "WebhookInbox.Api", serviceVersion: "1.0.0"))
    .WithMetrics(mb =>
    {
        mb.AddAspNetCoreInstrumentation();
        mb.AddHttpClientInstrumentation();
        mb.AddRuntimeInstrumentation();
        mb.AddMeter("Microsoft.EntityFrameworkCore");
        mb.AddMeter("WebhookInbox");
        mb.AddPrometheusExporter();
    })
    .WithTracing(tb =>
    {
        tb.AddAspNetCoreInstrumentation();
        tb.AddHttpClientInstrumentation();
        tb.AddEntityFrameworkCoreInstrumentation();
    });

var app = builder.Build();

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// ---- Middleware ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WebhookInbox.Api v1");
        c.RoutePrefix = "swagger";
    });
}


app.UseRateLimiter();

// ---- Minimal endpoints ----
// metrics
app.MapPrometheusScrapingEndpoint("/metrics");

// root ping
app.MapGet("/", () => Results.Ok(new { service = "WebhookInbox.Api", status = "ok" }))
   .WithName("RootPing");

// Liveness
app.MapGet("/healthz", () => Results.Ok(new { status = "live" }))
   .WithName("Liveness");

// Readiness
app.MapHealthChecks("/ready")
   .WithName("Readiness");

// Version API example
app.MapGet("/api/version", () => Results.Ok(new { version = "v0.1.0", framework = "net9.0" }))
   .WithName("ApiVersion")
   .WithOpenApi();

// POST /api/inbox/{source}
// Reads raw body + headers, persists Event, returns 202 Accepted with eventId
app.MapPost("/api/inbox/{source}", async (
    string source,
    HttpRequest request,
    AppDbContext db,
    IIdempotencyStore idem,
    IOptions<IdempotencyOptions> idemOpts,
    ISignatureValidator sigValidator,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(source))
        return Results.BadRequest(new { error = "source is required" });

    // Read raw body as bytes
    byte[] payload;
    await using (var ms = new MemoryStream())
    {
        await request.Body.CopyToAsync(ms, ct);
        payload = ms.ToArray();
    }

    // Signature validation (if configured/required)
    var (ok, reason) = await sigValidator.ValidateAsync(source, request, payload, ct);
    if (!ok)
    {
        WebhookInbox.Api.Observability.Metrics.SignatureValidationFailures.Add(1);
        return Results.Unauthorized();
    }

    // Resolve idempotency key
    var rawKey = IdempotencyKeyResolver.Resolve(request, payload);
    var namespacedKey = IdempotencyKeyResolver.Namespaced(source, rawKey);
    var ttl = TimeSpan.FromSeconds(idemOpts.Value.KeyTtlSeconds);

    // Try reserve key
    var tentativeId = Guid.NewGuid();
    var (created, eventId) = await idem.TryReserveAsync(namespacedKey, tentativeId, ttl, ct);

    if (!created)
    {
        WebhookInbox.Api.Observability.Metrics.IdempotentHits.Add(1);
        WebhookInbox.Api.Observability.Metrics.EventsTotal.Add(1,
            new KeyValuePair<string, object?>("status", "duplicate"));
        // Duplicate – return existing eventId, no DB write
        return Results.Ok(new { eventId, duplicate = true });
    }

    // Build headers JSON
    var headersDict = new Dictionary<string, string?[]>(StringComparer.OrdinalIgnoreCase);
    foreach (var h in request.Headers) headersDict[h.Key] = [.. h.Value];

    // Serialize headers to a JsonDocument (maps to jsonb)
    var headersJson = JsonSerializer.SerializeToDocument(headersDict, new JsonSerializerOptions
    {
        WriteIndented = false
    });

    var entity = new Event
    {
        Id = eventId,
        Source = source,
        ReceivedAt = DateTimeOffset.UtcNow,
        Headers = headersJson,
        Payload = payload,
        SignatureStatus = SignatureStatus.Verified,
        Status = EventStatus.New
    };

    db.Events.Add(entity);
    await db.SaveChangesAsync(ct);
    WebhookInbox.Api.Observability.Metrics.EventsTotal.Add(1, new KeyValuePair<string, object?>("status", "ingested"));

    // Return 202 with Location and body { eventId }
    var location = $"/api/events/{entity.Id}";
    return Results.Accepted(location, new { eventId = entity.Id, duplicate = false });
})
.RequireRateLimiting("inbox-per-source")
.WithName("InboxIngestion")
.Produces(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest)
.WithOpenApi();

app.Run();

public partial class Program { }


