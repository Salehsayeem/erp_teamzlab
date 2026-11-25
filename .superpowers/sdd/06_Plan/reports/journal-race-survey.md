# Journal-adjacent money path — race survey (feat/journal-race-hardening)

Baseline: dev-sayeem @ 83f6a19. House pattern from the money-hardening wave (1544883):
EF concurrency token on the state column (model-only) for single-row status flips, and
SELECT … FOR UPDATE inside the request transaction (joined, never nested, with Idem.Wrap)
for multi-write / aggregate check-then-act.

## Every path that writes journals

| # | Path | Check-then-act on state | Protection today | Verdict |
|---|------|------------------------|------------------|---------|
| 1 | JournalService.Approve | Pending→Posted flip | journals.Status token → DbUpdateConcurrencyException → 409 JOURNAL_ALREADY_POSTED | HARDENED (1544883) |
| 2 | JournalService.Reverse | HasReversal existence + multi-write (reversal journal + stock unwind) | SELECT … FOR UPDATE on the source row + authoritative re-check under the lock | HARDENED (1544883) |
| 3 | InvoiceService.Post | Draft→Posted flip | invoices.Status token → 409 INVOICE_ALREADY_POSTED | HARDENED (052cb12) |
| 4 | InvoiceService.ApproveAdjustment | Draft→Approved flip + credit-over-open aggregate | sales_adjustments.Status token + invoice-row FOR UPDATE before the open-amount math | HARDENED (052cb12) |
| 5 | PayrollService.Approve | Calculated→Approved flip | pay_runs.Status token → 409 PAYRUN_ALREADY_APPROVED | HARDENED |
| 6 | PayrollService.Post (incl. zero-total path) | Approved→Posted flip | pay_runs.Status token → 409 PAYRUN_ALREADY_POSTED | HARDENED |
| 7 | PayrollService.Reverse | Posted→Reversed flip + compensating run + journal | pay_runs.Status token → 409 PAYRUN_ALREADY_REVERSED | HARDENED |
| 8 | ProductionOrderService (completions, close-out, residual journal) | status flips + CompletedQty ceiling | production_orders.Status token → 409 MFG_CONCURRENCY_ERROR + LockOrderRow scalar lock | HARDENED (fix round 1) |
| 9 | **InventoryService.Close** | count Open→Closed flip, then adjustment journals + stock apply per variance line | **NONE** — counts.Status has no concurrency token, Save not guarded; two concurrent closes of one count both pass the Open check and BOTH post variance journals + apply the stock movement twice | **RACE 1** |
| 10 | **AssetService ApproveCapitalization / ApproveImpairment / ApproveDisposal** | pending event JournalId ""→journal id flip (the event's posted state) | **NONE** — asset_events.JournalId has no concurrency token; LoadPending's `evt.JournalId != ""` check is read-then-write; two concurrent approvals BOTH post the event journal (double impairment Dr 6600/Cr 1500, double disposal unwind) | **RACE 2** |
| 11 | **AssetService.RunDepreciation** | DepreciationPeriodAlreadyPosted aggregate check | app-level only (T4 fix round 1); no unique index on depreciation_runs(tenant, entity, period), no row to lock; two concurrent runs for one period BOTH post the depreciation journal and BOTH increment accumulated depreciation | **RACE 3** |
| 12 | **ReceiptService.Receive** | over-receipt guard: ReceivedQty(po, sku) aggregate vs line.Qty | **NONE** — the PO row is not locked; two concurrent receives of the full ordered qty both read already=0, BOTH pass, BOTH post the AP-accrual journal and BOTH apply stock (2x ordered) | **RACE 4** |
| 13 | SalesOrderService.Ship | stock availability: QtyOf(sku) vs qty (negative-stock block) | none on stock_levels — concurrent ships can over-draw and post COGS on phantom stock | RACE — out of scope cap (4 fixes), ledgered as concern |
| 14 | CollectionService | reminders/disputes only — writes NO journals | n/a | out of scope (no money write) |

Scope decision (cap: max 4 fixes): fix RACE 1–4; ledger Ship as the known remaining exposure.
All four fixes are model-only except the depreciation unique index (new migration, same shape
as the landed AddInvoiceDiscardIndex).
