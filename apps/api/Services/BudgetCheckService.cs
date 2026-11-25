using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Q-P1: in-process budget-control stub behind IBudgetChecker — a real adapter arrives later and
// re-points this one class. Mode is config-driven (finance_settings key budget.check_mode):
//   Pass        → reserve and allow
//   Warn        → allow (a warning surfaces in the outcome, never blocks)
//   HardStop    → block (013 AC2); overriding needs requisitions:override + reason (service-side)
//   Unavailable → block fail-closed (013 edge: an outage must never silently spend)
// An unknown/garbage mode is treated as Unavailable — fail-closed by default.
public sealed class BudgetCheckService(ErpDbContext db, EntityContext ec) : IBudgetChecker
{
    public BudgetCheckOutcome Evaluate(decimal amount)
    {
        var mode = db.FinanceSettings
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Key == "budget.check_mode")
            .Select(s => s.Value).FirstOrDefault();
        return mode switch
        {
            "Pass" => new BudgetCheckOutcome("Pass", null, null),
            "Warn" => new BudgetCheckOutcome("Warn", null, $"Budget check warning: {amount:0.##} will be committed."),
            "HardStop" => new BudgetCheckOutcome("HardStop", "BUDGET_HARD_STOP",
                $"Budget control is set to HardStop; committing {amount:0.##} requires a budget override."),
            "Unavailable" => new BudgetCheckOutcome("Unavailable", "BUDGET_CHECK_UNAVAILABLE",
                "The budget-control service is unavailable; the commitment is blocked (fail-closed)."),
            _ => new BudgetCheckOutcome("Unavailable", "BUDGET_CHECK_UNAVAILABLE",
                $"Budget-check mode '{mode ?? "missing"}' is not recognized; the commitment is blocked (fail-closed)."),
        };
    }

    // 013 AC3: reservation keyed by the txn-ID — a retry must never double-reserve. The unique
    // (tenant, entity, txn_id) index is the backstop under concurrency.
    public void Reserve(string txnId, decimal amount, string currency, string userId)
    {
        if (db.BudgetReservations.Any(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.TxnId == txnId))
            return;
        db.BudgetReservations.Add(new BudgetReservation(txnId, ec.TenantId, ec.EntityId, txnId,
            amount, currency, ReservationStatus.Reserved, userId, DateTimeOffset.UtcNow));
    }
}
