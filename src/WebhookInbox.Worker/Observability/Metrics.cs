using System.Diagnostics.Metrics;

namespace WebhookInbox.Worker.Observability;

public static class Metrics
{
    public static readonly Meter Meter = new("WebhookInbox", "1.0.0");

    public static readonly Counter<long> DeliveriesTotal =
        Meter.CreateCounter<long>("webhookinbox_deliveries_total", unit: "{deliveries}",
            description: "Number of delivery attempts by worker");

    public static readonly Histogram<double> DeliveryDurationMs =
        Meter.CreateHistogram<double>("webhookinbox_delivery_duration_ms", unit: "ms",
            description: "Duration of delivery HTTP requests");
}
