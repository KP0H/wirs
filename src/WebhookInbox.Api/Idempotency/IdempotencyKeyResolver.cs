using System.Security.Cryptography;

namespace WebhookInbox.Api.Idempotency;

public static class IdempotencyKeyResolver
{
    public static string Resolve(HttpRequest req, ReadOnlyMemory<byte> payload)
    {
        // Priority: Idempotency-Key -> X-Idempotency-Key -> X-Hub-Signature-256 -> SHA256(payload)
        if (req.Headers.TryGetValue("Idempotency-Key", out var idk) && !string.IsNullOrWhiteSpace(idk))
            return idk.ToString();

        if (req.Headers.TryGetValue("X-Idempotency-Key", out var xidk) && !string.IsNullOrWhiteSpace(xidk))
            return xidk.ToString();

        if (req.Headers.TryGetValue("X-Hub-Signature-256", out var ghSig) && !string.IsNullOrWhiteSpace(ghSig))
            return ghSig.ToString();

        // Fallback: hash of payload (hex)
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(payload.Span.ToArray());
        return Convert.ToHexString(hash); // UPPER hex ok
    }

    public static string Namespaced(string source, string key)
        => $"idem:{source}:{key}";
}