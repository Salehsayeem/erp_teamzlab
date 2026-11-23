# 02 — App Flow & Screen State Machine (skeleton)

> Status: SKELETON. Routes are Next.js App Router paths, locale-prefixed (`/[locale]/…`) per spec chapter 09. RTL layouts mirror automatically via CSS logical properties.

## 1. Global rules
- Every screen renders 4 states: **loading / empty / error / success** (see 03_UI_Spec tokens).
- Auth-gated: unauthenticated → redirect to `/login?next=<path>`.
- Tenant + entity context chosen at login; shown in top bar; every API call carries it.
- Deep links preserve tenant/entity/locale. Back never loses unsaved form data without confirm.

## 2. Route map (module level — screens added per built slice)
```text
/login                          # IdP auth (D-13: provider TBD)
/[locale]/select-entity         # tenant > legal entity picker
/[locale]/dashboard             # role-aware home
/[locale]/finance/…             # journals, AP, AR, periods, tax, close
/[locale]/procurement/…         # suppliers, requisitions, POs, receipts
/[locale]/sales/…               # customers, quotes, orders, invoices
/[locale]/inventory/…           # items, stock, warehouses, shipments, counts
/[locale]/manufacturing/…       # planning, production, quality
/[locale]/people/…              # workers, time, payroll
/[locale]/projects/…            # projects, service, assets
/[locale]/admin/…               # users, roles, entities, config, audit
/[locale]/settings/…            # locale, currency, notification prefs
```

## 3. Pilot flows (state machines — Wave 1)

### 3.1 Procure-to-receive
```text
[Supplier list] --new--> [Supplier form] --save--> [Supplier 360]
[PO list] --new--> [PO form: lines, tax, terms] --submit--> [PO: pending approval]
  --approve (maker-checker)--> [PO: approved] --send--> [PO: sent]
[Receipt] --against PO--> [Receipt form: qty/lot] --post--> stock updated + AP accrual
  --discrepancy--> [Exception: over/short/damage] --resolve--> [Receipt: posted w/ evidence]
```
Error paths: approval rejection → back to draft with reason; posting failure → inline error, draft preserved; duplicate receipt → blocked with link to original.

### 3.2 Order-to-fulfill
```text
[Customer list] --new--> [Customer form] --save--> [Customer 360]
[Order list] --new--> [Order form: lines, ATP check] --submit--> [Order: pending approval]
  --approve--> [Order: confirmed] --allocate stock--> [Order: released]
[Shipment] --pick--> [Pick list] --pack--> [Ship confirm] --> stock relieved + AR invoice draft
[Invoice] --approve--> [posted AR invoice] --payment--> [receipt allocation]
```
Error paths: insufficient stock → backorder/alternate-warehouse choice; credit hold → blocked with AR aging link; partial shipment allowed per order terms.

## 4. Per-module screen inventory (from `Requirement plan/spec_snapshot.md` card titles)
Each domain still gets the standard list (filter/sort/bulk), detail/360, new/edit form, approval queue, audit/history drawer states; the table below names the distinct screens per module. Card IDs cite `spec_snapshot.md` headings verbatim (ranges are inclusive). Snapshot prefix for i18n is `ERP-I18N-`; `ERP-SECURITY-` uses 2-digit numbering. Tech domains (`DOTNET`, `POSTGRES`, `NEXTJS`, `DELIVERY`) are mostly engineering-practice cards — only the operator/admin surfaces they imply are listed. Count per row feeds Phase 3 in `06_Plan.md`.

