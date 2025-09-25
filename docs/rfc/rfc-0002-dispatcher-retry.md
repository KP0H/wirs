---
RFC: 0002
Title: Dispatcher & Retry Policy
Status: Draft
Owners: Dmitrii [KP0H™] Pelevin
Created: 2025-09-25
---

## Scope
- Scheduling of attempts (NextAttemptAt).
- Exponential backoff with jitter (Polly).
- Per-endpoint rate limiting.
- DLQ criteria.

## Backoff
1m → 5m → 15m → 1h → 6h → DLQ.

For compatibility with SQLite-based tests, interim ordering in the worker uses Id. In retry milestone we will schedule by NextAttemptAt (UTC) and order accordingly.

## Metrics
- deliveries_total{status}
- retry_scheduled_total
- delivery_duration_ms

## Exit Criteria (M2)
- Failed endpoint causes ≥2 retries with backoff.
- DLQ reached after max attempts.
