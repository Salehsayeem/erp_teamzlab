# Review — FINANCE-deep P5 (foundations) — 2026-11-27

Scope: `26e4cdd..1106150` (4 P5 commits) + union merge `8219840` context. Fresh-context reviewer,
read-only (suite already run separately: 337/337 on union). Verdict: **APPROVE WITH FIXES**.

## Claims scorecard
TRUE: resolver exactly-one-or-block; tenant/entity scoping (EntityContext from verified token);
permission gating (fx:read/fx:write, U-015 negatives hit real endpoints with valid R-FIN2 token);
migration additive + chain 080511→150951→151812; R-FIN full 47-perm §3 superset; number-series void
cannot reissue sequences; error codes 329 keyed en+bn; web anti-pattern greps clean (no raw fetch,
hooks above early returns, no `?.[` row renders, fin.* namespace disjoint, en+bn parity).
MOSTLY TRUE: journal line enforcement (finding 1); error-code mapping (finding 4).
PARTIALLY FALSE: 007 effective-dated functional currency (finding 3 — storage-only; pinned test
near-tautological).

## Findings

### Important (fix round dispatched, RED-first)
1. **Scale vs wrong currency** — `JournalService.ValidateLines` applies the LINE currency's scale to
   `Debit`/`Credit` (functional amounts per Finance.cs model comment). JPY line + 819.50 BDT functional
   debit → falsely rejected; functional scale unenforced when foreign currency cited. JPY test pins the
   bug as correct behavior — split it.
2. **FX ambiguity permanent dead-end** — no ApproveRate pre-check for existing approved same
   (pair,date,type); no withdraw endpoint; no DB backstop. Two approvals → FX_RATE_AMBIGUOUS forever,
   P3/P4 postings on that pair blocked until DB surgery. Fix: 409 pre-check + withdraw endpoint +
   partial unique index WHERE status='Approved' (in-place edit of unapplied 20261127151812).
3. **Effective-dated functional currency = storage-only** — `UpsertSetting` records EffectiveFrom;
   `ValidateLines` reads current value, ignores the date. Future-dated change alters drafts immediately.
   RULING (this round): single-row upsert stays; drafts dated BEFORE EffectiveFrom → 400 VALIDATION_ERROR
   naming the pending change; on/after → new value. (Fuller as-of-with-history deferred with R14 if
   the owner wants it.)

### Minor — fixing now
4. FX_RATE_MISSING → 409 default arm; belongs to 404 family (missing reference data ≠ state conflict).
8. Concurrent double-void race → unhandled 500; catch unique-violation → 409 NUMBER_ALREADY_VOIDED.
9. DimensionValue.Active ignored for NEW postings (inactive-in-interval accepted on create) — enforce
   Active+interval on validation; posted lines never re-validated so 003 AC2 history clause stays true.
10. number-series page reuses `s.actionFailed` for client-side validation copy — proper fin.series key.

### Minor — DEFERRED to R14 multi-entity id-scheme redesign (ruling, matches existing global doc_ sequence design)
5. Literal-id PK collisions across legal entities (CUR-{code}, DS-{code}, DV-{set}-{value}, NSV-{number}
   are global PKs; second entity same void → PK collision 500).
6. NumberSeries.List reads pg_sequences unscoped (last_value mixes tenants sharing schema).
7. Void accepts never-issued numbers (bookkeeping; issued-range validation unreliable against global
   sequences — belongs with 5/6).

## Assessment recorded
- Resolver-blocks + no unique constraint: safe for correctness (loud blocking beats silent picking),
  operationally fragile → fixed this round with pre-check + withdraw + partial unique index backstop.
- 008 provenance: COMPLETE (source, retrieved_at, rate_date, precision, created_by, approved_by/at).

## Gates at fix-round end (all green required)
Full suite (337+new), tsc, error-code guard, build if web touched beyond copy. Scoped re-review before
feat/finance-deep → dev-sayeem merge.
