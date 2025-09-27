namespace WebhookInbox.Api.Signatures;

public interface ISignatureValidator
{
    // <summary>
    /// Validate signature for given source. Returns (ok, reason).
    /// If no config for source or Require=false => returns (true, "skipped").
    /// </summary>
    Task<(bool Ok, string? Reason)> ValidateAsync(string source, HttpRequest req, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
