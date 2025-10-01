namespace WebhookInbox.LoadTest;

public sealed class LoadTestStatus
{
    private readonly object _sync = new();
    private string _phase = "initializing";
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastActivity;
    private LoadTestScenarioKind? _lastScenario;
    private string? _lastError;

    public void TransitionTo(string phase)
    {
        lock (_sync)
        {
            _phase = phase;
            if (phase == "running" && _startedAt is null)
            {
                _startedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public void MarkRequest(LoadTestScenarioKind scenario)
    {
        lock (_sync)
        {
            _lastActivity = DateTimeOffset.UtcNow;
            _lastScenario = scenario;
        }
    }

    public void RecordError(string message)
    {
        lock (_sync)
        {
            _lastError = message;
            _lastActivity = DateTimeOffset.UtcNow;
        }
    }

    public LoadTestStatusSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new LoadTestStatusSnapshot(
                Phase: _phase,
                StartedAtUtc: _startedAt,
                LastActivityUtc: _lastActivity,
                LastScenario: _lastScenario?.ToString(),
                LastError: _lastError);
        }
    }
}

public sealed record LoadTestStatusSnapshot(
    string Phase,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastActivityUtc,
    string? LastScenario,
    string? LastError);
