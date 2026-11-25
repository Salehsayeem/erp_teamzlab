# 07 — QA & Verification Blueprint (four-role gates)

> From the workspace-enhancement request (`Requirement plan/enhanced_request.txt`, 2026-11-25).
> Adapted to repo truth: **.NET 11** (`net11.0`), **Next 16** (npm, not pnpm), dev **PostgreSQL :5433**
> (version qualification still open — D-10). Prescribed-but-not-yet items are listed as **gaps**,
> not claimed as done.

## Gate 1 — Senior Architect (boundaries & contracts)

| Target | Anchor | Here & now | Gap |
|---|---|---|---|
| Modular monolith isolation | ERP-DOTNET-008 | Layered `apps/api` (Controllers→Services→Repositories, `AddErpServices` DI pin) | NetArchTest boundary test that fails the build on cross-module infra reach |
| Contract stability | ERP-PLATFORM-005 | Swashbuckle OpenAPI; typed DTOs; smoke asserts response shapes | OpenAPI breaking-change diff + consumer-driven contract tests |
| Build provenance | ERP-DOTNET-005 | Pinned tool manifest (`dotnet-ef`), committed locks (package-lock) | Reproducible dual-build digest check |

## Gate 2 — Senior Backend Engineer (.NET)

| Target | Anchor | Here & now | Gap |
|---|---|---|---|
| Immutability & reversals | ERP-FINANCE-014 / INVENTORY-015 | Suite covers posted-record guards + linked reversals | — |
| Authorization & SoD matrix | ERP-DOTNET-024–025 / PROCUREMENT-016 | Per-endpoint RBAC tests incl. self-approval rejection | Full role×endpoint matrix table test |
| Atomicity & idempotency | ERP-DOTNET-041 / SALES-052 | Atomic idempotency store (concurrent winner replay), outbox writes, fault tests in-suite | Mid-command fault injection on every posting path |
| Rate limits & secret hygiene | ERP-DOTNET-029–030 | No secrets in source; telemetry carries correlationId only | Multi-tenant 429 isolation load test; automated log/secret scan |

## Gate 3 — Senior Database Engineer (PostgreSQL)

| Target | Anchor | Here & now | Gap |
|---|---|---|---|
| Real-engine integration | ERP-DOTNET-039 | EF migrations + full suite against dev PG :5433 (`TESTPG*`) | Testcontainers matrix across PG majors once version is qualified (D-10) |
| Tenant keys & RLS | ERP-DOTNET-033 / 035 | Composite tenant keys in schema; service-layer scoping | Catalog tests + pooled-connection RLS leak tests (deferred with D-04/D-10) |
| Migration safety | ERP-PLATFORM-004 | Applied migrations immutable; new migrations only | Checksum-negative test; expand/contract lint for locking DDL |
| Telemetry & vacuum | ERP-POSTGRES-065–067 | CorrelationId end-to-end | EXPLAIN fingerprint triage; autovacuum / TX-ID age monitoring |

## Gate 4 — Senior Frontend Engineer (Next.js)

| Target | Anchor | Here & now | Gap |
|---|---|---|---|
| Type & RSC boundary safety | ERP-NEXTJS-002–004 | TS strict, `tsc --noEmit` gate (post-edit hook), narrow `"use client"` | ESLint rule banning client-side env/secret reads |
| Tenant-safe reads | ERP-NEXTJS-010–012 | All reads server-side via API services layer | Multi-tenant cached-payload cross-render test |
| Visual / RTL / responsive fidelity | ERP-I18N-* / INVENTORY-070 | Browser-verified en+bn at 1440 / 390 px per wave; dict key-parity compile check | Scripted screenshot regression across locales/viewports |
| Accessibility & keyboard | ERP-PLATFORM-062–063 | Uikit: focus trap, roving tabs, aria-live toasts, AA dot/border badges | `@axe-core/playwright` audit + keyboard-only flow test in CI |

## Pipeline (run order — the standing regression)

```bash
# Backend (restore once; afterwards the post-edit hook keeps apps/api green)
dotnet build apps/api && dotnet test apps/api-tests

# Frontend (typecheck + route/build gate)
cd apps/web && npx tsc --noEmit && npm run build && cd ../..

# Endpoint smoke vs dev PG :5433 (45–46 assertions; see scripts/verify-all-features.sh)
bash scripts/verify-all-features.sh
```

All three green = dev-sayeem regression passed → eligible for `main` (owner gate, CLAUDE.md flow).
Post-edit automation: `.claude/hooks/verify-after-edit.sh` (fires on C#/TS edits only).
Prerequisites for the Claude tooling itself: `scripts/bootstrap-claude-erp.sh`.
