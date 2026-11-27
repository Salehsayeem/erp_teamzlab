# Review — FINANCE-deep P1 (periods, close checklist & 063 gate) — 2026-11-28

Scope: `6ae45d9..6f5fd2c` (4 P1 commits) at the pinned SHA, live filtered-class run 8/8 + build 0 errors
+ guard OK by the reviewer. Verdict: **APPROVE WITH FIXES** (merge gate — 5 Important, none Critical).

## Verified TRUE (adversarial)
063 gate ordering + waive semantics (pending-journal gate first, waive cannot bypass it; 409 names every
task id; waive needs close:approve + reason; stamps retained rows w/ reason/user/time + audit; ONE
SaveChanges flushes tasks+period close). Checklist evidence-or-waiver exclusivity, terminal-row
immutability, disjoint buckets w/ null guards. Tenant/entity scoping everywhere; close:write/close:approve
correctly consumed from the pre-existing R-FIN superset (not re-unioned); U-015 sweep on real endpoints
w/ proven token. PEOPLE payroll seam untouched; PERIOD_LOCKED message change breaks no existing
assertion (all 9 check CODE only). Migration additive, sequences collision-free, snapshot coherent.
Error codes exactly 10, house mapping, guard 340.

## Findings → fix round (RED-first)
1. **Demo seed CTK-0001 orphaned** — period_id FP-{next month} never exists on a fresh DB
   (AddFinanceSeed ends at current month; that applied migration must not be edited). Three comments
   assert a false fact; the demo test never checks the period exists. Fix: seed a dedicated reachable
   period in the (still-unapplied) P1 migration, point the task at it, fix comments, strengthen test.
2. **Close page missing loading/error states** — imports present, never rendered; `phase` dead; silent
   EmptyState on fetch failure violates the 4-state rule. + dead banner state + inline TaskRows remount churn.
3. **53-week year unrepresentable while comments claim handled** — engine is 52-week-only; loud 400
   CALENDAR_VALIDATION + honest comments + done-log deferral (spec 004 edge).
4. **Mid-year StartDate → inverted period rows** (StartDate>EndDate materialized, appear in the gate
   dropdown). Fix: enforce StartDate == Jan 1 of Year at create+activation (400).
5. **FP-<name> global PK → 500 on second entity** — defer to R14 (ratified Option B) but loud: 23505
   catch → 409 CALENDAR_PERIOD_CONFLICT + done-log note.

## Minors → fixing
7. close_tasks concurrency token (finance.md status+token rule) → stale Complete-vs-waive → 409
   CLOSE_TASK_STATE_ERROR. 10. "336 days" comment → 364. 8. covered by 2's cleanup.

## Deferred (ruled)
6. activation check-then-act dual-coverage (dev-scale, single-admin; unique index backstops same-pattern).
9. close:write vs close:approve distinction untestable with current fixtures (code-verified only).
Task-owner field (spec 062 normative mentions it) — done-log line, later breadth.

## Judged sound
ADJ-window routing arithmetic (364-day chain; 1–2 day remainder; PERIOD_LOCKED names the ADJ route,
test-verified). Checklist without header table = consistent w/ brief + spec 062 ("checklists by entity
and period" satisfied by per-period sets).
