---
description: Posted-state invariants — append-only ledgers, maker-checker, idempotency, envelope codes
paths:
  - "apps/api/**"
---

# Posted-state invariants (ERP-FINANCE-014 / ERP-INVENTORY-015 / ERP-DOTNET-025)

- Never UPDATE or DELETE a posted journal, posted stock movement, or posted pay/billing
  document. Corrections are NEW linked reversal/compensating entries carrying provenance.
- Preparer cannot approve (maker-checker). Re-editing after approval invalidates the
  approval (ERP-FINANCE-013) — enforce with status + EF concurrency token, not convention.
- Every posting path runs the FinanceGuards period-lock check and writes its balanced
  journal + ledger/stock effect in ONE SaveChanges (one transaction, all or nothing).
- Create/post endpoints honor Idempotency-Key; replays return the original response;
  same key + different payload → 409 IDEMPOTENCY_CONFLICT.
- Envelope codes: 404 `*_NOT_FOUND`, 400 `VALIDATION_ERROR`, 409 state conflicts only.
- Tenant + legal-entity scope resolved from verified claims on every query — never from
  body, header, or query string.
