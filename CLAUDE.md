# CLAUDE.md — erp_teamzlab

## What this is
International Multilingual ERP — configurable, multi-entity ERP. Spec baseline:
**ASP.NET Core (.NET 11) API + PostgreSQL 19 (candidate) + Next.js App Router frontend** (modular monolith, tenant-scoped, WCAG 2.2 AA, first-class i18n/RTL).

**Source of truth:** `Requirement plan/spec_snapshot.md` (2.3 MB markdown snapshot of the 1,120-requirement spec).
- NEVER read the original docx/pdf — Grep/Read the snapshot: `grep "## ERP-FINANCE-014" "Requirement plan/spec_snapshot.md"`
- 1,120 cards, 70 × 16 domains: FINANCE, PROCUREMENT, SALES, INVENTORY, MFG, PEOPLE, ASSETS, PLATFORM, I (i18n), DOTNET, POSTGRES, NEXTJS, INTEGRATION, SECURITY, DELIVERY, IMPLEMENTATION

**Before any task:** read `docs/01_PRD.md` → `06_Plan.md`. After completing any step, update `docs/06_Plan.md` status.

## Coding standards

### General
- TypeScript strict mode in frontend (`any` never — narrow `unknown`). C#: `<Nullable>enable</Nullable>`, no bare `object` where a type fits.
- Functions small and focused, ~40 lines max where practical. Early returns over nested if/else.
- No new npm/NuGet packages without stating why and getting approval first.
- If a requirement is ambiguous, ask — don't guess. Before a complex feature, present the plan and wait for confirmation.

### Frontend (Next.js App Router)
- Functional components + custom hooks for reusable logic. Server Components by default; Client Components narrow and deliberate.
- Every screen handles loading / empty / error / success explicitly.
- Forms: client-side validation + accessible labels (`htmlFor`, `aria-label`).
- API calls live in a `services/` layer — no raw `fetch()` inside UI components.
- Responsive across mobile / tablet / desktop. All user-facing strings go through the i18n layer — no hardcoded copy.

### Backend (ASP.NET Core)
- Layered: Controller → Service → Repository. Business logic never lives in controllers.
- Validate every incoming body/param/query **before** business logic touches it (FluentValidation or DataAnnotations; frontend mirrors with Zod).
- Consistent JSON envelope everywhere:
  - Success: `{ "success": true, "data": ... }`
  - Error: `{ "success": false, "error": { "code": "ERROR_CODE", "message": "Human-readable message" } }`
- Centralized exception-handling middleware. Stack traces never reach production responses.
- Commands/events: versioned, tenant-scoped, idempotent, traceable. Outbox/inbox for externalized side effects (spec assumption #4).

### Database (PostgreSQL via EF Core)
- Never modify an applied migration — always add a new one.
- Index every foreign key and frequent query column.
- Wrap multi-step writes that must succeed/fail together in one transaction.
- NEVER run `dotnet ef database drop`, `DROP TABLE`, `TRUNCATE`, or any destructive command without explicit user confirmation.
- Connection info lives in `.env` (gitignored) — never hardcode credentials.

## Security rules
- AuthN **and** RBAC/ownership verified on every protected endpoint. Never trust a userId from the client body — extract from the verified session/token/claims.
- Tenant + legal-entity scope enforced server-side on every query (RLS is defense-in-depth, not the only boundary).
- Parameterized queries only (EF Core / Npgsql) — never concatenate user input into SQL.
- Never `dangerouslySetInnerHTML` without sanitizing input first.
- Strict CORS origins, security headers, rate limiting on auth and public write endpoints.
- Never return password hashes, tokens, or sensitive PII — explicit selects/projections only.

## Testing
- TDD for new backend endpoints and core utilities: failing test first, then implement.
- Every new API endpoint has tests. Posted/posted-like flows (journals, payments, inventory) need approval-path tests too.

## Git & workflow (branch tiers)
- Default branch: `main` — production-ready snapshots only.
- Long-running integration branch: `dev-sayeem` — feature work merges here first.
- One feature/bug per branch: `feat/…`, `fix/…`, `chore/…`. Conventional Commits (`feat:`, `fix:`, `chore:`, `test:`, `refactor:`).
- Flow (owner instruction, 2026-11-24): feature branch → feature tests green on that branch → merge into `dev-sayeem` → **full site regression** (backend test suite + web build + endpoint smoke) → all green → merge into `main`.
- Never merge into `main` without a passing dev-sayeem regression.

## Style plugins (use when producing prose or UI)
- `/ponytail` — least code that works (active session-wide)
- `/unslop`, `/humanizer` — natural human prose for docs/UI copy
- `/hallmark` — doc quality bar
- `/impeccable`, `/ui-ux-pro-max` — design/UI build quality (apply when building screens and 03_UI_Spec work)