| Module | Key screens | Primary card IDs |
|---|---|---|
| FINANCE (20) | Chart of accounts & dimensions, fiscal calendars & period status/lock, currency & FX-rate master, journal entry + approval queue (+ reversal, recurring templates), subledger→GL reconciliation, AP invoice capture & matching (dup control, supplier adjustments), payment proposal → approval → execution status, AR invoice & credit-note issuance, receipt allocation & unapplied cash, AR aging & dunning workbench, tax registry & jurisdiction profiles (exemption evidence, withholding), tax return workpapers & liability reconciliation, e-invoice & tax-audit export monitors, asset register & depreciation runs, bank statement import & reconciliation, FX revaluation & consolidation eliminations, cash & budget-vs-actual dashboards, financial statement designer + drill-through, period close checklist dashboard, finance admin (sequences, SoD rules, audit evidence export) | ERP-FINANCE-001/003, 004/005, 006/008, 009/015, 016, 017, 018, 020/022, 023/026, 027/031, 032/034, 035/042, 037/038, 043/045, 047/049, 050/059, 060/061, 062/063, 064/070 |
| PROCUREMENT (12) | Supplier 360 + onboarding/evidence/risk/screening, category taxonomy & purchasing units, requisition intake + approval-matrix queue (delegation, SoD, anti-splitting), sourcing event workspace (eligibility, invitations, sealed bids, Q&A/addenda), bid evaluation & scoring trace, award decision (split awards, BAFO), contract workspace (amendments, obligations/milestones, renewals), PO form + change control + acknowledgement (blanket/call-offs), dispatch/ASN & goods receipt with tolerance, inspection/nonconformance/returns & supplier claims, supplier scorecards & corrective actions, spend & sourcing analytics | ERP-PROCUREMENT-005/009, 040/048, 002/004, 011/021, 022/029, 030/032, 033/035, 036/039, 049/056, 057/059, 061/062, 063 |
| SALES (14) | Customer 360 & hierarchy (dup detection, party merge, onboarding), lead capture & qualification, CRM timeline & cases, opportunity pipeline & forecast snapshots, quote builder (immutable revisions, approval, acceptance evidence), price books & discounts (contracted pricing), sales order list/form (holds, amendments, backorders, recurring, bundles), invoice assembly → posting (numbering, milestone billing), e-invoice adapter & delivery tracking, returns/RMA & refunds (credit/debit notes), receipt allocation & AR reconciliation, credit exposure & collections (reminders, disputes), rebate & commission claims, sales analytics | ERP-SALES-001/002, 005/008, 003/004, 015/020, 021/023, 024/026, 031/041, 042/045, 046/047, 050/051, 061/062, 054/056, 057/059, 063/065 |
| INVENTORY (16) | Item master, variants & UoM (packaging hierarchy), warehouse/bin layout & capacity, stock ledger & as-of stock view (status, custody, entity attribution), reservations & allocation, inbound receiving (appointments, ASN discrepancy, scan, partial), putaway & quarantine (+ returns receiving), outbound waves & replenishment (pick strategies, short/split pick, pack, staging/dock), handheld task list (offline sync), cycle count & variance approval, lot/serial control & genealogy/recall trace, quality inspection & disposition, shipment planning & carrier tracking (consolidation, POD, transport units), reverse logistics, costing & valuation (+ landed cost), inventory→finance reconciliation & expiry/alert queues | ERP-INVENTORY-001/004, 005/006, 007/011, 012/013, 021/024, 025/027, 031/036, 037/038, 039/040, 041/045, 046/047, 051/058, 059, 016/018, 019, 067/068 |
| MFG (14) | Production order lifecycle (split/merge, release gate, close/residuals), BOM/recipe editor & revisions (ECM, alternates, co-products, phantoms), routings & work centers, scheduling board (finite/infinite, scenarios/freeze), dispatch list & capacity load, MRP run & planned orders (action messages, fences), shop-floor reporting (operations, material issue/backflush, labor, counters, downtime), WIP projection & transfers, subcontract operations (custody, receipt), quality plans & inspections (sampling, signed results), NCR/CAPA workflow, genealogy & trace queries, scrap & rework, MFG close reconciliation & cost handoff (+ maintenance coordination) | ERP-MFG-007/013, 014/023, 024/025, 027/030, 031/032, 033/040, 041/049, 050/052, 055/058, 059/063, 064/066, 067/068, 069/070, 053/054 |
| PEOPLE (15) | Worker directory & profiles (ESS updates), org hierarchy & positions/headcount, employment lifecycle & onboarding tasks, job catalog & compensation history, work schedules & calendars, time capture + correction/approval queue, leave requests/balances/accruals (+ attendance exceptions), payroll run dashboard (validation → preview → approval/lock), gross-to-net & retro/off-cycle runs, earning/deduction codes + tax elections + garnishments + benefits, payment file handoff & payslips, statutory reporting & payroll→GL reconciliation, payroll corrections & historical inquiry, training & credentials, workforce plan & people analytics | ERP-PEOPLE-004/016, 002/008, 005/007, 022/024, 025, 026/027, 029/033, 046/048, 038/040, 037/044, 050/051, 052/054, 055/056, 058, 059/061 |
| ASSETS (15) | Project list, WBS & schedule baselines, project budget & cost control (commitments, change requests), resource assignments & time/expense capture (timesheet approval), project billing (milestone/T&M) & revenue handoff, project closure, service catalog & entitlements, installed-base asset register, service case queue + SLA tracking, dispatch board & work orders, field capture (parts/labor/expense) + offline sync, preventive-maintenance schedules, fixed-asset register, books & CIP capitalization, depreciation runs & posting, asset transfer/count/impairment/disposal, leases/insurance & register reconciliation | ERP-ASSETS-001/005, 006/014, 007/010, 015/017, 019, 021/025, 023/028, 029/030, 031/036, 040, 041/045, 046/048, 051/056, 057/059 |
| PLATFORM (12) | Tenant & org hierarchy admin (lifecycle, config isolation), reference settings (fiscal calendars, time zones, currencies, numbering), IdP/MFA & session admin, membership & role/permission catalog (SoD, elevation), service identities & privileged access, master-data registry (lifecycle, dup/merge, effective dating, provenance), workflow designer & routing rules (thresholds, escalation, delegation, parallel branches), task inbox, report catalog & scheduled/async delivery, locale/language preferences & RTL, audit search, retention & legal hold, extension/jurisdiction config & module registry | ERP-PLATFORM-009/013, 014/017, 018/019, 020/026, 027/036, 037/046, 047, 048/056, 057/064, 065/068, 069/070 |
| I (ERP-I18N) (7) | Supported-locale registry, user language/format switcher, translation catalog & workflow states, missing-translation & quality dashboards, RTL/layout & font coverage checks, document/email template localization, localized import/export profiles | ERP-I18N-002, 003/005, 011/016, 017/018, 041/049, 052/055, 059 |
| DOTNET (4) | Health/readiness status, tenant-scoped feature-flag admin, security-audit event viewer, trace/metrics dashboards | ERP-DOTNET-069, 062, 067, 068 |
| POSTGRES (3) | Capacity/slow-query/vacuum dashboards, backup/restore drill evidence, tenant export & erasure workflow | ERP-POSTGRES-065/068, 056/057, 052 |
| NEXTJS (5, cross-cutting components) | Consequential-action confirmations, bounded bulk-selection/bulk-action UX, language switcher, session-timeout warning & recovery, notification center | ERP-NEXTJS-020, 036, 038, 067, 068 |
| INTEGRATION (8) | Connector registry & lifecycle, field-mapping editor + transformation sandbox, mapping validation & quarantine queue, webhook subscription admin, dead-letter & replay console, bulk job monitor + import preview/dry-run, reconciliation & drift dashboard, integration health/dependency status | ERP-INTEGRATION-031/032, 033/034, 038, 048, 047, 053/066, 067, 069 |
| SECURITY (12) | Role & permission administration, privileged elevation & re-auth, session/device revocation, security audit access & export, consent & preference records, data-subject request workflow, data classification & residency policy, retention-by-record-class config, incident/breach workflow, compliance profiles & evidence export, vulnerability/dependency management, tenant exit & data disposal | ERP-SECURITY-21/24, 25/26, 18/31/33, 41/42, 47, 48/50, 52/53, 54, 56, 57/67, 62, 70 |
| DELIVERY (4) | Operational dashboards, alert routing & incident/on-call workflow, runbook & recovery library, release health gate & rollback signals | ERP-DELIVERY-058, 059/063, 064, 048/051 |
| IMPLEMENTATION (10) | Governance workbench (charter, RAID, decision log), fit-to-standard disposition tracker, configuration package & drift dashboard, migration dashboard (scope, mappings, profiling, rehearsals), load reconciliation & completeness reports, UAT sign-off tracker + defect triage, cutover runbook & go/no-go readiness, hypercare & support handover, training curriculum & adoption indicators, stakeholder communication planner | ERP-IMPLEMENTATION-001/007, 005, 011/014, 021/028, 031/033, 039/042, 043/044, 046, 051/055, 047/050 |

Total: 171 screens across 16 modules. Every screen above traces to the cited card titles; nothing here adds requirements beyond `spec_snapshot.md`.
