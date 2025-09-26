using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System;
using WebhookInbox.Infrastructure;
using WebhookInbox.Worker;

var builder = Host.CreateApplicationBuilder(args);

// EF Core
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Options
builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection("Delivery"));

// HttpClientFactory (named)
builder.Services.AddHttpClient("delivery", c =>
{
    c.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("Delivery:HttpTimeoutSeconds", 15));
    // default headers if needed
})
.AddResilienceHandler("delivery-pipeline", (resilience, context) =>
{
    var opts = context.ServiceProvider.GetRequiredService<IOptions<DeliveryOptions>>().Value;

    // Inline retry
    resilience.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = opts.InlineRetryCount,
        BackoffType = DelayBackoffType.Exponential,
        DelayGenerator = args =>
        {
            // decorrelated jitter ~ rand * prevDelay
            var rand = Random.Shared.NextDouble();
            var delayMs = (int)(Math.Pow(2, args.AttemptNumber) * 100 * rand);
            return new ValueTask<TimeSpan?>(TimeSpan.FromMilliseconds(delayMs));
        },
        ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
        {
            { Exception: HttpRequestException } => true,
            { Exception: TaskCanceledException } => true,
            { Result.StatusCode: >= System.Net.HttpStatusCode.InternalServerError } => true,
            { Result.StatusCode: System.Net.HttpStatusCode.RequestTimeout } => true,
            { Result.StatusCode: System.Net.HttpStatusCode.TooManyRequests } => true,
            _ => false
        })
    });
});

// Services
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddScoped<IDeliveryProcessor, DeliveryProcessor>();
builder.Services.AddHostedService<DeliveryWorker>();

var host = builder.Build();
await host.RunAsync();
