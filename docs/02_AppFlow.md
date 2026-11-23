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

## 4. Per-module screens (to fill when the slice is built)
Each domain gets: list (filter/sort/bulk), detail/360, new/edit form, approval queue, audit/history drawer. Screen inventory added to this file per `06_Plan.md` phase.
