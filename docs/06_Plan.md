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
- [ ] Repo scaffold: `apps/api` (.NET), `apps/web` (Next.js), solution/tooling pins (**D-09/D-11**)
- [ ] Tenant + legal-entity context middleware; server-side scoping on every query
- [ ] IdP integration + memberships/RBAC (provider may be stubbed, **D-13**)
- [ ] RLS policies on tenant tables + isolation tests
- [ ] Audit log (append-only) + evidence accessor
- [ ] Outbox/inbox + event envelope v1 (05_API_Spec §3)
- [ ] Response envelope + global error middleware + validation pipeline
- [ ] i18n scaffold: locale routing, message catalogs, RTL-ready tokens (03_UI_Spec)
- [ ] EF Core migration conventions + CI check (no edited migrations)
- [ ] Telemetry: correlationId end-to-end, structured logs
- [ ] Seed: one tenant, one entity, admin user

## Phase 2 — Pilot slice (Wave 1 flows)
- [ ] Procure-to-receive: suppliers → PO → approval (maker-checker) → receipt → stock + AP accrual
- [ ] Order-to-fulfill: customers → order → approval → stock allocation → shipment → AR invoice draft
- [ ] Finance posting behind approval for both flows; balanced-journal validation; linked reversal
- [ ] Audit evidence visible for every step (02_AppFlow §3 flows end-to-end)
- [ ] Tests per endpoint (TDD) + cross-module contract tests

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
