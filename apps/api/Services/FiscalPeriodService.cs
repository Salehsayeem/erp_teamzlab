using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Fiscal periods (FINANCE-004/005, 063 trimmed): create with overlap rejection, close through the
// draft-journal gate, reopen only with a reason — every transition audited + outboxed (R9d: the
// reopen IS the correction path; there is no direct posting into a locked month).
public sealed class FiscalPeriodService(
    IFiscalPeriodRepository periods, IAuditRepository audit, EntityContext ec) : IFiscalPeriodService
{
    public object List(PeriodStatus? status, DateOnly? from, DateOnly? to) =>
        periods.All()
            .Where(p => status is null || p.Status == status)
            .Where(p => from is null || p.EndDate >= from)
            .Where(p => to is null || p.StartDate <= to)
            .ToList();

    public (FiscalPeriod? period, string? code, string? message) Create(CurrentUser user, Dtos.CreatePeriodRequest request)
    {
        if (periods.NameExists(request.Name))
            return (null, "PERIOD_OVERLAP", $"Period name {request.Name} already exists for this legal entity.");
        if (periods.Overlaps(request.StartDate, request.EndDate))
            return (null, "PERIOD_OVERLAP",
                $"Requested range {request.StartDate:yyyy-MM-dd}..{request.EndDate:yyyy-MM-dd} overlaps an existing period.");
        var period = new FiscalPeriod($"FP-{request.Name}", ec.TenantId, ec.EntityId, request.Name,
            request.StartDate, request.EndDate, PeriodStatus.Open);
        periods.Add(period);
        audit.Log(user, "create", "fiscal_period", period.Id,
            $"Period {period.Name} created ({period.StartDate:yyyy-MM-dd}..{period.EndDate:yyyy-MM-dd}, Open)");
        audit.Emit(user, "finance.period.created", new { periodId = period.Id, name = period.Name });
        periods.Save();
        return (period, null, null);
    }

    public (FiscalPeriod? period, string? code, string? message) Close(CurrentUser user, string periodId, string? reason)
    {
        var period = periods.Find(periodId);
        if (period is null) return (null, "PERIOD_NOT_FOUND", "Fiscal period not found.");
        if (period.Status == PeriodStatus.Closed)
            return (null, "PERIOD_STATUS", $"Period {period.Name} is already closed.");

        // 063 AC1 trimmed gate: any unapproved (Pending) journal dated inside the period blocks close
        var blocking = periods.PendingInRange(period.StartDate, period.EndDate);
        if (blocking.Count > 0)
            return (null, "CLOSE_GATE_BLOCKED",
                $"Period {period.Name} cannot close: {blocking.Count} unposted journal(s) pending: {string.Join(", ", blocking.Select(b => b.Id))}.");

        periods.MarkClosed(period, user.Id);
        audit.Log(user, "close", "fiscal_period", period.Id,
            $"Period {period.Name} closed by {user.Email}{(string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}")}");
        audit.Emit(user, "finance.period.closed", new { periodId = period.Id, name = period.Name, reason });
        periods.Save();
        return (period, null, null);
    }

    public (FiscalPeriod? period, string? code, string? message) Reopen(CurrentUser user, string periodId, string reason)
    {
        var period = periods.Find(periodId);
        if (period is null) return (null, "PERIOD_NOT_FOUND", "Fiscal period not found.");
        if (period.Status == PeriodStatus.Open)
            return (null, "PERIOD_STATUS", $"Period {period.Name} is already open; reopen only applies to soft-closed/closed periods.");

        periods.MarkReopened(period, reason);
        audit.Log(user, "reopen", "fiscal_period", period.Id,
            $"Period {period.Name} reopened by {user.Email}: {reason}");
        audit.Emit(user, "finance.period.reopened", new { periodId = period.Id, name = period.Name, reason, actor = user.Id });
        periods.Save();
        return (period, null, null);
    }
}
