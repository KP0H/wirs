using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebhookInbox.Worker;

public sealed class DeliveryWorker(
    IDeliveryProcessor processor,
    IOptions<DeliveryOptions> options,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        logger.LogInformation("DeliveryWorker started. PollInterval={Poll}s, BatchSize={Batch}",
            options.Value.PollIntervalSeconds, options.Value.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await processor.ProcessOnceAsync(stoppingToken);
                if (count == 0)
                {
                    await Task.Delay(poll, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in DeliveryWorker loop");
                await Task.Delay(poll, stoppingToken);
            }
        }
    }
}
