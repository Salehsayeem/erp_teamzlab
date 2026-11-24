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
                (period.ClosedAt is { } at ? $" (closed by {period.ClosedBy ?? "system"} at {at:u})" : "."));
        return (null, null);
    }
}
