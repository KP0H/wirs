using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Built-in health checks (readiness/liveness)
// TODO: Db/Redis checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("OK"));

var app = builder.Build();

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

app.Run();

public partial class Program { }