namespace WebhookInbox.LoadTest;

public enum LoadTestScenarioKind
{
    Valid,
    InvalidSignature,
    MissingSignature,
    Replay,
    ExpiredSignature,
    InvalidPayload
}
