---
RFC: 0001
Title: Webhook Inbox MVP
Status: Active
Owners: Dmitrii [KP0H™] Pelevin
Created: 2025-09-25
---

## Summary
A minimal service to receive webhooks, store them reliably, provide UI to inspect payloads/headers, and re-deliver to configured endpoints with exponential backoff retries and deduplication.

## Problem / Motivation
External systems send webhooks, but consumers face challenges:
- Event loss during spikes or outages.
- No idempotency or deduplication.
- No UI for troubleshooting or manual re-delivery.
- Poor visibility into retries and failures.

## Scope
- `POST /api/inbox/{source}`: receive, validate HMAC signature (optional), store in PostgreSQL.
- Persistence: Event, Endpoint, DeliveryAttempt (EF Core + PostgreSQL).
- Dispatcher (BackgroundService): deliver to endpoints with retry & jitter.
- Deduplication / idempotency: Redis keys (TTL, hash of signature/Idempotency-Key).
- Security: HMAC-SHA256 signatures (GitHub/Stripe style), JWT (MVP).
- Observability: Serilog + OpenTelemetry (traces/metrics/logs) + Prometheus exporter.
- UI (Blazor Server): event list, details, manual re-deliver.
- DevOps: Docker Compose (api+worker+pg+redis+prom+grafana), GitHub Actions CI.

## Non-Goals
- Multi-tenant mode and full RBAC.
- Multiple OAuth providers.
- Payload transformations (templating).
- 99.99% SLA and HA.

## Architectural Overview
### Components
- **WebhookInbox.Api**: minimal APIs, `/inbox`, `/events`, `/healthz`, `/metrics`, Swagger.
- **WebhookInbox.Worker**: BackgroundService, retries via resilience pipeline.
- **Infrastructure**: EF Core (PostgreSQL 16), Redis 7.
- **UI**: Blazor Server MVP.
- **Observability**: Serilog, OTel, Prometheus.

### Data Model
- **Event**: Id, Source, ReceivedAt, Headers, Payload, SignatureStatus, Status.
- **Endpoint**: Id, Url, Secret, IsActive, RateLimitPerMinute, PolicyJson.
- **DeliveryAttempt**: Id, EventId, EndpointId, Try, SentAt, ResponseCode, ResponseBody, Success, NextAttemptAt.

### Flow
1. API receives webhook → validates signature (if configured) → checks Redis idempotency → writes to PG.
2. Worker pulls due events → delivers to endpoints → records DeliveryAttempt.

## Trade-offs
- Redis as scheduler: simple but not HA.
- Payload in PG: simple but limited scale.
- Blazor: fast dev, less ecosystem than React.

## Metrics
- `events_total{status}`
- `deliveries_total{status="success|failed|deadletter"}`
- `retry_scheduled_total`
- `delivery_duration_ms`
- `dead_letter_total`

## Security
- HMAC-SHA256 signatures.
- JWT (dev-secret).
- Rate limiting middleware.

## Risks
- PG under high IO load.
- Signature validation errors.
- Retry storm without proper limits.

## Exit Criteria
- 202 Accepted on `POST /inbox` with `eventId`; event visible in UI.
- Successful 2xx response → mark `Success=true`; errors → schedule retry (backoff).
- Duplicate idempotency key → no duplicate Event.
- `/metrics` exposes standard counters and histograms.
- README describes docker compose demo and curl example.