using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// MF7 — maintenance interface (ERP-MFG-053/054, Q-MF2). ASSETS emits nothing today, so the SAME
// idempotent endpoint serves manual windows and future ASSETS feeds: (sourceEventId, revision) —
// duplicates return the stored window, out-of-order revisions never regress availability, and an
// unmapped resource lands as a persisted integration exception instead of crashing the feed.
// Accepted outages materialize as unavailable calendar windows the scheduler already respects.
public sealed class MaintenanceService(
    IMfgDeferredRepository mfg, ISchedulingService scheduling,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : IMaintenanceService
{
    public (object data, string? code, string? message) Ingest(CurrentUser user, Dtos.MaintenanceEventRequest request)
    {
        if (Guards.Check(request.SourceEventId, "SourceEventId") is { } sourceError) return (null!, "VALIDATION_ERROR", sourceError);
        if (Guards.Check(request.ResourceCode, "ResourceCode") is { } codeError) return (null!, "VALIDATION_ERROR", codeError);
        if (request.WindowEnd <= request.WindowStart) return (null!, "VALIDATION_ERROR", "Window end must be after window start.");

        var existing = mfg.FindMaintenanceEventBySource(request.SourceEventId);
        if (existing is not null)
        {
            // idempotent ingestion (053 AC1): duplicates return the stored window; an OLDER
            // revision never regresses availability — only a strictly newer revision applies
            if (request.Revision <= existing.Revision)
                return (Proj(existing), "MAINTENANCE_REPLAY", null);
            ApplyWindow(existing, request, mfg.FindWorkCenterByCode(request.ResourceCode) is not null);
            audit.Log(user, "revise", "maintenance_event", existing.Id,
                $"Outage {request.SourceEventId} revised to r{request.Revision}");
            mfg.Save();
            return (Proj(existing), "MAINTENANCE_REPLAY", null);
        }

        var workCenter = mfg.FindWorkCenterByCode(request.ResourceCode);
        var evt = new MaintenanceAvailabilityEvent(numbers.Next("mav"), ec.TenantId, ec.EntityId,
            request.SourceEventId, request.Revision, request.ResourceCode, request.AssetId,
            request.WindowStart, request.WindowEnd, request.Reason,
            workCenter is null ? MaintenanceEventStatus.Exception : MaintenanceEventStatus.Accepted,
            workCenter is null ? "UNMAPPED_RESOURCE" : null, // integration exception, persisted (053 AC1)
            user.Id, DateTimeOffset.UtcNow);
        mfg.AddMaintenanceEvent(evt);
        if (workCenter is not null)
            MaterializeOutage(evt, workCenter.Id);
        audit.Log(user, "ingest", "maintenance_event", evt.Id,
            $"Availability event {request.SourceEventId} r{request.Revision} for {request.ResourceCode}: {evt.Status}");
        mfg.Save();
        return (Proj(evt), null, null);
    }

    private void ApplyWindow(MaintenanceAvailabilityEvent existing, Dtos.MaintenanceEventRequest request, bool mapped)
    {
        mfg.UpdateMaintenanceEventWindow(existing, request.WindowStart, request.WindowEnd,
            mapped ? MaintenanceEventStatus.Accepted : MaintenanceEventStatus.Exception,
            mapped ? null : "UNMAPPED_RESOURCE", request.Revision);
        if (mapped && mfg.FindCalendarBySourceEvent(request.SourceEventId) is { } window)
        {
            // refresh the materialized outage window to the newer revision
            db.Entry(window).Property(nameof(ResourceCalendar.WindowStart)).CurrentValue = request.WindowStart;
            db.Entry(window).Property(nameof(ResourceCalendar.WindowEnd)).CurrentValue = request.WindowEnd;
        }
    }

    private void MaterializeOutage(MaintenanceAvailabilityEvent evt, string workCenterId)
    {
        if (mfg.FindCalendarBySourceEvent(evt.SourceEventId) is not null) return;
        mfg.AddCalendarWindow(new ResourceCalendar(numbers.Next("wkc"), ec.TenantId, ec.EntityId, workCenterId,
            evt.WindowStart, evt.WindowEnd, false, "maintenance", evt.SourceEventId, evt.IngestedBy, evt.IngestedAt));
    }

    // 054 AC1: an accepted outage either reschedules the intersecting work or becomes a
    // documented planner exception — both dispositions recorded here
    public (object data, string? code, string? message) Impacts(string outageEventId)
    {
        var outage = mfg.FindMaintenanceEvent(outageEventId);
        if (outage is null) return (null!, "MAINTENANCE_EVENT_NOT_FOUND", "Maintenance event not found.");
        var stored = mfg.ImpactsFor(outageEventId).ToList();
        return (new
        {
            outage = Proj(outage),
            rows = ComputeImpacts(outage, stored).Select(r => new
            {
                r.placementId, r.scheduleRunId, r.orderId, r.workCenterId, r.operationCode,
                r.placementStart, r.placementEnd, r.overlapMinutes,
                disposition = stored.FirstOrDefault(s => s.ScheduleRunId == r.scheduleRunId && s.OrderId == r.orderId)?.Disposition ?? "Pending",
                reason = (string?)null,
            }).ToList(),
            recorded = stored.Select(s => new { s.Id, s.ScheduleRunId, s.OrderId, s.WorkCenterId, s.Disposition, s.Reason, s.DecidedBy, s.DecidedAt }).ToList(),
        }, null, null);
    }

    private List<(string placementId, string scheduleRunId, string orderId, string workCenterId, string? operationCode, DateTimeOffset? placementStart, DateTimeOffset? placementEnd, int overlapMinutes)> ComputeImpacts(
        MaintenanceAvailabilityEvent outage, List<MaintenanceImpact> stored)
    {
        if (outage.Status != MaintenanceEventStatus.Accepted) return [];
        var rows = new List<(string, string, string, string, string?, DateTimeOffset?, DateTimeOffset?, int)>();
        foreach (var run in PublishedRunsTouching(outage.ResourceCode))
        {
            foreach (var p in mfg.PlacementsFor(run.Id))
            {
                if (p.WorkCenterId != outage.ResourceCode || !p.Scheduled) continue;
                var start = p.StartAt!.Value;
                var end = p.EndAt!.Value;
                if (start < outage.WindowEnd && end > outage.WindowStart)
                {
                    var overlap = (int)Math.Min((end - outage.WindowStart).TotalMinutes, (outage.WindowEnd - start).TotalMinutes);
                    rows.Add((p.Id, run.Id, p.OrderId, p.WorkCenterId, p.OperationCode, p.StartAt, p.EndAt, overlap));
                }
            }
        }
        return rows;
    }

    public (object data, string? code, string? message) ResolveImpact(CurrentUser user, Dtos.MaintenanceResolutionRequest request)
    {
        if (Guards.Check(request.OutageEventId, "OutageEventId") is { } outageError) return (null!, "VALIDATION_ERROR", outageError);
        if (mfg.FindMaintenanceEvent(request.OutageEventId) is not { Status: MaintenanceEventStatus.Accepted } outage)
            return (null!, "MAINTENANCE_EVENT_NOT_FOUND", "An ACCEPTED outage is required to resolve impacts.");
        if (request.Disposition is not ("rescheduled" or "planner_exception"))
            return (null!, "VALIDATION_ERROR", "Disposition must be rescheduled or planner_exception.");
        if (request.Disposition == "planner_exception" && string.IsNullOrWhiteSpace(request.Reason))
            return (null!, "VALIDATION_ERROR", "A planner exception requires a documented reason (054 AC2).");
        if (mfg.FindScheduleRun(request.ScheduleRunId) is null) return (null!, "SCHEDULE_RUN_NOT_FOUND", "Schedule run not found.");

        string? detail = request.Reason;
        if (request.Disposition == "rescheduled")
        {
            // the outage windows are already materialized as unavailable calendar rows — a fresh
            // finite run for the order naturally lands OUTSIDE the outage (054 AC1)
            var (run, runCode, runMessage) = scheduling.CreateRun(user, new Dtos.CreateScheduleRunRequest(
                request.OrderId, ScheduleRunMode.Finite,
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                "cal-maintenance", "fwd-1"));
            if (runCode is not null) return (null!, runCode, runMessage);
            var runId = System.Text.Json.JsonSerializer.SerializeToElement(run!, Idem.JsonOptions).GetProperty("id").GetString();
            detail = $"rescheduled by run {runId}" + (request.Reason is null ? "" : $" — {request.Reason}");
        }

        var impact = new MaintenanceImpact(numbers.Next("mav"), ec.TenantId, ec.EntityId,
            outage.Id, request.ScheduleRunId, request.OrderId, request.WorkCenterId,
            0, request.Disposition == "rescheduled" ? "Rescheduled" : "PlannerException",
            detail, user.Id, DateTimeOffset.UtcNow);
        mfg.AddMaintenanceImpact(impact);
        audit.Log(user, "resolve", "maintenance_impact", impact.Id, $"Impact resolved: {impact.Disposition} — {detail}");
        mfg.Save();
        return (new { impact.Id, impact.ScheduleRunId, impact.OrderId, impact.WorkCenterId, impact.Disposition, impact.Reason, impact.DecidedBy, impact.DecidedAt }, null, null);
    }

    // published FINITE runs that placed anything on the outage's work center
    private List<ScheduleRun> PublishedRunsTouching(string resourceCode) =>
        mfg.AllPublications().Select(p => mfg.FindScheduleRun(p.ScheduleRunId))
            .Where(r => r is not null && r.Mode == ScheduleRunMode.Finite)
            .Select(r => r!)
            .Where(r => mfg.PlacementsFor(r.Id).Any(p => p.WorkCenterId == resourceCode))
            .ToList();

    private static object Proj(MaintenanceAvailabilityEvent e) => new
    {
        e.Id, e.SourceEventId, e.Revision, e.ResourceCode, e.AssetId, e.WindowStart, e.WindowEnd,
        e.Reason, status = e.Status.ToString(), e.ExceptionReason, e.IngestedBy, e.IngestedAt,
    };
}
