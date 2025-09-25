using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// ---- Services ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Built-in health checks (readiness/liveness)
// TODO: Db/Redis checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("OK"));

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

// ---- Minimal endpoints ----

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

    // Normalize headers to string[] for jsonb
    var headersDict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    foreach (var h in request.Headers)
        headersDict[h.Key] = h.Value.ToArray();

    // Serialize headers to a JsonDocument (maps to jsonb)
    var headersJson = JsonSerializer.SerializeToDocument(headersDict, new JsonSerializerOptions
    {
        WriteIndented = false
    });

    var entity = new Event
    {
        Id = Guid.NewGuid(),
        Source = source,
        ReceivedAt = DateTimeOffset.UtcNow,
        Headers = headersJson,
        Payload = payload,
        SignatureStatus = SignatureStatus.None,
        Status = EventStatus.New
    };

    db.Events.Add(entity);
    await db.SaveChangesAsync(ct);

    // Return 202 with Location and body { eventId }
    var location = $"/api/events/{entity.Id}";
    return Results.Accepted(location, new { eventId = entity.Id });
})
.WithName("InboxIngestion")
.Produces(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest)
.WithOpenApi();

app.Run();

public partial class Program { }