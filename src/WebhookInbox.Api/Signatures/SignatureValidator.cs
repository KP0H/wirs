using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace WebhookInbox.Api.Signatures;

public sealed class SignatureValidator(IOptions<SignatureOptions> options) : ISignatureValidator
{
    private readonly SignatureOptions _opts = options.Value;

    public Task<(bool Ok, string? Reason)> ValidateAsync(string source, HttpRequest req, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var cfg = _opts.Sources.FirstOrDefault(s => string.Equals(s.Source, source, StringComparison.OrdinalIgnoreCase));
        if (cfg is null) return Result((true, "no-config"));
        if (!cfg.Require) return Result((true, "skipped"));
        if (string.IsNullOrWhiteSpace(cfg.Secret)) return Result((false, "secret-missing"));

        return cfg.Provider.ToLowerInvariant() switch
        {
            "github" => Result(ValidateGitHub(cfg.Secret!, req, payload)),
            "stripe" => Result(ValidateStripe(cfg.Secret!, cfg.ToleranceSeconds, req, payload)),
            _ => Result((false, $"unknown-provider:{cfg.Provider}"))
        };
    }

    private static (bool Ok, string? Reason) ValidateGitHub(string secret, HttpRequest req, ReadOnlyMemory<byte> payload)
    {
        if (!req.Headers.TryGetValue("X-Hub-Signature-256", out var sigHeader))
            return (false, "missing:X-Hub-Signature-256");

        var provided = sigHeader.ToString();
        const string prefix = "sha256=";
        if (!provided.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return (false, "bad-format");

        var sigHex = provided[prefix.Length..];
        if (!TryParseHex(sigHex, out var providedBytes)) return (false, "bad-hex");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(payload.Span.ToArray());
        var matches = FixedTimeEquals(computed, providedBytes);
        return (matches, matches ? null : "mismatch");
    }

    private static (bool Ok, string? Reason) ValidateStripe(string secret, int toleranceSeconds, HttpRequest req, ReadOnlyMemory<byte> payload)
    {
        if (!req.Headers.TryGetValue("Stripe-Signature", out var sigHeader))
            return (false, "missing:Stripe-Signature");

        // Format: t=timestamp,v1=hex[,v1=hex]...
        var parts = sigHeader.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? t = null;
        var v1 = new List<string>();
        foreach (var p in parts)
        {
            var kv = p.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") t = kv[1];
            if (kv[0] == "v1") v1.Add(kv[1]);
        }
        if (string.IsNullOrWhiteSpace(t) || v1.Count == 0) return (false, "bad-format");

        if (!long.TryParse(t, out var ts)) return (false, "bad-timestamp");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > toleranceSeconds) return (false, "timestamp-out-of-tolerance");

        var signedPayload = Encoding.UTF8.GetBytes($"{t}.{Encoding.UTF8.GetString(payload.Span)}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(signedPayload);
        foreach (var candidate in v1)
        {
            if (TryParseHex(candidate, out var provided) && FixedTimeEquals(computed, provided))
                return (true, null);
        }
        return (false, "mismatch");
    }

    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        if (hex.Length % 2 != 0)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static Task<(bool Ok, string? Reason)> Result((bool Ok, string? Reason) value) => Task.FromResult<(bool Ok, string? Reason)>(value);
}

