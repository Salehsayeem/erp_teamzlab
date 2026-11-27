using Erp.Api.Data;

namespace Erp.Api;

// The ONE period lock every posting path shares (FINANCE-005 AC1 / ruling R9d+R9e): resolve the
// fiscal period for the tenant+entity by accounting date and reject unless it is Open. Journal
// approval, receipts, shipments, invoices, reversals and count adjustments all call this — no
// path may post into a SoftClosed/Closed month, and correction requires an audited admin reopen.
// A date with no configured period also blocks posting (fail-closed): unbooked calendars must not
// silently accept money movements.
public static class FinanceGuards
{
    public static (string? Code, string? Message) EnsurePeriodOpen(ErpDbContext db, EntityContext ec, DateOnly date)
    {
        var period = db.FiscalPeriods
            .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId
                && p.StartDate <= date && p.EndDate >= date)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefault();
        if (period is null)
            return ("PERIOD_NOT_CONFIGURED",
                $"No fiscal period covers {date:yyyy-MM-dd} for this legal entity; posting is blocked until a period is configured.");
        if (period.Status is PeriodStatus.SoftClosed or PeriodStatus.Closed)
            return ("PERIOD_LOCKED",
                $"Fiscal period {period.Name} is {period.Status}; posting is locked" +
                (period.ClosedAt is { } at ? $" (closed by {period.ClosedBy ?? "system"} at {at:u})" : ".")
                // 063 edge: name the designated adjustment period so a late source event routes to
                // the adjustment workflow (dated in the open ADJ period) instead of backdating
                + AdjustmentRoute(db, ec));
        return (null, null);
    }

    // the entity's latest OPEN adjustment period, if a calendar has materialized one
    private static string AdjustmentRoute(ErpDbContext db, EntityContext ec)
    {
        var adj = db.FiscalPeriods
            .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId
                && p.IsAdjustment && p.Status == PeriodStatus.Open)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefault();
        return adj is null ? "" :
            $"; late events for this period must route to the designated adjustment period {adj.Name} ({adj.StartDate:yyyy-MM-dd}..{adj.EndDate:yyyy-MM-dd}) via the adjustment workflow — no backdated postings.";
    }
}
