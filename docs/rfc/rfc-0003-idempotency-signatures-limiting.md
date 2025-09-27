---
RFC: 0003
Title: Idempotency, HMAC Signatures & Rate Limiting
Status: Active
Owners: Dmitrii [KP0H™] Pelevin
Created: 2025-09-26
---

## Summary
Introduce mechanisms to guarantee safe ingestion of webhook events:
- **Idempotency** to prevent duplicates.
- **HMAC signature validation** for authenticity.
- **Rate limiting** per endpoint to protect downstream services.

## Problem / Motivation
Webhook integrations face recurring problems:
- Duplicate deliveries (retries from providers).
- Forged or replayed requests.
- Overloaded destinations if a single endpoint receives too many requests.

We need to ensure safety, authenticity, and fairness at the ingestion and dispatch stages.

## Scope
- **Idempotency / Deduplication**:
  - Redis keys to detect duplicate events: `idem:{source}:{key}` → `eventId`.
  - TTL on keys (configurable, default: 24h).
  - Key resolution priority: `Idempotency-Key` → `X-Idempotency-Key` → `X-Hub-Signature-256` → SHA-256(payload).
  - Duplicates return **200 OK** with the same `{eventId, duplicate=true}` without inserting a new row.
- **HMAC Signatures**:
  - Support for GitHub-style `X-Hub-Signature-256`.
  - Support for Stripe-style `Stripe-Signature`.
  - Per-endpoint secret stored securely in DB.
  - Reject invalid or missing signatures with `401 Unauthorized`.
- **Rate Limiting**:
  - ASP.NET Core rate-limiting middleware at API layer (fixed-window).
  - Configurable per endpoint: requests per minute plus per-source overrides.
  - Worker-side enforcement: ensure dispatches don’t exceed endpoint limits.
  - API ingress: fixed window per {source} (ASP.NET Core Rate Limiter), responds with 429 + Retry-After.

## Non-Goals
- Multi-tenant key management.
- Complex OAuth2 flows.
- Pluggable signature algorithms beyond HMAC-SHA256 (future).

## Architectural Overview
- **API ingestion path**:
  - On `POST /api/inbox/{source}`: validate signature if endpoint has secret.
  - Check Redis for idempotency key → if duplicate, return existing `eventId`.
  - Otherwise persist Event and create deduplication key in Redis.
- **Worker dispatch path**:
  - For each Event+Endpoint, enforce per-endpoint rate limit before delivery.
  - If exceeded, schedule retry with backoff.

## Trade-offs
- Using Redis for idempotency is simple, but requires Redis availability.
- Rejecting unsigned events may break integrations; optional enforcement per endpoint.
- Fixed-window rate limiting is easy, but less precise than token-bucket.

## Metrics
- `idempotent_hits_total` — number of deduplicated events.
- `signature_validation_failures_total`
- `rate_limit_blocked_total`
- `rate_limit_current{endpoint}` gauge (active requests count).

## Security
- HMAC prevents forgery and replay.
- Redis-based deduplication prevents accidental duplicate ingestion.
- Rate limiting prevents downstream denial-of-service.

## Risks
- Redis outage disables idempotency checks (duplicates possible).
- Clock skew can affect signature validation (for Stripe-style signed timestamps).
- Rate-limiting misconfiguration can block legitimate traffic.

## Exit Criteria
- Duplicate ingestion requests return same `eventId` without creating a new row.
- Events with invalid HMAC signatures are rejected with `401`.
- API responds with `429 Too Many Requests` if per-endpoint limit exceeded.
- Metrics for idempotency, signature validation, and rate limiting exposed via Prometheus.
## Client Integration Guide
To reach `202 Accepted`, callers must send the payload plus a signature that matches the configured secret for the given `{source}`. Missing or invalid signatures now return `401 Unauthorized`.

### GitHub-compatible signing
1. Serialize the payload exactly as it is sent on the wire.
2. Compute `HMACSHA256(secret, payloadBytes)`.
3. Prefix the lowercase hex digest with `sha256=` and put it in `X-Hub-Signature-256`.

```bash
SECRET='<github-secret>'
BODY='{"ok":true}'
SIG="sha256=$(echo -n "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -binary | xxd -p -c 256)"
curl -X POST http://localhost:5000/api/inbox/github \
  -H "Content-Type: application/json" \
  -H "X-Hub-Signature-256: $SIG" \
  -d "$BODY"
```

Equivalent C# for reuse inside services:

```csharp
var payload = JsonSerializer.Serialize(body);
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
var sig = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
httpClient.DefaultRequestHeaders.Add("X-Hub-Signature-256", sig);
```

### Stripe-compatible signing
1. Pick a unix timestamp (`t = DateTimeOffset.UtcNow.ToUnixTimeSeconds()`).
2. Build the string `${t}.${payload}`.
3. Compute `HMACSHA256(secret, bytesOfStep2)`.
4. Send header `Stripe-Signature: t={t},v1={lowercaseHex}`.

```bash
SECRET='<stripe-secret>'
BODY='{"x":1}'
T=$(date +%s)
V1=$(printf "%s.%s" "$T" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -binary | xxd -p -c 256)
curl -X POST http://localhost:5000/api/inbox/stripe \
  -H "Content-Type: application/json" \
  -H "Stripe-Signature: t=$T,v1=$V1" \
  -d "$BODY"
```

### Local offline test client
The snippet below shows a minimal C# worker that you can run locally without any third-party services. It rotates between GitHub- and Stripe-style signatures so you can smoke-test the inbox endpoint end-to-end.

```csharp
var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
var githubSecret = Environment.GetEnvironmentVariable("GITHUB_WEBHOOK_SECRET") ?? "<env>";
var stripeSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? "<env>";

async Task SendGithubAsync(object body)
{
    var payload = JsonSerializer.Serialize(body);
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(githubSecret));
    var sig = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
    content.Headers.Add("X-Hub-Signature-256", sig);
    var response = await client.PostAsync("/api/inbox/github", content);
    Console.WriteLine(await response.Content.ReadAsStringAsync());
}

async Task SendStripeAsync(object body)
{
    var payload = JsonSerializer.Serialize(body);
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stripeSecret));
    var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
    var sig = Convert.ToHexString(hmac.ComputeHash(signedPayload)).ToLowerInvariant();

    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
    content.Headers.Add("Stripe-Signature", $"t={timestamp},v1={sig}");
    var response = await client.PostAsync("/api/inbox/stripe", content);
    Console.WriteLine(await response.Content.ReadAsStringAsync());
}
```

Run the methods above to emulate providers during development.

