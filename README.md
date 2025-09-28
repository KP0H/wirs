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

Without sign
```bash
curl -i -X POST http://localhost:8080/api/inbox/test \
  -H 'Content-Type: application/json' \
  -H 'X-Test: abc' \
  -d '{"ok": true}'
```

With sign
```bash
SECRET='<your-github-secret>'
BODY='{"ok":true}'
SIG="sha256=$(echo -n "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -binary | xxd -p -c 256)"

curl -i -X POST http://localhost:8080/api/inbox/github \
  -H "Content-Type: application/json" \
  -H "X-Hub-Signature-256: $SIG" \
  -d "$BODY"
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

### Background dispatcher
Run the worker locally:
```bash
dotnet run --project src/WebhookInbox.Worker
```

#### Retry & Backoff

Inline retries: configured via **.NET Resilience** (`Microsoft.Extensions.Http.Resilience`)
for HttpClient "delivery". Controlled by `Delivery:InlineRetryCount` (default 2).
Exponential-style backoff with jitter is applied via a custom DelayGenerator.
Scheduled backoff (between cycles): `Delivery:BackoffSeconds` (default `[60,300,900,3600,21600]`).
Max attempts (per Event+Endpoint): `Delivery:MaxAttempts` (default 6).  
On exhaustion, event is marked as **DeadLetter**.

Configuration:

`Delivery:PollIntervalSeconds` — polling interval  
`Delivery:BatchSize` — events per cycle  
`Delivery:HttpTimeoutSeconds` — HttpClient timeout  

### Idempotency & Deduplication
The ingestion endpoint (`POST /api/inbox/{source}`) is idempotent.

Key resolution priority:
1. `Idempotency-Key`
2. `X-Idempotency-Key`
3. `X-Hub-Signature-256`
4. SHA-256(payload)

Redis key format: `idem:{source}:{key}` → `eventId` (TTL = `Idempotency:KeyTtlSeconds`, default 86400).  
Duplicates return **200 OK** with the same `{ eventId, duplicate: true }` and no new row is inserted.

**Config:**
```json
{
  "Idempotency": { "KeyTtlSeconds": 86400 },
  "ConnectionStrings": {
    "Postgres": "...",
    "Redis": "localhost:6379"
  }
}
```

or environment: `REDIS__CONNECTION=redis:6379`

### API Rate Limiting (ingress)

The inbound endpoint `POST /api/inbox/{source}` is protected by a fixed-window rate limiter (ASP.NET Core Rate Limiting).
The limit is applied per source (`{source}`), with the partition key being the source value itself.

**Config:**
```json
"RateLimiting": {
  "DefaultRequestsPerMinute": 120,
  "Sources": [
    { "Source": "github", "RequestsPerMinute": 60 },
    { "Source": "stripe", "RequestsPerMinute": 30 }
  ]
}
```

### Observability: OpenTelemetry + Prometheus

API publish **Prometheus** metrics on `/metrics`.

Include standart instructions:
- ASP.NET Core, HttpClient, Runtime, EF Core

Custom metrics (Meter: `WebhookInbox`):
- `webhookinbox_events_total{status="ingested|duplicate"}`
- `webhookinbox_signature_validation_failures_total`
- `webhookinbox_idempotent_hits_total`
- `webhookinbox_rate_limit_blocked_total{source}` (for rate limiting)
- `webhookinbox_deliveries_total{result="success|failed|deadletter"}` (worker)
- `webhookinbox_delivery_duration_ms` (histograms)

**Local check**
```bash
curl http://localhost:8080/metrics
```


## Docker Compose stack

Run the service end-to-end (API, worker, PostgreSQL, Redis, Prometheus, Grafana) with Docker Compose:

```bash
cp .env.example .env
docker compose up --build
```
Environment defaults live in `.env.example`; copy it to `.env` and adjust as needed before running compose.

What you get:
- API on http://localhost:8080 (metrics at `/metrics`).
- Prometheus on http://localhost:9090.
- Grafana on http://localhost:3000 (defaults: `admin` / `admin`).
- API applies EF Core migrations automatically on startup when `AutoMigrate=true` (Docker Compose sets this by default).

A default Grafana dashboard is auto-provisioned from `docker/grafana/dashboards/webhookinbox.json`. For manual import details see [Grafana dashboard docs](docs/observability/grafana-dashboard.md).

To stop and remove containers:
```bash
docker compose down -v
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
