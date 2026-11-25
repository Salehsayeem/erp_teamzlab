---
description: EF Core / Npgsql data layer — tenant keys, query filters, migration discipline, telemetry hygiene
paths:
  - "apps/api/Data/**"
  - "apps/api/Migrations/**"
---

# Data layer (ERP-DOTNET-033..041 / ERP-POSTGRES-*)

- Every tenant-owned table carries a non-null `tenant_id` inside its primary/alternate
  keys; index every FK and frequent query column.
- EF Core global query filters scope every tenant-owned DbSet; service-layer scoping
  stays regardless (RLS is defense-in-depth — decision D-04).
- When RLS lands: transaction-local `SET LOCAL` context per unit of work (pooling-safe);
  the runtime role must be non-owner without BYPASSRLS (ERP-DOTNET-035).
- Applied migrations are immutable — always add a new one; never `DROP TABLE`/`TRUNCATE`
  without explicit user confirmation; multi-step writes in one transaction.
- Never log bind parameters or secrets (ERP-POSTGRES-065); connection info only via
  env vars (`TESTPG*` for tests, `PG*` runtime — see README).
