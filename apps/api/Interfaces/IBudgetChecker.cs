namespace Erp.Api.Interfaces;

// Q-P1: the budget-control contract is a fail-closed in-process stub — no real adapter this wave.
// Evaluate reads the config-driven mode (Pass/Warn/HardStop/Unavailable, finance_settings
// budget.check_mode); Reserve is idempotent on txn-ID (013 AC3 — same txn never double-reserves).
public interface IBudgetChecker
{
    BudgetCheckOutcome Evaluate(decimal amount);
    void Reserve(string txnId, decimal amount, string currency, string userId);
}

public sealed record BudgetCheckOutcome(string Mode, string? BlockCode, string? Message);
