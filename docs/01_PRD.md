# 01 — Product Requirements Document (skeleton)

> Status: SKELETON — filled per-domain slice by slice from `Requirement plan/spec_snapshot.md`. Open decisions marked **D-xx** (see §7).

## 1. Problem & product
Configurable, internationally deployable, multi-entity ERP covering finance, procurement, sales, inventory/logistics, manufacturing/quality, people/payroll, projects/service/assets, plus platform, localization, security, integration, delivery, and implementation controls. One uniquely-identified testable requirement per card (1,120 total). Product baseline — not a claim of universal legal compliance.

## 2. Target personas (draft)
| Persona | Uses |
|---|---|
| Tenant admin | entities, users, roles, locale/currency/policy config |
| Finance officer | journals, AP/AR, periods, tax, close |
| Procurement officer | suppliers, requisitions, POs, receipts |
| Sales/CRM user | customers, quotes, orders, billing |
| Warehouse operator | receipts, putaway, picks, shipments, counts |
| Manufacturing planner | planning, production orders, quality, traceability |
| HR/payroll officer | workers, time, payroll handoff |
| Project manager | projects, service, assets |
| Platform/SRE | tenants, jobs, integrations, observability |

## 3. First build target (from spec Wave 1)
Bounded end-to-end pilot flows, finance posting controlled behind approvals:
- **Procure-to-receive** (ERP-PROCUREMENT + ERP-INVENTORY cards)
- **Order-to-fulfill** (ERP-SALES + ERP-INVENTORY cards)
- Foundation: tenant/entity context, auth, server-side authorization, audit, outbox/inbox, i18n scaffold (see `06_Plan.md` Phase 1)

## 4. MVP user stories (pilot slice — to expand per card)
1. As tenant admin I configure my entity, locales, currencies so operations run under my jurisdiction profile. *(D-01 pending)*
2. As procurement officer I create a supplier, raise a PO, receive against it, and see stock and AP impact — with maker-checker where configured.
3. As sales officer I create a customer, take an order, fulfill it, and invoice — stock and AR posting behind approval.
4. As finance officer I see every posting from 2–3 in a balanced journal view and can reverse with linked provenance.
5. As auditor I see immutable audit evidence for all of the above.

## 5. Success metrics (TBD — D-08)
Operational targets (users, SLOs, RPO/RTO) are owner decisions; propose after Phase 0.

## 6. Out of scope (non-goals — from spec §2)
- No universal legal/tax/payroll compliance claims; jurisdiction rules are configurable profiles (**D-01, D-05**).
- No hosting provider, region, tenancy topology, or residency choice baked in (**D-03, D-04, D-06**).
- No SLOs/scale commitments without service profiles (**D-08**).
- No MES vendor claim or universal industry qualification (**D-02**).

## 7. Decision register (D-01…D-14 — condensed)
| ID | Decision | Blocks |
|---|---|---|
| D-01 | Countries, legal entities, tax regimes, accounting basis | Domain design freeze |
| D-02 | Industry / regulated scope, manufacturing depth | Scope & estimates |
| D-03 | Hosting model/provider | Architecture freeze |
| D-04 | Tenancy model (RLS discriminator / schema / DB-per-tenant) | Data model + threat model |
| D-05 | Payroll jurisdictions & providers | Payroll build |
| D-06 | Data residency, retention, deletion | Data lifecycle design |
| D-07 | Language/locale catalog, RTL set | UX/content freeze |
| D-08 | Scale profile, SLOs, RPO/RTO | Sizing, SRE sign-off |
| D-09 | .NET 11 STS vs .NET 10 LTS | Implementation approval |
| D-10 | PG major/provider, extensions, Npgsql/EF versions | DB qualification |
| D-11 | Next.js/Node pins, hosting mode, BFF boundary | Build freeze |
| D-12 | Master-data system-of-record owners; Quality vs Mfg boundary | Contract freeze |
| D-13 | External providers (IdP, bank, tax, e-invoice, carrier…) | Integration testing |
| D-14 | Retention/privacy/security assurance per jurisdiction | Production readiness |

## 8. Domain capability map (16)
Finance (FINANCE) · Procurement (PROCUREMENT) · Sales/CRM/Billing (SALES) · Inventory/Warehouse/Logistics (INVENTORY) · Manufacturing/Quality (MFG) · HR/Payroll (PEOPLE) · Projects/Service/Assets (ASSETS) · Core Platform/Master Data/Workflow/Reporting (PLATFORM) · i18n/Localization (I) · .NET backend (DOTNET) · PostgreSQL (POSTGRES) · Next.js frontend (NEXTJS) · API/Integration/Eventing (INTEGRATION) · Identity/Security/Privacy (SECURITY) · Testing/DevOps/SRE (DELIVERY) · Implementation/Governance (IMPLEMENTATION)
