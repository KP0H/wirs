using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebhookInbox.LoadTest;

public sealed class LoadTestWorker(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<LoadTestOptions> optionsMonitor,
    LoadTestMetrics metrics,
    LoadTestStatus status,
    ILogger<LoadTestWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptionsMonitor<LoadTestOptions> _optionsMonitor = optionsMonitor;
    private readonly LoadTestMetrics _metrics = metrics;
    private readonly LoadTestStatus _status = status;
    private readonly ILogger<LoadTestWorker> _logger = logger;
    private readonly string[] _replayKeys =
        ["demo-alpha", "demo-beta", "demo-gamma", "demo-delta", "demo-epsilon"];
    private readonly ConcurrentDictionary<string, string> _replayPayloadCache = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var runtime = BuildRuntime(options);

        if (runtime.Sources.Count == 0)
        {
            _status.TransitionTo("disabled");
            _logger.LogWarning("Load test not started: no source profiles configured.");
            return;
        }

        if (runtime.RequestsPerSecond <= 0)
        {
            _status.TransitionTo("disabled");
            _logger.LogWarning("Load test not started: RequestsPerSecond <= 0 (value: {RequestsPerSecond}).", runtime.RequestsPerSecond);
            return;
        }

        _logger.LogInformation(
            "Starting load test with {Concurrency} workers at ~{Rps:F2} RPS targeting {Sources} sources.",
            runtime.Concurrency,
            runtime.RequestsPerSecond,
            runtime.Sources.Count);

        _status.TransitionTo("waiting");

        if (runtime.StartupDelay > TimeSpan.Zero)
        {
            _logger.LogInformation("Delaying load test start for {Delay}s.", runtime.StartupDelay.TotalSeconds);
            try
            {
                await Task.Delay(runtime.StartupDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        CancellationTokenSource? durationCts = null;
        var duration = runtime.Duration;
        if (duration.HasValue && duration.Value > TimeSpan.Zero)
        {
            durationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            durationCts.CancelAfter(duration.Value);
        }

        var effectiveToken = durationCts?.Token ?? stoppingToken;
        var workerTasks = new List<Task>(runtime.Concurrency);
        _status.TransitionTo("running");

        for (var i = 0; i < runtime.Concurrency; i++)
        {
            workerTasks.Add(RunWorkerAsync(i + 1, runtime, effectiveToken));
        }

        try
        {
            await Task.WhenAll(workerTasks);
        }
        catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
        {
            // cancellation expected
        }
        finally
        {
            durationCts?.Dispose();
            _status.TransitionTo(effectiveToken.IsCancellationRequested ? "stopped" : "completed");
        }
    }

    private async Task RunWorkerAsync(int workerId, RuntimeConfiguration runtime, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("loadtest");
        if (client.BaseAddress is null && Uri.TryCreate(runtime.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            client.BaseAddress = baseUri;
        }

        var interval = runtime.IntervalPerWorker;
        var timer = interval > TimeSpan.Zero ? new PeriodicTimer(interval) : null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var source = runtime.PickSource();
                var scenario = NormalizeScenario(source, runtime.ChooseScenario(source));

                _metrics.IncrementInflight();
                var sw = Stopwatch.StartNew();

                try
                {
                    var result = await ExecuteScenarioAsync(client, source, scenario, cancellationToken);
                    sw.Stop();

                    var outcome = result.IsSuccess ? "success" : "failure";
                    _metrics.RecordResult(source.Source, scenario, outcome, (int)result.StatusCode, sw.Elapsed.TotalMilliseconds);

                    if (!result.IsSuccess && runtime.EnableLogging)
                    {
                        _logger.LogWarning(
                            "Scenario {Scenario} for source {Source} returned HTTP {StatusCode}.",
                            scenario,
                            source.Source,
                            (int)result.StatusCode);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    sw.Stop();
                    _metrics.RecordResult(source.Source, scenario, "http_error", null, null);
                    _status.RecordError($"http_error:{ex.GetType().Name}");

                    if (runtime.EnableLogging)
                    {
                        _logger.LogWarning(ex, "HTTP error running scenario {Scenario} for {Source}.", scenario, source.Source);
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _metrics.RecordResult(source.Source, scenario, "error", null, null);
                    _status.RecordError($"exception:{ex.GetType().Name}");
                    _logger.LogError(ex, "Unexpected error in scenario {Scenario} for {Source}.", scenario, source.Source);
                }
                finally
                {
                    _metrics.DecrementInflight();
                    _status.MarkRequest(scenario);
                }

                if (timer is null)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                else
                {
                    try
                    {
                        if (!await timer.WaitForNextTickAsync(cancellationToken))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            timer?.Dispose();
            _logger.LogInformation("Load test worker {WorkerId} exiting.", workerId);
        }
    }

    private RuntimeConfiguration BuildRuntime(LoadTestOptions options)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://api:8080" : options.BaseUrl;
        var concurrency = Math.Max(1, options.Concurrency);
        var rps = options.RequestsPerSecond <= 0 ? 0 : options.RequestsPerSecond;

        var sources = options.Sources
            .Select(BuildSource)
            .Where(static s => s is not null)
            .Cast<RuntimeSource>()
            .ToList();

        return new RuntimeConfiguration(
            BaseUrl: baseUrl,
            Concurrency: concurrency,
            RequestsPerSecond: rps,
            EnableLogging: options.EnableLogging,
            StartupDelay: options.StartupDelay,
            Duration: options.Duration,
            Sources: sources);
    }

    private RuntimeSource? BuildSource(LoadTestSourceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Source))
        {
            return null;
        }

        var provider = string.IsNullOrWhiteSpace(options.Provider)
            ? "github"
            : options.Provider.Trim();

        var secret = !string.IsNullOrWhiteSpace(options.Secret)
            ? options.Secret
            : (!string.IsNullOrWhiteSpace(options.SecretEnvVar)
                ? Environment.GetEnvironmentVariable(options.SecretEnvVar)
                : null);

        var canSign = !string.IsNullOrWhiteSpace(secret);
        var weights = ScenarioDistribution.Create(options, provider);

        if (!canSign)
        {
            _logger.LogWarning("Source {Source} has no signing secret. Signature scenarios will downgrade to missing signature.", options.Source);
        }

        return new RuntimeSource(
            Source: options.Source.Trim(),
            Provider: provider,
            Secret: secret,
            CanSign: canSign,
            ToleranceSeconds: options.ToleranceSeconds,
            Weights: weights);
    }

    private async Task<ScenarioExecutionResult> ExecuteScenarioAsync(
        HttpClient client,
        RuntimeSource source,
        LoadTestScenarioKind scenario,
        CancellationToken cancellationToken)
    {
        var requestData = BuildRequest(source, scenario);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inbox/{source.Source}");
        request.Content = new ByteArrayContent(requestData.PayloadBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(requestData.ContentType);

        if (!string.IsNullOrWhiteSpace(requestData.IdempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", requestData.IdempotencyKey);
        }

        foreach (var (key, value) in requestData.Headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new ScenarioExecutionResult(response.StatusCode, response.IsSuccessStatusCode);
    }

    private RequestData BuildRequest(RuntimeSource source, LoadTestScenarioKind scenario)
    {
        var payloadText = BuildPayload(source, scenario, out var contentType, out var idempotencyKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadText);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Accept", "application/json")
        };

        switch (source.Provider.ToLowerInvariant())
        {
            case "github":
                headers.Add(new("X-GitHub-Event", "push"));
                var deliveryId = idempotencyKey is not null && scenario == LoadTestScenarioKind.Replay
                    ? idempotencyKey
                    : Guid.NewGuid().ToString();
                headers.Add(new("X-GitHub-Delivery", deliveryId));

                if (scenario != LoadTestScenarioKind.MissingSignature && source.CanSign)
                {
                    var signatureSecret = scenario == LoadTestScenarioKind.InvalidSignature
                        ? source.Secret + "-invalid"
                        : source.Secret;

                    var signature = ComputeGitHubSignature(signatureSecret!, payloadBytes);
                    headers.Add(new("X-Hub-Signature-256", signature));
                }
                break;

            case "stripe":
                var timestamp = scenario == LoadTestScenarioKind.ExpiredSignature
                    ? DateTimeOffset.UtcNow.AddSeconds(-(source.ToleranceSeconds + 120)).ToUnixTimeSeconds()
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (scenario != LoadTestScenarioKind.MissingSignature && source.CanSign)
                {
                    var signatureSecret = scenario == LoadTestScenarioKind.InvalidSignature
                        ? source.Secret + "-invalid"
                        : source.Secret;

                    var signature = ComputeStripeSignature(signatureSecret!, timestamp, payloadText);
                    headers.Add(new("Stripe-Signature", $"t={timestamp},v1={signature}"));
                }

                headers.Add(new("Stripe-Webhook-Id", idempotencyKey ?? $"evt_{Guid.NewGuid():N}"));
                break;

            default:
                if (scenario != LoadTestScenarioKind.MissingSignature && source.CanSign)
                {
                    var signature = ComputeGitHubSignature(source.Secret!, payloadBytes);
                    headers.Add(new("X-Hub-Signature-256", signature));
                }
                break;
        }

        return new RequestData(payloadText, payloadBytes, contentType, idempotencyKey, headers);
    }

    private string BuildPayload(RuntimeSource source, LoadTestScenarioKind scenario, out string contentType, out string? idempotencyKey)
    {
        contentType = "application/json";
        idempotencyKey = null;

        if (scenario == LoadTestScenarioKind.InvalidPayload)
        {
            return "{\"invalid\":true"; // intentionally malformed JSON
        }

        if (scenario == LoadTestScenarioKind.Replay)
        {
            var key = $"{source.Source}:{_replayKeys[Random.Shared.Next(_replayKeys.Length)]}";
            idempotencyKey = key;
            return _replayPayloadCache.GetOrAdd(key, _ => BuildValidPayload(source, key));
        }

        if (scenario == LoadTestScenarioKind.Valid)
        {
            idempotencyKey = $"{source.Source}:{Guid.NewGuid():N}";
            return BuildValidPayload(source, idempotencyKey);
        }

        // default for other scenarios (invalid signature, missing signature, expired)
        return BuildValidPayload(source, Guid.NewGuid().ToString());
    }

    private string BuildValidPayload(RuntimeSource source, string correlationId)
    {
        var now = DateTimeOffset.UtcNow;
        return source.Provider.ToLowerInvariant() switch
        {
            "stripe" => JsonSerializer.Serialize(new
            {
                id = $"evt_{Guid.NewGuid():N}",
                api_version = "2022-11-15",
                created = now.ToUnixTimeSeconds(),
                type = "invoice.payment_succeeded",
                livemode = false,
                data = new
                {
                    @object = new
                    {
                        id = $"in_{Guid.NewGuid():N}",
                        @object = "invoice",
                        currency = "usd",
                        amount_due = 4200,
                        status = "paid",
                        metadata = new
                        {
                            correlation = correlationId,
                            source = source.Source
                        }
                    }
                }
            }, JsonOptions),
            _ => JsonSerializer.Serialize(new
            {
                _ref = "refs/heads/main",
                before = Guid.NewGuid().ToString("N"),
                after = Guid.NewGuid().ToString("N"),
                repository = new
                {
                    id = Random.Shared.NextInt64(1, long.MaxValue),
                    name = "webhook-inbox-demo",
                    full_name = "demo/webhook-inbox-demo"
                },
                head_commit = new
                {
                    id = Guid.NewGuid().ToString("N"),
                    message = "Load test commit",
                    timestamp = now.ToString("O", CultureInfo.InvariantCulture),
                    author = new { name = "Webhook Load Tester", email = "loadtest@example.com" }
                },
                delivery = correlationId
            }, JsonOptions)
        };
    }

    private static LoadTestScenarioKind NormalizeScenario(RuntimeSource source, LoadTestScenarioKind scenario)
    {
        if (!source.CanSign && scenario is LoadTestScenarioKind.Valid or LoadTestScenarioKind.InvalidSignature or LoadTestScenarioKind.ExpiredSignature)
        {
            return LoadTestScenarioKind.MissingSignature;
        }

        if (!string.Equals(source.Provider, "stripe", StringComparison.OrdinalIgnoreCase) && scenario == LoadTestScenarioKind.ExpiredSignature)
        {
            return LoadTestScenarioKind.InvalidSignature;
        }

        return scenario;
    }

    private static string ComputeGitHubSignature(string secret, byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(payload);
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string ComputeStripeSignature(string secret, long timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var hash = hmac.ComputeHash(signedPayload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record RuntimeConfiguration(
        string BaseUrl,
        int Concurrency,
        double RequestsPerSecond,
        bool EnableLogging,
        TimeSpan StartupDelay,
        TimeSpan? Duration,
        IReadOnlyList<RuntimeSource> Sources)
    {
        public TimeSpan IntervalPerWorker { get; } = RequestsPerSecond <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Max(1, Concurrency) / RequestsPerSecond);

        public RuntimeSource PickSource()
        {
            var index = Random.Shared.Next(Sources.Count);
            return Sources[index];
        }

        public LoadTestScenarioKind ChooseScenario(RuntimeSource source)
        {
            var total = source.Weights.Total;
            var roll = Random.Shared.NextDouble() * total;
            var weights = source.Weights;

            if (roll < weights.Valid) return LoadTestScenarioKind.Valid;
            roll -= weights.Valid;
            if (roll < weights.InvalidSignature) return LoadTestScenarioKind.InvalidSignature;
            roll -= weights.InvalidSignature;
            if (roll < weights.MissingSignature) return LoadTestScenarioKind.MissingSignature;
            roll -= weights.MissingSignature;
            if (roll < weights.Replay) return LoadTestScenarioKind.Replay;
            roll -= weights.Replay;
            if (roll < weights.ExpiredSignature) return LoadTestScenarioKind.ExpiredSignature;
            return LoadTestScenarioKind.InvalidPayload;
        }
    }

    private sealed record RuntimeSource(
        string Source,
        string Provider,
        string? Secret,
        bool CanSign,
        int ToleranceSeconds,
        ScenarioDistribution Weights);

    private sealed class ScenarioDistribution
    {
        private ScenarioDistribution(double valid, double invalidSignature, double missingSignature, double replay, double expiredSignature, double invalidPayload)
        {
            Valid = valid;
            InvalidSignature = invalidSignature;
            MissingSignature = missingSignature;
            Replay = replay;
            ExpiredSignature = expiredSignature;
            InvalidPayload = invalidPayload;
            Total = valid + invalidSignature + missingSignature + replay + expiredSignature + invalidPayload;
        }

        public double Valid { get; }
        public double InvalidSignature { get; }
        public double MissingSignature { get; }
        public double Replay { get; }
        public double ExpiredSignature { get; }
        public double InvalidPayload { get; }
        public double Total { get; }

        public static ScenarioDistribution Create(LoadTestSourceOptions options, string provider)
        {
            var valid = Math.Max(0, options.ValidRatio);
            var invalid = Math.Max(0, options.InvalidSignatureRatio);
            var missing = Math.Max(0, options.MissingSignatureRatio);
            var replay = Math.Max(0, options.ReplayRatio);
            var expired = string.Equals(provider, "stripe", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0, options.ExpiredSignatureRatio)
                : 0;
            var invalidPayload = Math.Max(0, options.InvalidPayloadRatio);

            var distribution = new ScenarioDistribution(valid, invalid, missing, replay, expired, invalidPayload);
            return distribution.Total <= 0
                ? new ScenarioDistribution(1, 0, 0, 0, 0, 0)
                : distribution;
        }
    }

    private sealed record RequestData(string PayloadText, byte[] PayloadBytes, string ContentType, string? IdempotencyKey, List<KeyValuePair<string, string>> Headers);

    private sealed record ScenarioExecutionResult(HttpStatusCode StatusCode, bool IsSuccess);
}



