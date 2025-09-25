using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using WebhookInbox.Infrastructure;
using WebhookInbox.Worker;

var builder = Host.CreateApplicationBuilder(args);

// EF Core
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// HttpClientFactory (named)
builder.Services.AddHttpClient("delivery", c =>
{
    c.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("Delivery:HttpTimeoutSeconds", 15));
    // default headers if needed
});

// Options
builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection("Delivery"));

// Services
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddScoped<IDeliveryProcessor, DeliveryProcessor>();
builder.Services.AddHostedService<DeliveryWorker>();

var host = builder.Build();
await host.RunAsync();
