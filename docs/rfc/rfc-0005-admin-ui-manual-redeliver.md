---
RFC: 0005
Title: Admin UI & Manual Redeliver
Status: Active
Owners: Dmitrii [KP0H™] Pelevin
Created: 2025-09-27
---

## Summary
Introduce a lightweight admin user interface to inspect webhook events and manually trigger re-delivery.  
This provides visibility and control for developers and operators when troubleshooting integrations.

## Problem / Motivation
At MVP stage we ingest and dispatch events automatically, but:
- Operators cannot easily inspect headers/payloads of stored events.
- Failed events cannot be manually retried without DB access.
- Lack of UI prevents adoption by teams who need visibility.

We need a simple UI that surfaces stored events and provides a manual “Re-deliver” action.

## Scope
- **Admin UI** (Blazor Server or lightweight React SPA):
  - Events list with filters (status, source, date).
  - Event details: headers, payload, delivery attempts.
  - Manual re-deliver button (per endpoint).
- **API extensions**:
  - `GET /api/events` — list with pagination/filter.
  - `GET /api/events/{id}` — details with attempts.
  - `POST /api/events/{id}/redeliver?endpointId=...` — manual trigger.
- **Documentation**:
  - Screenshots in README.
  - Example workflows: troubleshooting, redelivery.

## Non-Goals
- Multi-user RBAC with granular permissions.
- Full audit trail for admin actions.
- Rich UI polish (MVP only).

## Architectural Overview
- **UI**:
  - Blazor Server (shared hosting with API) OR separate React app calling API.
  - Auth via same mechanism as API (JWT/OAuth).
- **API**:
  - Extends existing endpoints for listing and redeliver.
  - Redeliver reuses Dispatcher pipeline (creates a new DeliveryAttempt).
- **Storage**:
  - DeliveryAttempt table grows with manual redeliver actions.
- **Deployment**:
  - Docker Compose includes UI alongside API/Worker.

## Trade-offs
- Blazor Server allows faster dev in .NET ecosystem, but React may be more familiar to front-end devs.
- Minimal UI keeps scope tight, but may lack polish.
- No multi-tenant/RBAC in MVP.

## Metrics
- `manual_redeliver_total{endpoint}`
- `ui_events_viewed_total`
- `ui_redeliver_success_total`
- `ui_redeliver_failed_total`

## Security
- Admin UI requires authentication (JWT/OAuth).
- Manual re-deliver action must be restricted to authenticated admins.

## Risks
- Manual retries can amplify retry storms if used irresponsibly.
- Payload visibility may expose sensitive data.
- UI may lag if querying large datasets without pagination.

## Exit Criteria
- Admin UI shows events list and event details.
- Manual redeliver action available and functional.
- Metrics for manual redeliver recorded.
- README includes screenshots of the UI.
