# # Webhook Inbox & Retry Service

A mini-service for secure webhook reception, storage, observability, and reliable retransmission to configurable endpoints with intelligent retries.

## Overview
- **Tech stack**: ASP.NET Core 9, EF Core + PostgreSQL, Redis, Serilog, OpenTelemetry, Polly, Docker, GitHub Actions.
- **Core features**:
  - Ingestion endpoint (`/api/inbox/{source}`) with optional HMAC validation.
  - Reliable storage in PostgreSQL (Event, Endpoint, DeliveryAttempt).
  - Dispatcher with exponential backoff retries (Polly + jitter).
  - Deduplication with Redis (idempotency keys).
  - Admin UI (Blazor Server) to inspect events and manually re-deliver.
  - Observability: structured logs, traces, metrics (Prometheus exporter).
  - DevOps: Docker Compose stack, GitHub Actions CI.

## Quick Start

### Ingest a webhook
```bash
curl -i -X POST http://localhost:8080/api/inbox/github \
  -H 'Content-Type: application/json' \
  -H 'X-Test: abc' \
  -d '{"ok": true}'
```

**Expected**: HTTP/1.1 202 Accepted  
**Body**: {"eventId":"<GUID>"}  
**Location header**: /api/events/<GUID>

## Development

### Database (EF Core + PostgreSQL)

Set connection string in `src/WebhookInbox.Api/appsettings.Development.json`:

```
"ConnectionStrings": { 
    "Postgres": "Host=localhost;Port=5432;Database=webhook_inbox;Username=postgres;Password=postgres" 
}
```

Create migration and update database:
```bash
dotnet tool update --global dotnet-ef

dotnet ef migrations add Initial --project src/WebhookInbox.Infrastructure --startup-project src/WebhookInbox.Api --output-dir Migrations

dotnet ef database update --project src/WebhookInbox.Infrastructure --startup-project src/WebhookInbox.Api
```

## RFCs / Milestones
We use RFCs to document scope, architecture, and decisions. Each milestone references one or more RFCs.

- [RFC-0001: Webhook Inbox MVP](docs/rfc/rfc-0001-webhook-inbox-mvp.md)
- [RFC-0002: Dispatcher & Retry Policy](docs/rfc/rfc-0002-dispatcher-retry.md)

### Milestones
- **M1 – Ingestion & Storage** — API skeleton, EF Core models, health checks, Swagger. *Exit*: `POST /inbox` writes Event to PostgreSQL.
- **M2 – Dispatcher & Retry** — Background worker, Polly retry, DeliveryAttempt records. *Exit*: Successful delivery to httpbin; failed endpoint schedules retries.
- **M3 – Idempotency, HMAC, Rate Limits** — Redis deduplication, HMAC validation, rate limiting. *Exit*: Duplicate requests blocked; invalid signatures rejected.
- **M4 – Observability & SRE** — OTel traces/metrics/logs, Prometheus + Grafana dashboard. *Exit*: Metrics available at `/metrics`; dashboard import works.
- **M5 – Admin UI & Manual Redeliver** — Blazor UI list/details/attempts, manual re-deliver. *Exit*: Redeliver works via UI.
- **M6 – Packaging & CI** — Docker Compose full stack, GitHub Actions CI pipeline. *Exit*: `docker compose up` starts stack; CI passes.