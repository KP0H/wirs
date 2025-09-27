---
RFC: 0004
Title: Observability & SRE
Status: Active
Owners: Dmitrii [KP0H™] Pelevin
Created: 2025-09-26
---

## Summary
Add full observability to the Webhook Inbox system: metrics, traces, and logs.  
Ensure operators and developers can detect issues (latency, retries, DLQ growth) and debug them efficiently.

## Problem / Motivation
Without observability:
- Failures in webhook delivery may go unnoticed.
- Root cause analysis is slow due to lack of correlation between services.
- No visibility into retry storms, DLQ growth, or high-latency endpoints.

We need structured logs, distributed tracing, and service-level metrics to provide a complete operational picture.

## Scope
- **Metrics**:
  - Prometheus metrics endpoint `/metrics` in API and Worker.
  - Counters for events, deliveries, retries, DLQ.
  - Histograms for request and delivery latency.
- **Traces**:
  - OpenTelemetry instrumentation for ASP.NET Core, HttpClient, EF Core.
  - Context propagation across API → Worker → outbound HTTP.
- **Logs**:
  - Structured logging with Serilog.
  - Correlation ID per request/event for consistent tracing.
- **Dashboards & Alerts**:
  - Minimal Grafana dashboard (importable JSON).
  - Example alerts: high DLQ growth, error rate > 10%.

## Non-Goals
- Full-fledged on-call runbooks (future).
- Integration with external APM vendors (Datadog, New Relic).
- Advanced anomaly detection.

## Architectural Overview
- **API**: Exposes `/metrics`; traces requests and DB operations.
- **Worker**: Exposes `/metrics`; traces event processing and HttpClient calls.
- **Prometheus** scrapes metrics from API and Worker.
- **Grafana** visualizes key metrics and latency distributions.
- **Serilog** writes structured logs (JSON or text).

## Trade-offs
- Using Prometheus + Grafana is standard, but requires infra setup.
- Serilog is simple and reliable, but may need sink tuning for production scale.
- Full OTel collector deployment is out of scope (we use SDK defaults).

## Metrics
- `events_total{status="new|dispatched|failed|deadletter"}`
- `deliveries_total{status="success|failed"}`
- `retry_scheduled_total`
- `delivery_duration_ms_bucket`
- `dlq_size_gauge`

## Security
- `/metrics` endpoint should not expose sensitive data.
- Auth optional; may rely on network-level protections in MVP.

## Risks
- Metrics cardinality explosion if labels not controlled (e.g., per-endpoint).
- High log volume may increase storage costs.
- Missing alerts → silent failures.

## Exit Criteria
- API and Worker expose Prometheus metrics.
- OTel traces collected for API → Worker → HttpClient.
- Logs structured and correlated via eventId.
- Grafana dashboard provided in `/docs/grafana-dashboard.json`.
- Alerts for DLQ size and error rate documented.
