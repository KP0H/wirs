---
RFC: 0004
Title: Observability & SRE
Status: Active
Owners: Dmitrii [KP0Hâ„¢] Pelevin
Created: 2025-09-28
---

## Summary
Introduce practical observability for the Webhook Inbox system with focus on service-level metrics scraped by Prometheus.  
Traces and structured logging remain on the roadmap but are not yet implemented in code.

## Problem / Motivation
Without observability:
- Failures in webhook delivery may go unnoticed.
- Root cause analysis is slow due to lack of correlation between services.
- No visibility into retry storms, DLQ growth, or high-latency endpoints.

We need metrics that highlight ingestion health, delivery success, and rate limiting behaviour, with documentation that matches the current implementation state.

## Scope
### Metrics (implemented)
- OpenTelemetry metrics wired in API and Worker using the shared `WebhookInbox` meter.
- Prometheus scraping endpoint `/metrics` exposed by the API (ASP.NET pipeline).
- Worker emits the same meter but requires an OTLP pipeline or Prometheus exporter hosted externally (no built-in HTTP listener yet).
- Counters for ingestion flow, signature failures, idempotency hits, rate limit blocks, and delivery outcomes.
- Histogram for delivery latency in the worker.
- Grafana dashboard provisioned from `docker/grafana/dashboards/webhookinbox.json`.

### Deferred / Future Work
- Prometheus endpoint for the Worker (dedicated listener or sidecar exporter).
- Distributed traces across API > Worker > outbound HTTP.
- Structured logging standardisation with Serilog.
- Grafana dashboards and alert definitions.

## Architectural Overview
- **API**: Exposes `/metrics`; instruments ASP.NET Core, HttpClient, runtime, and EF Core.
- **Worker**: Emits metrics via OpenTelemetry SDK; metrics need to be scraped via OTLP collector or future HTTP listener.
- **Prometheus**: Scrapes API metrics endpoint.
- **Shared Meter**: Both services publish to `WebhookInbox` meter for consistent naming.

## Metrics
- `webhookinbox_events_total{status="ingested|duplicate"}` — ingress volume.
- `webhookinbox_signature_validation_failures_total` — HMAC validation failures.
- `webhookinbox_idempotent_hits_total` — deduplicated requests.
- `webhookinbox_rate_limit_blocked_total{source="..."}` — requests rejected by rate limiter.
- `webhookinbox_deliveries_total{result="success|failed|deadletter"}` — worker delivery outcomes.
- `webhookinbox_delivery_duration_ms_bucket` — histogram of delivery durations.

## Security
- `/metrics` endpoint should not expose sensitive data; keep it internal-only in environments where Auth is not yet available.

## Risks
- Metrics cardinality explosion if labels are extended with per-event identifiers.
- Missing traces/logs reduce debuggability until deferred items are delivered.

## Exit Criteria
- API exposes `/metrics` with Prometheus text format and the counters listed above.
- Grafana served via Docker Compose (`docker compose up`) with Prometheus datasource pre-configured.
- Worker publishes delivery metrics to the shared meter (verifiable via unit/integration tests or collector).
- RFC kept in sync with implementation status, with deferred work tracked in backlog.

