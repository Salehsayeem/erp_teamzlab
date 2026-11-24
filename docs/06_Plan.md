# 06 — Implementation Plan (living checklist)

> Status: LIVING FILE — update after EVERY completed step (`[ ]` → `[x]` + date + evidence note). Ordered by dependency.

> ▶ **WAVE LANDED 2026-11-24 — `main` @ `e380e09` (T2 persistence → T5 FINANCE, full regression green).** State: Phase 1+2 done; T1 restructure (efca865), T2 PostgreSQL persistence (5a549ee + C1 atomic idempotency 3414aae, review clean), T3 live verification (scripts/verify-all-features.sh 45/45 — surfaced + fixed the status-flip double-count 3c69dcd and the receiving-dock drift), INVENTORY UI (8275d73+4119c2c), T4 ASSETS (aa7474c+99e7a63 + fix round d21a41a..deff4f0 — 4 Important review findings fixed), T5 FINANCE (f1ee79f..72931d8 — periods/CoA/tax/aging/journal idempotency, 33→35 tests), Swagger UI (Swashbuckle 11, owner-approved), reusable web uikit (9db502b+9218479, /en|bn/design showcase), root locale redirect + LAN dev origin (3e6e7fb). **Suite 58/58 (zero skipped), web build green, smoke 45/45.** Regression gate passed on dev-sayeem; main fast-forwarded. **Remaining:** T6 PROCUREMENT deep, T7 SALES deep (briefs not yet drafted — recon needed), T8 README controller pass; BACKLOG gates: R14 multi-entity id scheme (blocks any second legal entity), control-edge contrast token (owner sign-off), FinanceGuards adoption for T4 asset postings done (86e053f). Dev DB :5433 fully migrated (InitialSchema → AddAssetsSeed → AddFinanceSeed → FixDepreciationRunsPk). Working ledger: `.superpowers/sdd/06_Plan/progress.md`.

Legend: `[ ]` todo · `[~]` in progress · `[x]` done (date) · `[b]` blocked by decision D-xx

