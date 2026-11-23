# 05 — API & Integration Spec (skeleton)

> Status: SKELETON. Source: integration note §5 + `ERP-INTEGRATION` cards. Contracts versioned; module owns its authoritative facts (spec principle).

## 1. Conventions
- REST, base `/api/v1`. Resource URLs plural; actions that don't fit CRUD become explicit subresources (`/purchase-orders/{id}/approvals`).
- Auth: IdP token (provider TBD, **D-13**) + tenant/entity context from verified claims — never from client body.
- Every request validated before business logic (CLAUDE.md backend rules).

## 2. Response envelope (all endpoints)
```json
// success
{ "success": true, "data": { "...": "..." } }
// error
{ "success": false, "error": { "code": "PO_APPROVAL_REQUIRED", "message": "Purchase order requires approval before sending." } }
```
- Codes: `SCREAMING_SNAKE`, stable, documented per endpoint. Stack traces never in responses.
- Pagination: `?page=&pageSize=` → `data: { items, page, pageSize, total }`.
- Idempotency: unsafe externalized operations accept `Idempotency-Key` header (spec assumption #4).

## 3. Event envelope (outbox/inbox, versioned)
```json
{ "eventId": "uuid", "type": "procurement.po.approved", "version": 1,
  "tenantId": "...", "legalEntityId": "...", "occurredAt": "ISO-8601",
  "actor": { "userId": "...", "onBehalfOf": null }, "payload": { "...": "..." },
  "causationId": null, "correlationId": "uuid" }
```
- Cross-module work prefers events over synchronous calls. Every consumer idempotent + retryable; DLQ with reconciliation (chapter 13).

## 4. Endpoint inventory (seed — one row per built slice)
| Method | Path | Domain | Notes |
|---|---|---|---|
| POST | /api/v1/auth/token-exchange | SECURITY | IdP token → app session + tenant context |
| GET | /api/v1/me | PLATFORM | user, memberships, roles, locale |
| GET/POST | /api/v1/entities | PLATFORM | legal entities (admin) |
| * | /api/v1/suppliers… | PROCUREMENT | pilot slice |
| * | /api/v1/purchase-orders… (+ /approvals, /receipts) | PROCUREMENT | pilot slice |
| * | /api/v1/customers… | SALES | pilot slice |
| * | /api/v1/sales-orders… (+ /shipments, /invoices) | SALES | pilot slice |
| * | /api/v1/journals… (+ /reversals) | FINANCE | postings behind approval |
| * | /api/v1/items… /stock… | INVENTORY | pilot slice |
| * | /api/v1/audit-events | SECURITY | read-only, scoped |

## 5. Cross-cutting
- Rate limiting on auth + public write endpoints; strict CORS allowlist; security headers at middleware level.
- Telemetry: correlationId flows UI → API → events → logs (chapter 15).
- All list endpoints tenant-scoped server-side; no cross-tenant read path exists, tested.
