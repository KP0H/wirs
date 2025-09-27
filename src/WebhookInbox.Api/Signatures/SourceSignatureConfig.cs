namespace WebhookInbox.Api.Signatures;

public class SourceSignatureConfig
{
    public string Source { get; set; } = default!;           // e.g., "github", "stripe"
    public string Provider { get; set; } = default!;         // "github" | "stripe"
    public string? Secret { get; set; }                      // from env/secret
    public bool Require { get; set; } = true;                // enforce signature
    public int ToleranceSeconds { get; set; } = 300;         // for Stripe (default 5m)
}
