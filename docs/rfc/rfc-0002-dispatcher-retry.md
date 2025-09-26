---
RFC: 0002
Title: Dispatcher & Retry Policy
Status: Active
Owners: Dmitrii [KP0H™] Pelevin
Created: 2025-09-25
---

## Scope
- Inline retries via **.NET Resilience** (using `AddResilienceHandler` and a jittered `DelayGenerator`).
- Scheduled retries via `NextAttemptAt` using the `BackoffSeconds` list.
- Dead-letter queue (DLQ) when `MaxAttempts` is reached or the backoff sequence is exhausted.
- Per-endpoint rate limiting.

### Retry & Backoff
- Inline retries: `Delivery:InlineRetryCount` (default: 2, jittered).  
- Scheduled backoff (between cycles): `Delivery:BackoffSeconds` (default: `[60,300,900,3600,21600]`).  
- Max attempts (per Event+Endpoint): `Delivery:MaxAttempts` (default: 6).  
- On exhaustion, the event is marked as **DeadLetter**.

## Trade-offs
For compatibility with SQLite-based tests, interim ordering in the worker uses `Id`.  
In the retry milestone we will schedule by `NextAttemptAt` (UTC) and order accordingly.

## Metrics
- `deliveries_total{status="success|failed|deadletter"}`
- `retry_scheduled_total`
- `delivery_duration_ms`

## Exit Criteria (M2)
- Inline retries triggered for transient errors (≥2 attempts with jitter).  
- Scheduled retries respect `BackoffSeconds`.  
- DLQ is reached after `MaxAttempts`.