## Phase 0 — Decisions (before architecture/build freeze)
- [ ] D-01 jurisdictions/entities/accounting basis
- [ ] D-04 tenancy model — **recommended default: shared tables + tenant_id + RLS** (simplest that satisfies isolation; revisit at scale)
- [ ] D-09 .NET track — **recommended default: .NET 11 STS** (spec's dated baseline; LTS switch possible before build freeze)
- [ ] D-10 PG version/provider — DB reachable only inside network; qualify on first connect
- [ ] D-11 Next.js/Node pins — pin at repo bootstrap
- [ ] D-07 initial language set (en + bn suggested; RTL deferred)
- [ ] Defer: D-02, D-03, D-05, D-06, D-08, D-12, D-13, D-14 to the phase that needs them

## Phase 1 — Foundation (Wave 1)
- [x] Repo scaffold: `apps/api` (.NET 11) done, `apps/web` (Next.js) done — build passes (Next 16.4.6), tooling pinned locally (D-09 default: .NET 11)
- [x] Tenant + legal-entity context middleware; server-side scoping in every handler (mock store)
- [x] Auth stub + memberships/RBAC (token = user id; real IdP when D-13 chosen)
- [ ] RLS policies + isolation tests — deferred until DB reachable (D-10); service-layer scoping meanwhile
- [x] Audit log (append-only) + evidence accessor (`GET /api/v1/audit-events`)
- [x] Outbox event envelope v1 (writes on approve/receipt); inbox consumer pending
- [x] Response envelope + global error middleware + validation pipeline
- [x] i18n scaffold: dictionaries en+bn, locale routing (Dict key-parity; all 6 pages render both locales)
- [ ] EF Core migration conventions + CI check — blocked on DB (D-10)
- [x] Telemetry: correlationId middleware end-to-end
- [x] Strict CORS for the browser client (default `http://localhost:3000`, `CorsOrigins` env override; preflight before auth)
- [x] Seed: one tenant, one entity, 3 users (admin/procurve/finance), 3 roles

## Phase 2 — Pilot slice (Wave 1 flows)
- [x] Procure-to-receive API: suppliers → PO → submit → maker-checker approval → receipt → stock + balanced AP accrual journal — 3/3 tests green
- [x] Journal reversal with linked provenance (`/{id}/reversals`)
- [x] Procure-to-receive UI: suppliers + PO screens w/ per-status actions (4-user picker + demo credentials hint on login)
- [x] Order-to-fulfill: customers → order → approval → stock allocation → shipment → AR invoice draft (2026-11-24, smoke-proven end-to-end by T3; maker-checker + COGS/AR journals)
- [x] Audit evidence visible for every step (`GET /api/v1/audit-events`)
- [x] Flow test (whole-chain coverage) + validation + authz tests

## Backlog — critic review findings (2026-11-23) — ✅ ALL FIXED commit `732d9fe`, 13/13 tests green
> Known limits: no negative scoping test possible on mock store (single tenant/entity); idempotency replays cached error responses too (semantically correct).
1. HIGH: tenant+entity scoping missing on every id lookup (Services.cs, Program.cs) — entity context never used
2. HIGH: over-receipt unbounded — reject received+qty > line.Qty before journal (ERP-PROCUREMENT-057) + test
3. HIGH: Idempotency-Key absent on create endpoints (dup receipts = dup stock+journals) — store key→response map in Db
4. MED: journal reversal leaves stock/receipt uncorrected, no outbox event — decrement stock, emit finance.journal.reversed
5. MED: error codes conflated — 404 *_NOT_FOUND, 400 VALIDATION_ERROR, 409 state conflicts only
6. MED: /journals needs journals:read; /audit-events admin-only; /outbox gate to * or drop
7. MED: /submit needs po:write; add po:approve permission (SoD beyond instance maker-checker)
8. MED: negative-path tests missing (approve-as-finance, reverse-as-proc, receive-on-draft, wrong-SKU, over-receipt)
9. MED: malformed JSON bodies bypass envelope — binding-failure filter → VALIDATION_ERROR
10. LOW: /stock,/audit-events,/outbox reads unlocked; decimal.Round → MidpointRounding.AwayFromZero (ERP-FINANCE-010)
11. OPEN: PO "send" step (approved→sent) has no endpoint/button (02_AppFlow §3.1) — decide if pilot needs it; Receive already accepts Approved|Sent
12. PARTIAL (design): warning color #B26A00 fails 4.5:1 as text — badges now ship the dot/border treatment in the uikit (feat/web-uikit, `.badge::before` + `--badge-accent`, `.badge--warning-strong` removed); still open: control edges `text-muted` swap (`.input`/`.btn--secondary` still use `--border`) and Noto Sans Bengali fallback (D-07)

## Phase 3 — Core transactional domains (Wave 2, per domain — order by D-01/D-02)
> Screen counts from the per-module inventory in `02_AppFlow.md` §4 (derived from `spec_snapshot.md` card titles).

- [ ] FINANCE deep: AP/AR close, periods/locks, tax (**D-01**) (20 screens)
- [x] INVENTORY deep: valuation, counts, transfers (16 screens) — API slice DONE (17/17 tests, commit ec4fcb4) + UI DONE (8275d73+4119c2c, build 24/24, browser-verified en+bn)
- [x] PROCUREMENT deep: contracts, sourcing (12 screens) — API spine DONE (15ae201..3de82e9 on feat/procurement-deep: sourcing events/lots/frozen criteria, eligibility gates + exceptions, buyer-recorded digest-receipt bids, sealed-bid opening, evaluator scoring with deterministic trace, maker-checked awards with eligibility re-check + lot ceilings, commitment-only contracts with versioned amendments/overlap protection/obligations/renewal; 21 new tests, suite 79/79; UI screens pending)
- [~] SALES deep: CRM, billing, dunning (14 screens) — API slice DONE on `feat/sales-deep` (T7: leads/CRM/opportunities, invoice documents with per-line tax + maker-checker posting, credit/debit adjustments, collections + credit exposure; journal-only invoice endpoint RETIRED per R-T7f; suite 58→88 green; frontend screens not yet built)
- [ ] PROCUREMENT deep: contracts, sourcing (12 screens)
- [~] SALES deep: CRM, billing, dunning (14 screens) — API slice DONE on `feat/sales-deep` (T7: leads/CRM/opportunities, invoice documents with per-line tax + maker-checker posting, credit/debit adjustments, collections + credit exposure; journal-only invoice endpoint RETIRED per R-T7f; fix round 1: status-guarded posting, invoice discard, per-customer exposure serialization, route-aware idem hash — suite 58→94 green; frontend screens not yet built)
- [ ] ASSETS: projects, service, fixed assets (15 screens)
- [ ] PEOPLE: workers, time, payroll handoff (**b D-05**) (15 screens)
- [ ] MFG: planning, production, quality (**b D-02**) (14 screens)

## Phase 4 — Scale & breadth (Wave 3)
- [ ] Jurisdiction/language packs (**b D-07**), tenant placements, read models/partitioning (**b D-08**)
- [ ] Upgrade/DR/event-replay rehearsals; compatibility policy (**b D-03, D-06, D-14**)

## Done-log
| Date | Step | Evidence |
|---|---|---|
| 2026-11-23 | Scaffold: CLAUDE.md, docs 01–06, .env/.gitignore/.cursorrules, snapshot | this file, branch `dev-sayeem` |
| 2026-11-23 | API foundation + procure-to-receive slice, 3/3 tests green | `apps/api`, `apps/api-tests` — `dotnet test` Passed 3/3 |
| 2026-11-24 | `apps/api` restructured to layered layout (Dtos/Models/Controllers/Interfaces/Services/Common/Middleware; minimal APIs → `[ApiController]`, `AddErpServices` DI pin), zero behavior change | branch `feat/backend-core` — `dotnet test apps/api-tests` 17/17 green (baseline and final), test files untouched |
| 2026-11-24 | Dev-env readiness: README full run guide (prereqs, EF migrations bootstrap, `TESTPG*` vs `PG*`, troubleshooting), strict CORS on the API (preflight before auth), `dotnet-ef` tool manifest, documented `.env.example` | branch `feat/backend-core` — this session; see README "Run it" |
| 2026-11-24 | PostgreSQL persistence via EF Core + Npgsql (19 tables, 8 repos + number-series/idempotency stores, InitialSchema migration w/ seed, per-class throwaway test DBs) | `5a549ee` feat/backend-core — `dotnet test apps/api-tests` 18/18 (17 untouched + 1 persistence proof, RED→GREEN); review Approved (idempotency atomicity follow-up in flight) |
| 2026-11-24 | INVENTORY UI slice: stock / warehouses+locations / transfers / counts / valuation screens, en+bn (108 keys/locale, compile-checked parity) | `8275d73`+`4119c2c` feat/inventory-ui — clean build 24/24 pages, live browser-verified both locales, review clean after 1 fix round |
| 2026-11-24 | C1 fix: atomic idempotency — reserve/complete in one transaction; concurrent same-key resolves to winner replay (was: raw 500s + duplicate writes) | `3414aae` feat/backend-core — RED 9/10 concurrent 500s → GREEN; suite 20/20; fresh review clean (no Critical/Important) |
| 2026-11-24 | T3 live verification: `scripts/verify-all-features.sh` (45 assertions) vs dev PG :5433 — found + fixed 2 real bugs: status flips double-counted committed rows (PO Received at 9/10 → state-blocked forever); receiving dock drifted to first-created location | `3c69dcd` + InventoryRepository fix on feat/backend-core — both RED→GREEN in-suite; final smoke 45/45 |
| 2026-11-24 | T4 ASSETS slice: fixed assets (capitalization/depreciation/transfer/impairment/disposal), projects (gated closure), service cases (SLA/breach), AddAssetsSeed migration, R-ASSETS + U-005 | `aa7474c`+`99e7a63` feat/assets-api — 31 pass + 1 honest Skip (PERIOD_LOCKED pending T5) / 32; review in flight |
| 2026-11-24 | Review-minor fixes: CorsOrigins empty-string + trailing-slash fallback (TDD), README table count 19→23, `.sr-only` utility (F4) | `fix/cors-origins` (135739c..4b0c646, branch suite 26/26), `fix/web-sr-only` (1c194bc, build green) |
| 2026-11-24 | Merge wave → `dev-sayeem` (backend-core + assets + cors + sr-only + inventory-ui, all clean) + FULL regression | suite 38 pass + 1 skip / 39, `npm run build` green, smoke 45/45 on merged binary; `main` merge held pending T4 review verdict + T5 |
| 2026-11-24 | Reusable web UI kit: Field/Input/Textarea/Select (forwardRef, aria wiring), Card, AA dot/border Badge (finding 12), Modal (focus trap/Esc/focus return), ToastRegion (aria-live, 5s auto-dismiss w/ hover-focus pause, errors manual), Tabs (roving/RTL arrows), Pagination, EmptyState/ErrorState/Skeleton polish; `/design` showcase en+bn (~70 new dict keys, compile-checked parity) | feat/web-uikit — `npm run build` green (26 routes incl. /en|bn/design), browser-verified: modal trap/Esc/focus-return, toast roles+dismiss, tab roving+arrows, pagination aria-current; screenshots in `.superpowers/sdd/06_Plan/uikit-*.png` |
| 2026-11-24 | T5 FINANCE deep: fiscal periods + centralized FinanceGuards lock on every posting path, CoA, effective-dated tax codes with control-line computation (2200/1450), AR aging buckets as seed data, journals POST payload-hash idempotency (409 IDEMPOTENCY_CONFLICT), AddFinanceSeed | f1ee79f..72931d8 feat/finance-api — 33/33 RED-first, review clean after fix round (double-reversal guard, test de-ordering, Basis rate trap) → 35/35 |
| 2026-11-24 | Swagger UI (Swashbuckle 11, owner-approved), reusable web uikit + /design showcase (AA-checked, Playwright-verified), root locale redirect + LAN dev origin, depreciation_runs PK fix migration, T4 period-lock un-stubbed onto FinanceGuards | 3e4039f, 9db502b+9218479, 3e6e7fb, 510a63c, 86e053f — merged suite 58/58 zero skipped |
| 2026-11-24 | FULL REGRESSION on dev-sayeem (suite 58/58, web build, smoke 45/45) → pushed → main fast-forward | main 46ee51e..e380e09 on origin — wave T2→T5 complete |
