using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using WebhookInbox.LoadTest;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LoadTestOptions>(builder.Configuration.GetSection("LoadTest"));
builder.Services.AddSingleton<LoadTestMetrics>();
builder.Services.AddSingleton<LoadTestStatus>();
builder.Services.AddHostedService<LoadTestWorker>();

builder.Services.AddHttpClient("loadtest", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptionsMonitor<LoadTestOptions>>().CurrentValue;
    if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
    {
        client.BaseAddress = baseUri;
    }

    var timeoutSeconds = Math.Clamp(options.HttpTimeoutSeconds, 1, 300);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("WebhookInbox.LoadTest/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.All,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb.AddService("WebhookInbox.LoadTest", serviceVersion: "1.0.0"))
    .WithMetrics(mb =>
    {
        mb.AddHttpClientInstrumentation();
        mb.AddRuntimeInstrumentation();
        mb.AddMeter(LoadTestMetrics.MeterName);
        mb.AddPrometheusExporter();
    });

var app = builder.Build();

app.MapGet("/", (LoadTestStatus status) => Results.Ok(status.Snapshot()));
app.MapGet("/status", (LoadTestStatus status) => Results.Ok(status.Snapshot()));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapPrometheusScrapingEndpoint();

await app.RunAsync();
