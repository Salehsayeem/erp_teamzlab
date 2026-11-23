# 06 — Implementation Plan (living checklist)

> Status: LIVING FILE — update after EVERY completed step (`[ ]` → `[x]` + date + evidence note). Ordered by dependency.

> ⏸ **PAUSED 2026-11-24 (owner command: resume tomorrow).** State: Phase 1+2 done. **T1 layered restructure DONE** (efca865, review clean, 17/17 frozen contract). **T2 PostgreSQL persistence COMMITTED** (5a549ee: EF Core+Npgsql 19 tables, repos, migration seed, idempotency table; 18/18 green incl. persistence proof on local PG; review Approved, 1 Important follow-up IN FLIGHT: atomic idempotency fix). **INVENTORY UI DONE** (8275d73+4119c2c on `feat/inventory-ui`, review clean, build 24/24, browser-verified). **T3 live insert/get verification NOT started.** Tree holds uncommitted peer-session dev-env work (CORS/README/.env/2 plan lines) + UNTRACKED duplicate migration `20261123201118_InitialCreate` — DELETE that pair (InitialSchema already committed in 5a549ee). **Resume order:** (1) land + re-review the T2 idempotency fix; (2) settle peer edits, delete the duplicate migration pair; (3) T3 `verify-all-features.sh` vs local PG (superuser postgres/postgres — .env creds are remote-only); (4) merge `feat/backend-core` + `feat/inventory-ui` → `dev-sayeem` → FULL regression (dotnet suite + `npm run build` + endpoint smoke + fix uikit `sr-only` F4) → `main` (branch tiers per CLAUDE.md); (5) T4 ASSETS / T5 FINANCE — briefs ready with rulings in `.superpowers/sdd/06_Plan/task-4|5-brief-draft.md`. Working ledger: `.superpowers/sdd/06_Plan/progress.md`. Read CLAUDE.md + docs 01–05 first.

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
- [ ] Order-to-fulfill: customers → order → approval → stock allocation → shipment → AR invoice draft
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
12. OPEN (design): warning color #B26A00 fails 4.5:1 as text — use dot/border only; controls use text-muted edges (border is decorative); add Noto Sans Bengali fallback (docs/design/ui-direction.md)

## Phase 3 — Core transactional domains (Wave 2, per domain — order by D-01/D-02)
> Screen counts from the per-module inventory in `02_AppFlow.md` §4 (derived from `spec_snapshot.md` card titles).

- [ ] FINANCE deep: AP/AR close, periods/locks, tax (**D-01**) (20 screens)
- [~] INVENTORY deep: valuation, counts, transfers (16 screens) — API slice DONE (locations, transfers, counts+maker-checker, valuation; 17/17 tests, commit ec4fcb4); UI pending
- [ ] PROCUREMENT deep: contracts, sourcing (12 screens)
- [ ] SALES deep: CRM, billing, dunning (14 screens)
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
