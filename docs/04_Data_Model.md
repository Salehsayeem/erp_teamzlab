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
| Domain | Entities (seed list → card IDs, Wave-2 domains deepened; ranges inclusive) |
|---|---|
| FINANCE | chart_of_accounts (ERP-FINANCE-001/002), accounting_policy_assignments (017), dimensions (003), fiscal_calendars (004), periods (005), currencies (006/007), fx_rates (008), journals + journal_lines (009/010), journal_sources (011), recurring_journal_templates (012), journal_approvals (013), journal_reversals (015), document_sequences (016), subledger_reconciliations (018), supplier_tax_identities (019), ap_invoices (020/021), ap_invoice_duplicates (022), payment_proposals (023), payment_approvals (024), payment_executions (025), supplier_adjustments (026), ar_invoices (027), credit_notes (028), receipts (029/030), dunning_plans (031), tax_codes (032), tax_jurisdiction_profiles (033), tax_exemptions (034), tax_return_workpapers (036/041), withholding_tax (040), e_invoice_documents (037), fixed_assets (043), depreciation_schedules (044), asset_disposals (045), bank_statements (047), bank_reconciliations (048/049), fx_revaluations (050/051), consolidation_mappings (052), eliminations (054), cash_forecasts (056), budgets + commitments (057/058), cost_allocation_rules (059), financial_statement_definitions (060/061), close_checklists (062/063) |
| PROCUREMENT | suppliers (ERP-PROCUREMENT-005/006/009), supplier_onboarding_evidence (040/041), supplier_risk_profiles (042/043), supplier_qualifications (044), supplier_portal_users (046/047), purchasing_units (002), category_taxonomy (003), requisitions (011/012), budget_commitments (013), approval_matrices (014), approval_delegations (015), approval_decisions (017), sourcing_events (022/023), sourcing_invitations (024), bids (028/029), evaluation_criteria + evaluator_assignments (030/031), awards (033/034), supplier_contracts (036), contract_amendments (037), contract_obligations (038), contract_renewals (039), purchase_orders + po_lines (049/050/051), po_change_requests (052), order_acknowledgements (053), blanket_orders + call_offs (054), asns (056), receipts (057), inspections + nonconformances (058), returns_claims (059), supplier_invoices (060), supplier_scorecards (061/062) |
| SALES | customers (ERP-SALES-001/002), customer_hierarchies (012), leads (003/004), crm_interactions (007), crm_cases (008), sales_catalog (009/010), territories (011), opportunities (015/017), forecasts (018/020), quotes (021), quote_revisions (022), quote_approvals (023), price_books (024/026), sales_orders + order_lines (031/033), order_amendments (034), order_holds (035), backorders (037), reservations (038), recurring_orders (040), invoices (042/045), milestone_billing (047), e_invoices (050), invoice_deliveries (051), credit_notes (046/062), return_authorizations (061), receipts (054/055), credit_exposures (057), collections (058/059), rebates (063), commissions (064); shipments fulfilled via INVENTORY (ERP-INVENTORY-051/054), AR handoff per ERP-SALES-052 |
| INVENTORY | items (ERP-INVENTORY-001), item_variants (002), uoms (003), packaging_hierarchy (004), warehouses + locations (005/006), warehouse_calendars (010), stock_ledger (011), reservations (012/013), stock_movements (014/015), cost_layers (016/017), landed_costs (018), inbound_appointments (021), asn_discrepancies (022), receipts (023/024), putaway_tasks (025), quarantines (026), return_receipts (027), lot_serials (041/042), genealogy_links (044), recall_traces (045), count_sheets + variances (039/040), quality_inspections (046/047), waves + pick_tasks (031/033), handling_units (034), staging_dock_loads (035), replenishment_tasks (036), shipments (051/054), freight_statuses (055), carrier_tracking_events (057), transport_units (058), reverse_logistics_authorizations (059) |
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
