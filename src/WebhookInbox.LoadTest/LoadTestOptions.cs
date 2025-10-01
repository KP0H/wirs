namespace WebhookInbox.LoadTest;

public sealed class LoadTestOptions
{
    public string BaseUrl { get; set; } = "http://api:8080";
    public int StartupDelaySeconds { get; set; } = 15;
    public double RequestsPerSecond { get; set; } = 1.0;
    public int Concurrency { get; set; } = 1;
    public int DurationSeconds { get; set; }
        = 0;
    public bool EnableLogging { get; set; } = false;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public List<LoadTestSourceOptions> Sources { get; set; } = new();

    public TimeSpan StartupDelay => TimeSpan.FromSeconds(Math.Max(0, StartupDelaySeconds));
    public TimeSpan? Duration => DurationSeconds <= 0 ? null : TimeSpan.FromSeconds(DurationSeconds);
}

public sealed class LoadTestSourceOptions
{
    public string Source { get; set; } = "github";
    public string Provider { get; set; } = "github";
    public string? Secret { get; set; }
        = null;
    public string? SecretEnvVar { get; set; }
        = null;
    public int ToleranceSeconds { get; set; } = 300;
    public double ValidRatio { get; set; } = 0.6;
    public double InvalidSignatureRatio { get; set; } = 0.2;
    public double MissingSignatureRatio { get; set; } = 0.1;
    public double ReplayRatio { get; set; } = 0.05;
    public double ExpiredSignatureRatio { get; set; } = 0.05;
    public double InvalidPayloadRatio { get; set; } = 0.0;
}
