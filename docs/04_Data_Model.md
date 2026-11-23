# 04 — Data Model (skeleton)

> Status: SKELETON. Source: `ERP-POSTGRES` cards + integration note §4. Tenancy model is **D-04** — foundation below is valid under any option; tenant scoping columns required regardless.

## 1. Foundation (Phase 1, before any domain)
| Entity | Notes |
|---|---|
| `tenants` | root isolation boundary |
| `legal_entities` | belongs to tenant; jurisdiction, accounting basis, currencies (**D-01**) |
| `users` / `memberships` / `roles` / `role_permissions` | IdP identity ≠ membership (spec assumption #6); RBAC per tenant+entity |
| `audit_log` | append-only; who/what/when/before/after, tenant-scoped; no updates ever |
| `outbox` / `inbox` | transactional event relay; versioned envelopes, idempotency keys |
| `document_sequences` | per entity/document-type numbering (ERP-FINANCE-016 pattern) |
| `configuration_versions` | effective-dated config with controlled release (assumption #12) |

Every business table carries: `tenant_id`, `legal_entity_id` (where applicable), `created_by/at`, `updated_by/at`, row version. **All FKs indexed.**

## 2. Domain entity skeletons (top level only — expand per slice from spec cards)
| Domain | Entities (seed list) |
|---|---|
| FINANCE | chart_of_accounts, dimensions, fiscal_calendars, periods, currencies, fx_rates, journals, journal_lines, tax_codes, ap_invoices, ar_invoices, payments, receipts |
| PROCUREMENT | suppliers, supplier_contracts, requisitions, purchase_orders, po_lines, receipts, supplier_invoices |
| SALES | customers, quotes, sales_orders, order_lines, shipments, invoices, credit_notes, receipts |
| INVENTORY | items, item_variants, uoms, warehouses, locations, stock_levels, stock_movements, lot_serials, counts, shipments |
| MFG | bom/routings, production_orders, work_centers, quality_specs, inspections, traceability_links |
| PEOPLE | workers, positions, time_entries, pay_runs (handoff to external payroll provider, **D-05**) |
| ASSETS | projects, project_tasks, service_cases, fixed_assets, asset_depreciation_schedules |
| PLATFORM | master_data_registry, workflows, workflow_instances, reports, dashboards, jobs |
| I (i18n) | translation_keys, translation_values, locale_policies, format_profiles |

Money: exact decimal `numeric(p,s)` + currency code — never float. Posted documents immutable; corrections via linked reversal/adjustment with provenance (assumption #5).

## 3. Rules
- Migration per change, never edit applied ones (CLAUDE.md DB rules).
- Migrations forward-only + tested down-rebuild path via seed/migration replay.
- PG major version/extension allowlist = **D-10**; partitioning decisions deferred to Wave 3 (spec Wave 3).
- No cross-tenant FK or query path, ever. RLS policies defense-in-depth on tenant tables (policy set finalized with **D-04**).
