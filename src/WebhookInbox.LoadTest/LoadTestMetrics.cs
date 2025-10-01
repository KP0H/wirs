using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Threading;

namespace WebhookInbox.LoadTest;

public sealed class LoadTestMetrics : IDisposable
{
    public const string MeterName = "WebhookInbox.LoadTest";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _latency;
    private readonly ObservableGauge<int> _inflightGauge;
    private int _inflight;

    public LoadTestMetrics()
    {
        _requests = _meter.CreateCounter<long>(
            name: "webhookinbox_loadtest_requests_total",
            unit: "{requests}",
            description: "Number of webhook test requests that were attempted");

        _latency = _meter.CreateHistogram<double>(
            name: "webhookinbox_loadtest_latency_ms",
            unit: "ms",
            description: "Latency of webhook test requests in milliseconds");

        _inflightGauge = _meter.CreateObservableGauge(
            name: "webhookinbox_loadtest_inflight",
            observeValue: () => new Measurement<int>(Volatile.Read(ref _inflight)),
            unit: "{requests}",
            description: "Number of in-flight load test requests");
    }

    public void IncrementInflight()
        => Interlocked.Increment(ref _inflight);

    public void DecrementInflight()
        => Interlocked.Decrement(ref _inflight);

    public void RecordResult(string source, LoadTestScenarioKind scenario, string outcome, int? statusCode, double? latencyMs)
    {
        var tags = new TagList
        {
            { "source", source },
            { "scenario", scenario.ToString() },
            { "outcome", outcome }
        };

        if (statusCode is int sc)
        {
            tags.Add("status", sc.ToString(CultureInfo.InvariantCulture));
        }

        _requests.Add(1, tags);

        if (latencyMs is double value)
        {
            _latency.Record(value, tags);
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
