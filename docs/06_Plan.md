# 06 — Implementation Plan (living checklist)

> Status: LIVING FILE — update after EVERY completed step (`[ ]` → `[x]` + date + evidence note). Ordered by dependency.

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
- [x] Repo scaffold: `apps/api` (.NET 11) done, `apps/web` (Next.js) in progress, tooling pinned locally (D-09 default: .NET 11)
- [x] Tenant + legal-entity context middleware; server-side scoping in every handler (mock store)
- [x] Auth stub + memberships/RBAC (token = user id; real IdP when D-13 chosen)
- [ ] RLS policies + isolation tests — deferred until DB reachable (D-10); service-layer scoping meanwhile
- [x] Audit log (append-only) + evidence accessor (`GET /api/v1/audit-events`)
- [x] Outbox event envelope v1 (writes on approve/receipt); inbox consumer pending
- [x] Response envelope + global error middleware + validation pipeline
- [~] i18n scaffold: dictionaries en+bn, locale routing (web agent building)
- [ ] EF Core migration conventions + CI check — blocked on DB (D-10)
- [x] Telemetry: correlationId middleware end-to-end
- [x] Seed: one tenant, one entity, 3 users (admin/procurve/finance), 3 roles

## Phase 2 — Pilot slice (Wave 1 flows)
- [x] Procure-to-receive API: suppliers → PO → submit → maker-checker approval → receipt → stock + balanced AP accrual journal — 3/3 tests green
- [x] Journal reversal with linked provenance (`/{id}/reversals`)
- [~] Procure-to-receive UI (web agent in progress)
- [ ] Order-to-fulfill: customers → order → approval → stock allocation → shipment → AR invoice draft
- [x] Audit evidence visible for every step (`GET /api/v1/audit-events`)
- [x] Flow test (whole-chain coverage) + validation + authz tests

## Phase 3 — Core transactional domains (Wave 2, per domain — order by D-01/D-02)
- [ ] FINANCE deep: AP/AR close, periods/locks, tax (**D-01**)
- [ ] INVENTORY deep: valuation, counts, transfers
- [ ] PROCUREMENT deep: contracts, sourcing
- [ ] SALES deep: CRM, billing, dunning
- [ ] ASSETS: projects, service, fixed assets
- [ ] PEOPLE: workers, time, payroll handoff (**b D-05**)
- [ ] MFG: planning, production, quality (**b D-02**)

## Phase 4 — Scale & breadth (Wave 3)
- [ ] Jurisdiction/language packs (**b D-07**), tenant placements, read models/partitioning (**b D-08**)
- [ ] Upgrade/DR/event-replay rehearsals; compatibility policy (**b D-03, D-06, D-14**)

## Done-log
| Date | Step | Evidence |
|---|---|---|
| 2026-11-23 | Scaffold: CLAUDE.md, docs 01–06, .env/.gitignore/.cursorrules, snapshot | this file, branch `dev-sayeem` |
| 2026-11-23 | API foundation + procure-to-receive slice, 3/3 tests green | `apps/api`, `apps/api-tests` — `dotnet test` Passed 3/3 |
