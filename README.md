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
| --- | --- |
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

| Tool | Version | Install (Windows) |
| --- | --- | --- |
| .NET SDK | 11 (`net11.0`) | `winget install Microsoft.DotNet.SDK.11` |
| Node.js + npm | 20.9+ (LTS) | `winget install OpenJS.NodeJS.LTS` |
| PostgreSQL | 15+ (19 per spec baseline) | `winget install PostgreSQL.PostgreSQL.17` — or any reachable server |

Verify: `dotnet --list-sdks`, `node --version`, `psql --version`.

### 1. Configure environment

The repo-root `.env` feeds the API — a dependency-free loader reads it at startup
(`apps/api/Common/EnvLoader.cs`); real environment variables always win over file values.

```bash
cp .env.example .env   # then fill PGHOST, PGPORT, PGDATABASE, PGUSER, PGPASSWORD
```

`.env` is gitignored — credentials never enter the repo.

Two different servers, on purpose:

- **`PG*` → the API's dev database** (EF connection name `Default`). May be remote.
- **`TESTPG*` → the test suite's server.** Tests create and drop throwaway
  `erp_test_<hex>` databases and need `CREATEDB` rights on `localhost:5432`
  (`postgres`/`postgres` by default). Override with **real environment variables**
  (`TESTPGHOST`, `TESTPGPORT`, `TESTPGUSER`, `TESTPGPASSWORD`) — not `.env`:
  the test fixtures read credentials before the `.env` loader runs.

### 2. Restore packages and EF tooling

```bash
dotnet tool restore     # dotnet-ef, pinned in .config/dotnet-tools.json
dotnet restore          # API + test projects
```

### 3. Database schema (once, and after every EF model change)

The API boots lazily — Npgsql connects on first request, never at startup — so the schema
must be applied explicitly with the EF CLI. Run from the repo root so the `.env` loader
can resolve the connection:

```bash
# apply the committed InitialSchema migration to the PG* database from .env —
# double-check PGHOST before running
dotnet ef database update --project apps/api
```

The schema is additive (19 tables, sequences, seeded reference data); nothing is dropped.
When the EF model changes later, add a migration first —
`dotnet ef migrations add <DescriptiveName> --project apps/api` — and never edit an
already-applied one (CLAUDE.md: forward-only migrations).

### 4. Backend

```bash
dotnet run --project apps/api
# API listens on the URL printed by Kestrel (default http://localhost:5000)
```

Demo seed (migration-seeded): tenant `T-001`, legal entity `LE-001` (TeamZlab Ltd, BD, BDT),
users `admin@teamzlab.dev` / `procurve@teamzlab.dev` / `finance@teamzlab.dev` / `sales@teamzlab.dev`.
Stub auth: POST `/api/v1/auth/token-exchange` with an email → token (= user id) →
`Authorization: Bearer <token>`.

Smoke test:

```bash
curl -X POST http://localhost:5000/api/v1/auth/token-exchange \
  -H "Content-Type: application/json" -d '{"email":"admin@teamzlab.dev"}'
curl http://localhost:5000/api/v1/suppliers -H "Authorization: Bearer <token-from-above>"
```

### 5. Frontend

```bash
cd apps/web
npm install
npm run dev        # http://localhost:3000
```

The browser client calls `NEXT_PUBLIC_API_BASE` (default `http://localhost:5000`, see
`apps/web/.env.example`). Cross-origin calls are allowed only for origins listed in the
API's CORS policy — set `CorsOrigins` (comma-separated) to extend the default
`http://localhost:3000`.

### 6. Tests

```bash
dotnet test apps/api-tests      # API integration suite (incl. maker-checker + negative paths)
cd apps/web && npm run build    # frontend build gate
```

The API suite needs the `TESTPG*` server from step 1 reachable — every test class creates
and drops its own throwaway database, so never point it at a server holding real data.

Every response uses one envelope:

```json
{ "success": true, "data": { } }
{ "success": false, "error": { "code": "ERROR_CODE", "message": "Human-readable message" } }
```

### Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| API boots fine, requests fail with `relation … does not exist` | Schema not applied to the `PG*` database → run step 3 |
| `dotnet ef` says "No database provider is configured" | Run from the repo root so `.env` is found, and pass `--project apps/api` |
| Test suite can't connect | No PostgreSQL on `TESTPGHOST` (default `localhost:5432`) — start the service or set `TESTPG*` env vars |
| Tests fail while creating databases | Test user lacks `CREATEDB` — `ALTER USER postgres CREATEDB;` on the test server |
| Browser console shows CORS errors | Add the frontend origin to the API's `CorsOrigins` env var, or serve the frontend on `http://localhost:3000` |
| 401 on every endpoint | Missing `Authorization: Bearer <token>` — get one from `/api/v1/auth/token-exchange` (step 4) |
| Port 5000/3000 busy | API: `ASPNETCORE_URLS=http://localhost:<port>`. Web: `npm run dev -- -p <port>` — then align `NEXT_PUBLIC_API_BASE` and `CorsOrigins` |

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
| --- | --- |
| [docs/01_PRD.md](docs/01_PRD.md) | Product requirements |
| [docs/02_AppFlow.md](docs/02_AppFlow.md) | User flows + 16-module screen inventory |
| [docs/03_UI_Spec.md](docs/03_UI_Spec.md) | Screen-level UI spec |
| [docs/04_Data_Model.md](docs/04_Data_Model.md) | Data model |
| [docs/05_API_Spec.md](docs/05_API_Spec.md) | API contract (envelope, endpoints, errors) |
| [docs/06_Plan.md](docs/06_Plan.md) | Living delivery checklist |
| `Requirement plan/spec_snapshot.md` | Full 1,120-card spec, searchable |
