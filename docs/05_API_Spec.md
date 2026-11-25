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
| * | /api/v1/quotes… (+ /revisions, /approvals, /acceptances, /revalidations) | SALES | S1 quotes: immutable revisions, maker-checker, pinned acceptances (021/022/023/030) |
| * | /api/v1/price-books…, /api/v1/customer-contract-prices | SALES | S2 pricing: effective-dated, precedence evidence, PRICE_OVERLAP on tier overlap (024–026) |
| GET | /api/v1/customers/{id} | SALES | S3 customer 360 composite (profile, hierarchy rollup, open docs, exposure for credit:read holders, onboarding state) |
| POST | /api/v1/customers/{id}/hierarchy | SALES | S3 self/cycle-safe parent links → 400 CUSTOMER_HIERARCHY_CYCLE (012) |
| * | /api/v1/customers/{id}/onboarding-checks… | SALES | S3 mandatory controls gate SO submit → ONBOARDING_CONTROLS_UNMET items (013) |
| * | /api/v1/customer-merges/preview + /{previewId}/executions | SALES | S3 merge: customers:merge (R-ADMIN), posted balances block → MERGE_BLOCKED (014) |
| * | /api/v1/sales-orders… (+ /holds, /amendments, /cancellations, GET /{id}) | SALES | S4 lifecycle: blocking holds gate shipment (035), versioned amendments pending approval (034), per-line fulfilled/remaining/backorder (036/037), reasoned cancellations keep posted invoices (039) |
| * | /api/v1/recurring-schedules… (+ /pauses, /generations) | SALES | S4 recurring: ≤1 occurrence per (schedule, period), paused yields none (040) |
| * | /api/v1/forecast… (+ /forecast-submissions, /forecast-snapshots) | SALES | S5 buckets (unweighted/weighted/override + unclassified) and immutable snapshots (018/020) |
| * | /api/v1/invoices/{id}/einvoice-submissions, /delivery-attempts… | SALES | S6 adapter boundary + delivery attempts (050/051) |
| * | /api/v1/sales-returns… (+ /receipts), /api/v1/adjustments/{id}/refund-requests, /api/v1/refund-requests/{id}/approvals | SALES | S7 returns capped at sold − prior, refunds are finance-only (061/062) |
| * | /api/v1/receipt-candidates… (+ /payer-links) | SALES | S8 unapplied payment candidates; allocation stays finance-owned (054/055) |
| GET | /api/v1/ar-reconciliation | SALES | S8 read view: docId/financeRef/currency/openAmount/asOf/status (056) |
| * | /api/v1/rebate-agreements… (+ /accruals, /recalculation-previews), /api/v1/rebate-claims… | SALES | S9 accrual→claim capped at approved accrual; settlement rebates:settle (063) |
| GET | /api/v1/sales-analytics?domain=crm\|orders\|billing | SALES | S10 role-filtered metrics, drill-through ids (065) |

## 5. Cross-cutting
- Rate limiting on auth + public write endpoints; strict CORS allowlist; security headers at middleware level.
- Telemetry: correlationId flows UI → API → events → logs (chapter 15).
- All list endpoints tenant-scoped server-side; no cross-tenant read path exists, tested.

## 6. SALES deferred-screens boundaries (this wave)
- **Inventory reservation contract (ERP-SALES-038) — DEFERRED (controller ruling Q-S4).** Shipments apply stock directly through the existing `ApplyStock` semantics (draw at ship, restock at default receiving location on return receipts); there is no reservation handshake yet. ATP accuracy lands with the INVENTORY reservation work (INVENTORY-021/024 also deferred). Nothing in S1–S10 blocks on it.
- **Bundles (ERP-SALES-041) — DEFERRED (Q-S5, wave 4).** Bundle-shaped orders are entered as plain lines meanwhile.
- **E-invoice jurisdiction (ERP-SALES-050) — adapter boundary only (Q-S6).** `IInvoiceAdapter` ships with a `NotEnabled` stub for the seeded entity; country/schema profiles arrive with the D-01/D-13 rule. Failed validation blocks transmission and the submission row is kept with structured errors (in the envelope's `error.detail`).
- **Forecast snapshots (ERP-SALES-020) — synchronous on demand (Q-S7)** until the jobs infrastructure lands; a failed generation records an `Incomplete` row instead of vanishing.
- **AR allocation (ERP-SALES-055) — finance-owned.** Sales ships the receipt-candidate write surface and the reconciliation READ only; no allocation write endpoint exists here by design.
- **Analytics metric definitions (ERP-SALES-065):** `crm` = open opportunities, value = Σ line qty × target price (017), grouped by owner or customer; `orders` = sales orders, value = Σ line qty × unit price, grouped by status or customer; `billing` = posted invoices, value = tax-inclusive total (048), grouped by customer. Rows carry `docIds` for drill-through; a caller lacking the domain's read permission gets 403 and no rows.
- **Commission (ERP-SALES-064) — event boundary only.** `sales.commission.event` on invoice posting (credited owner = source order's maker, source transaction, event date); `sales.commission.reversed` on credit-approval reversals, linked by `sourceInvoiceId`. No claim/approval surface this wave.
- **Quote pricing (024):** line `unitPrice` is optional — when absent the internal PriceResolutionService resolves contract price (highest minQty, latest effectiveFrom) → book entry (lowest book precedence, highest entry minQty); no match → 404 `PRICE_NOT_FOUND`, never a silent zero. A line discount above the configured cap (`sales.discount_cap_pct`) or a revision total above `quotes.approval_threshold` lands the revision `PendingApproval` with an approver ≠ issuer.
