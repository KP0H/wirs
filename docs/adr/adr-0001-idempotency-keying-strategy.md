# ADR-0001: Idempotency Keying Strategy
- Decision: Use Redis SET NX with TTL; store eventId under key `idem:{source}:{key}`.
- Key resolution priority: Idempotency-Key → X-Idempotency-Key → X-Hub-Signature-256 → SHA-256(payload).
- Duplicate requests return 200 OK with {eventId, duplicate=true}.
- Status: Accepted
- Consequences: Requires Redis availability for best-effort dedup; if Redis is down, duplicates are possible.
