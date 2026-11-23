# erp_teamzlab — International Multilingual ERP

A configurable, multi-entity ERP for companies that operate across countries and languages.
Backend: ASP.NET Core (.NET 11) modular-monolith API. Database: PostgreSQL via EF Core.
Frontend: Next.js App Router with first-class i18n (English + Bengali today, RTL-ready).
The 1,120-requirement product spec drives every slice; each domain card in the spec maps to
a tracked screen and endpoint in this repo.

## Why this project exists

Most open and mid-market ERP systems treat language, jurisdiction, and legal-entity structure
as afterthoughts. This project starts from the opposite premise:

- **International by default.** Multi-tenant, multi-legal-entity data model with
  tenant + entity scoping enforced server-side on every query — not bolted on per report.
- **Multilingual and RTL-first.** Every user-facing string flows through the i18n layer
  (en + bn dictionaries shipped); Arabic/Urdu-style RTL layouts are a routing concern, not a rewrite.
- **Accessible.** WCAG 2.2 AA is a build gate, not an audit pass at the end.
- **Auditable money.** Every posted fact (journals, receipts, shipments, stock moves) is
  tenant-scoped, idempotent, and traceable through an append-only audit log and an outbox.

## Feature domains

FINANCE · PROCUREMENT · SALES · INVENTORY · MFG · PEOPLE · ASSETS · PLATFORM — each with
sub-modules covering the full spec (e.g. procure-to-receive, order-to-fulfill, inventory
valuation/counts/transfers, periods and locks, fixed assets, payroll handoff).

Current delivery status lives in [docs/06_Plan.md](docs/06_Plan.md) (living checklist).

## Tech stack

| Layer | Choice |
|---|---|
| API | ASP.NET Core, .NET 11 (`net11.0`), layered Controller → Service → Repository |
| Database | PostgreSQL 19 (candidate), EF Core, migrations only forward |
| Frontend | Next.js App Router (React Server Components by default), TypeScript strict |
| i18n | Dictionary-based (en/bn), locale-prefixed routes |
| Auth | Stub token auth now (token = seeded user id); IdP swap deferred (decision D-13) |

## Project structure

```
apps/
  api/            ASP.NET Core API
    Controllers/  HTTP endpoints ([ApiController], no business logic)
    Dtos/         request/response shapes (validated before business logic)
    Models/       domain records + enums
    Interfaces/   service contracts
    Services/     business logic implementations
    Data/         DbContext, repositories, seed
  web/            Next.js frontend
    app/          routes (locale-prefixed)
    components/   UI
    services/     API client layer (no raw fetch in components)
  api-tests/      API integration tests (xUnit)
docs/             PRD, app flow, UI spec, data model, API spec, plan (01–06)
Requirement plan/  spec_snapshot.md — searchable snapshot of the 1,120-requirement spec
```

## Run it

### Prerequisites

- .NET 11 SDK (`winget install Microsoft.DotNet.SDK.11`)
- Node 20+ and npm
- PostgreSQL 15+ reachable from your machine

### 1. Configure the database

```bash
cp .env.example .env   # then fill PGHOST, PGPORT, PGDATABASE, PGUSER, PGPASSWORD
```

`.env` is gitignored — credentials never enter the repo.

### 2. Backend

```bash
dotnet run --project apps/api
# API listens on the URL printed by Kestrel (default http://localhost:5000)
```

Demo seed (created automatically): tenant `T-001`, legal entity `LE-001` (TeamZlab Ltd, BD, BDT),
users `admin@teamzlab.dev` / `procurve@teamzlab.dev` / `finance@teamzlab.dev` / `sales@teamzlab.dev`.
Stub auth: POST `/api/v1/auth/token-exchange` with an email → token (= user id) →
`Authorization: Bearer <token>`.

### 3. Frontend

```bash
cd apps/web
npm install
npm run dev
```

### 4. Tests

```bash
dotnet test apps/api-tests      # API integration suite (incl. maker-checker + negative paths)
cd apps/web && npm run build    # frontend build gate
```

Every response uses one envelope:

```json
{ "success": true, "data": { } }
{ "success": false, "error": { "code": "ERROR_CODE", "message": "Human-readable message" } }
```

## Git workflow

```
main            ← production-ready snapshots only
dev-sayeem      ← integration branch; full site regression runs here
feat/… fix/…    ← one feature or bug per branch, feature tests green on the branch
```

Flow: feature branch → tests green → merge into `dev-sayeem` → full regression
(backend suite + web build + endpoint smoke) → all green → merge into `main`.
Conventional Commits (`feat:`, `fix:`, `chore:`, `test:`, `refactor:`).

## Documentation map

| Doc | Contents |
|---|---|
| [docs/01_PRD.md](docs/01_PRD.md) | Product requirements |
| [docs/02_AppFlow.md](docs/02_AppFlow.md) | User flows + 16-module screen inventory |
| [docs/03_UI_Spec.md](docs/03_UI_Spec.md) | Screen-level UI spec |
| [docs/04_Data_Model.md](docs/04_Data_Model.md) | Data model |
| [docs/05_API_Spec.md](docs/05_API_Spec.md) | API contract (envelope, endpoints, errors) |
| [docs/06_Plan.md](docs/06_Plan.md) | Living delivery checklist |
| `Requirement plan/spec_snapshot.md` | Full 1,120-card spec, searchable |
