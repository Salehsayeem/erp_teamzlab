using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// MF2 — scheduling (ERP-MFG-027/028/029/030) and MF3 — dispatch list + capacity load (031/032).
// Finite runs place operations ONLY inside open work-center calendar windows and serialize ops
// per work center; unscheduled ops carry a reason code and are never silently dropped. Infinite
// runs ignore calendars — the mode rides every output so an infinite plan can never masquerade
// as finite. Publications are unique per run (idempotent) and gate on hard conflicts unless an
// override (reason + reviewer ≠ run maker) is supplied (030 AC2).
public sealed class SchedulingService(
    IMfgDeferredRepository mfg, IManufacturingRepository landed, IInventoryRepository inventory,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : ISchedulingService
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public object RunsForOrder(string? orderId)
    {
        var runs = orderId is null ? null : mfg.RunsForOrder(orderId);
        return runs ?? db.ScheduleRuns
            .Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId)
            .OrderByDescending(r => r.CreatedAt).ToList();
    }

    public (object data, string? code, string? message) CreateRun(CurrentUser user, Dtos.CreateScheduleRunRequest request)
    {
        var order = landed.FindOrder(request.OrderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (request.HorizonTo < request.HorizonFrom) return (null!, "VALIDATION_ERROR", "Horizon end must not be before start.");
        if (Guards.Check(request.CalendarVersion, "CalendarVersion") is { } cvError) return (null!, "VALIDATION_ERROR", cvError);
        if (Guards.Check(request.AlgorithmVersion, "AlgorithmVersion") is { } avError) return (null!, "VALIDATION_ERROR", avError);

        var routingId = order.ReleasedRoutingRevisionId ?? order.PlannedRoutingRevisionId;
        if (routingId is null)
            return (null!, "SCHEDULING_NO_ROUTING", "The order has no routing revision frozen — schedule against a released routing (024 AC3).");
        var ops = mfg.RoutingOperationsFor(routingId);
        if (ops.Count == 0) return (null!, "SCHEDULING_NO_ROUTING", "The frozen routing revision has no operations.");

        var run = new ScheduleRun(numbers.Next("srn"), ec.TenantId, ec.EntityId, request.OrderId,
            request.Mode, ScheduleRunStatus.Completed, request.HorizonFrom, request.HorizonTo,
            request.CalendarVersion, request.AlgorithmVersion, user.Id, DateTimeOffset.UtcNow);
        mfg.AddScheduleRun(run);

        // forward pass: ops in sequence; finite mode consumes the earliest open window that fits
        var remaining = order.PlannedQty - order.CompletedQty;
        var perWorkCenterBusyUntil = new Dictionary<string, DateTimeOffset>();
        var horizonStart = new DateTimeOffset(request.HorizonFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        foreach (var op in ops)
        {
            var durationMin = op.SetupMin + op.PerUnitMin * Math.Max(remaining, 0);
            var duration = TimeSpan.FromMinutes((double)durationMin);
            if (op.IsZeroDuration) duration = TimeSpan.FromMinutes(15); // milestones still occupy a visible slot

            DateTimeOffset? start = null;
            string? reason = null;
            if (request.Mode == ScheduleRunMode.Infinite)
            {
                // infinite mode: back-to-back from the horizon start, calendars ignored — labeled
                start = PerWorkCenterNextFree(perWorkCenterBusyUntil, op.WorkCenterId, horizonStart);
            }
            else
            {
                var window = mfg.AvailableWindowsFor(op.WorkCenterId, request.HorizonFrom, request.HorizonTo)
                    .FirstOrDefault(w => Fits(w, perWorkCenterBusyUntil.TryGetValue(op.WorkCenterId, out var until) ? until : horizonStart, duration));
                if (window is null)
                    reason = "NO_CAPACITY"; // 027 AC3 — never silently over capacity
                else
                    start = Max(window.WindowStart, perWorkCenterBusyUntil.TryGetValue(op.WorkCenterId, out var busyUntil) ? busyUntil : DateTimeOffset.MinValue);
            }

            var placed = start is not null;
            var end = placed ? start + duration : null;
            if (placed) perWorkCenterBusyUntil[op.WorkCenterId] = end!.Value;
            mfg.AddPlacement(new OperationPlacement(numbers.Next("srn"), ec.TenantId, ec.EntityId, run.Id,
                request.OrderId, routingId, op.Seq, op.WorkCenterId, op.OperationCode, placed ? start : null, end,
                placed, reason, user.Id, DateTimeOffset.UtcNow));
        }

        // 030 AC2: hard conflicts = overlapping placements on one work center inside this run
        var placements = mfg.PlacementsFor(run.Id).Where(p => p.Scheduled).OrderBy(p => p.StartAt).ToList();
        foreach (var pair in placements.SelectMany((p, i) => placements.Skip(i + 1).Select(q => (p, q)))
                     .Where(x => x.p.WorkCenterId == x.q.WorkCenterId && x.p.StartAt < x.q.EndAt))
        {
            mfg.AddConflict(new ConflictReport(numbers.Next("cfl"), ec.TenantId, ec.EntityId, run.Id, "Hard",
                "WORK_CENTER_OVERLAP",
                $"Operations {pair.p.OperationCode} (seq {pair.p.Seq}) and {pair.q.OperationCode} (seq {pair.q.Seq}) overlap on work center {pair.p.WorkCenterId}.",
                false, null, null));
        }

        audit.Log(user, "create", "schedule_run", run.Id,
            $"{request.Mode} schedule run for order {order.OrderNo}: {ops.Count} operation(s), horizon {request.HorizonFrom:yyyy-MM-dd}..{request.HorizonTo:yyyy-MM-dd}");
        audit.Emit(user, "mfg.schedule.run.completed", new { runId = run.Id, orderId = request.OrderId, mode = request.Mode.ToString() });
        mfg.Save();
        return (ProjRun(run, mfg.PlacementsFor(run.Id), mfg.ConflictsFor(run.Id)), null, null);
    }

    private static DateTimeOffset PerWorkCenterNextFree(Dictionary<string, DateTimeOffset> busy, string wc, DateTimeOffset from) =>
        busy.TryGetValue(wc, out var until) && until > from ? until : from;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static bool Fits(ResourceCalendar window, DateTimeOffset earliest, TimeSpan duration) =>
        window.WindowEnd - Max(window.WindowStart, earliest) >= duration;

    public object Placements(string runId) =>
        mfg.PlacementsFor(runId).Select(p => new
        {
            p.Id, p.ScheduleRunId, p.OrderId, p.RoutingRevisionId, p.Seq, p.OperationCode, p.WorkCenterId,
            p.StartAt, p.EndAt, p.Scheduled, p.UnscheduledReason,
        }).ToList();

    public object Conflicts(string runId) => mfg.ConflictsFor(runId).Select(c => new
    {
        c.Id, c.ScheduleRunId, c.Severity, c.Code, c.Detail, c.Resolved, c.ResolvedBy, c.ResolvedAt,
    }).ToList();

    public (object data, string? code, string? message) Publish(CurrentUser reviewer, string runId, Dtos.SchedulePublicationRequest request)
    {
        var run = mfg.FindScheduleRun(runId);
        if (run is null) return (null!, "SCHEDULE_RUN_NOT_FOUND", "Schedule run not found.");
        if (mfg.FindPublication(runId) is { } existing)
            return (ProjPublication(existing), null, null); // published events idempotent per run
        if (run.CreatedBy == reviewer.Id)
            return (null!, "SCHEDULE_PUBLISH_ERROR", "Maker-checker: the run maker cannot publish their own run (030).");

        var unresolvedHard = mfg.ConflictsFor(runId).Any(c => c.Severity == "Hard" && !c.Resolved);
        if (unresolvedHard && string.IsNullOrWhiteSpace(request.OverrideReason))
            return (null!, "SCHEDULE_PUBLISH_BLOCKED",
                "The run carries unresolved hard conflicts — publish requires an approved override reason (030 AC2).");

        var publication = new SchedulePublication(runId, ec.TenantId, ec.EntityId, runId,
            reviewer.Id, request.OverrideReason, DateTimeOffset.UtcNow);
        if (!mfg.AddPublication(publication))
        {
            var winner = mfg.FindPublication(runId)!;
            return (ProjPublication(winner), null, null);
        }
        audit.Log(reviewer, "publish", "schedule_run", runId,
            $"Run published{(request.OverrideReason is null ? "" : " with override: " + request.OverrideReason)} by {reviewer.Email}");
        audit.Emit(reviewer, "mfg.schedule.published", new { runId, orderId = run.OrderId, mode = run.Mode.ToString() });
        mfg.Save();
        return (ProjPublication(publication), null, null);
    }

    public (object data, string? code, string? message) CreateScenario(CurrentUser user, Dtos.CreateScenarioRequest request)
    {
        var run = mfg.FindScheduleRun(request.BaseRunId);
        if (run is null) return (null!, "SCHEDULE_RUN_NOT_FOUND", "Base run not found.");
        if (Guards.Check(request.Name, "Name") is { } nameError) return (null!, "VALIDATION_ERROR", nameError);
        if (request.FrozenFrom is { } f && request.FrozenTo is { } t && t < f)
            return (null!, "VALIDATION_ERROR", "Frozen-to must not be before frozen-from.");

        var scenario = new ScheduleScenario(numbers.Next("scn"), ec.TenantId, ec.EntityId, request.Name,
            request.BaseRunId, request.FrozenFrom, request.FrozenTo, user.Id, DateTimeOffset.UtcNow);
        mfg.AddScenario(scenario);
        audit.Log(user, "create", "schedule_scenario", scenario.Id, $"Scenario '{request.Name}' created over run {request.BaseRunId}");
        mfg.Save();
        return (new { scenario.Id, scenario.Name, scenario.BaseRunId, scenario.FrozenFrom, scenario.FrozenTo, scenario.CreatedBy, scenario.CreatedAt }, null, null);
    }

    // 029 AC3: changes inside a frozen interval need a permitted override (reason + an approver
    // who is not the scenario maker).
    public (object data, string? code, string? message) MovePlacement(CurrentUser user, string scenarioId, Dtos.PlacementMoveRequest request)
    {
        var scenario = mfg.FindScenario(scenarioId);
        if (scenario is null) return (null!, "SCHEDULE_SCENARIO_NOT_FOUND", "Scenario not found.");
        var placement = mfg.FindPlacement(request.PlacementId);
        if (placement is null || placement.ScheduleRunId != scenario.BaseRunId)
            return (null!, "PLACEMENT_NOT_FOUND", "Placement not found on this scenario's base run.");

        var insideFreeze = placement.StartAt is { } s && scenario.FrozenFrom <= s && s <= (scenario.FrozenTo ?? s);
        if (insideFreeze)
        {
            if (string.IsNullOrWhiteSpace(request.OverrideReason) || string.IsNullOrWhiteSpace(request.OverrideApprover))
                return (null!, "SCHEDULE_FROZEN",
                    "The placement sits inside the frozen interval — moving it requires an override reason and approver (029 AC3).");
            if (request.OverrideApprover == scenario.CreatedBy)
                return (null!, "SCHEDULE_FROZEN", "Maker-checker: the scenario maker cannot approve their own override.");
            mfg.AddOverride(new ScheduleOverride(numbers.Next("scn"), ec.TenantId, ec.EntityId, scenarioId,
                request.OverrideReason, request.OverrideApprover, DateTimeOffset.UtcNow));
            audit.Log(user, "override", "schedule_scenario", scenarioId,
                $"Frozen-interval override by {request.OverrideApprover}: {request.OverrideReason}");
        }

        var duration = placement.EndAt - placement.StartAt;
        mfg.UpdatePlacement(placement, request.NewStartAt, request.NewStartAt + duration, placement.Scheduled, placement.UnscheduledReason);
        audit.Log(user, "move", "operation_placement", placement.Id, $"Placement moved to {request.NewStartAt:u} in scenario {scenario.Name}");
        mfg.Save();
        return (new { placement.Id, placement.StartAt, placement.EndAt }, null, null);
    }

    public object Overrides(string scenarioId) => mfg.OverridesFor(scenarioId).Select(o => new
    {
        o.Id, o.ScenarioId, o.Reason, o.ApprovedBy, o.At,
    }).ToList();

    // ---- MF3: dispatch list (031) ----
    // names the active (latest published) schedule version + refresh time; placements blocked by
    // material or quality prerequisites are visibly non-dispatchable.
    public object DispatchList(CurrentUser user, string? workCenterId)
    {
        var published = db.SchedulePublications
            .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId)
            .OrderByDescending(p => p.At).ToList();
        ScheduleRun? active = null;
        foreach (var pub in published)
        {
            var run = mfg.FindScheduleRun(pub.ScheduleRunId);
            if (run is not null && (workCenterId is null
                    || mfg.PlacementsFor(run.Id).Any(p => p.WorkCenterId == workCenterId)))
            {
                active = run;
                break;
            }
        }

        var rows = new List<object>();
        if (active is not null)
        {
            foreach (var p in mfg.PlacementsFor(active.Id))
            {
                if (workCenterId is not null && p.WorkCenterId != workCenterId) continue;
                var order = landed.FindOrder(p.OrderId);
                var blockedReason = (string?)null;
                if (mfg.OpenHoldsForOrder(p.OrderId).Count > 0) blockedReason = "QUALITY_HOLD";
                else if (order is not null && MaterialShort(order)) blockedReason = "MATERIAL_SHORT";
                rows.Add(new
                {
                    p.Id, p.Seq, p.OperationCode, p.WorkCenterId, orderId = p.OrderId,
                    orderNo = order?.OrderNo, p.StartAt, p.EndAt, p.Scheduled,
                    dispatchable = p.Scheduled && blockedReason is null,
                    blockedReason, lateEarly = (string?)null,
                });
            }
        }

        return new
        {
            scheduleRunId = active?.Id, scheduleMode = active?.Mode.ToString(),
            publishedAt = active is null ? null : mfg.FindPublication(active.Id)?.At,
            refreshedAt = DateTimeOffset.UtcNow, rows,
            unscheduled = active is null
                ? new List<object>()
                : mfg.PlacementsFor(active.Id).Where(p => !p.Scheduled && (workCenterId is null || p.WorkCenterId == workCenterId))
                    .Select(p => (object)new { p.Id, p.Seq, p.OperationCode, p.WorkCenterId, reason = p.UnscheduledReason }).ToList(),
        };
    }

    // material readiness: any BOM component of the order's released revision is short against
    // availability (stock minus open quality holds) — computed, not stored
    private bool MaterialShort(ProductionOrder order)
    {
        var revisionId = order.ReleasedBomRevisionId ?? order.PlannedBomRevisionId;
        if (revisionId is null) return false;
        var basis = landed.FindBom(revisionId)?.OutputQtyPer ?? 1m;
        var lines = landed.LinesForRevision(revisionId);
        var remaining = order.PlannedQty - order.CompletedQty;
        foreach (var line in lines)
        {
            var required = line.QtyPer / Math.Max(basis, 0.000001m) * remaining;
            var available = inventory.QtyOf(line.ComponentSku) - OpenHoldQty(line.ComponentSku);
            if (available < required) return true;
        }
        return false;
    }

    private decimal OpenHoldQty(string itemSku) =>
        mfg.OpenHoldsForItem(itemSku).Sum(h => h.Qty);

    // ---- MF3: capacity load (032) ----
    // load = Σ included placement durations per work center per bucket, disclosed with the
    // bucket + calendar + duration basis; unscheduled ops listed separately.
    public object CapacityLoad(string bucket)
    {
        if (bucket is not ("day" or "week")) bucket = "day";
        var published = db.SchedulePublications
            .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId)
            .OrderByDescending(p => p.At).ToList();
        var loaded = new List<(string WorkCenterId, string? Code, DateTimeOffset BucketStart, decimal Load, decimal Capacity)>();
        var unscheduled = new List<object>();
        foreach (var pub in published.Take(20)) // latest-published runs only
        {
            var run = mfg.FindScheduleRun(pub.ScheduleRunId);
            if (run is null || run.Mode != ScheduleRunMode.Finite) continue;
            foreach (var p in mfg.PlacementsFor(run.Id))
            {
                if (!p.Scheduled)
                {
                    unscheduled.Add(new { p.Id, runId = run.Id, p.OrderId, p.OperationCode, p.WorkCenterId, reason = p.UnscheduledReason });
                    continue;
                }
                var loadMinutes = (decimal)((p.EndAt - p.StartAt)?.TotalMinutes ?? 0);
                var bucketStart = BucketStart(p.StartAt!.Value, bucket);
                var workCenter = mfg.FindWorkCenter(p.WorkCenterId);
                var capacityMinutes = CapacityMinutes(p.WorkCenterId, bucketStart, bucket);
                loaded.Add((p.WorkCenterId, workCenter?.Code, bucketStart, loadMinutes, capacityMinutes));
            }
        }

        // aggregate to work center × bucket — the load equals the sum of included durations
        var aggregated = loaded
            .GroupBy(r => (r.WorkCenterId, r.BucketStart))
            .Select(g => new
            {
                workCenterId = g.Key.WorkCenterId,
                workCenterCode = g.First().Code,
                bucketStart = g.Key.BucketStart,
                loadMinutes = g.Sum(r => r.Load),
                capacityMinutes = g.First().Capacity,
                utilizationPct = g.First().Capacity == 0 ? 0
                    : decimal.Round(g.Sum(r => r.Load) / g.First().Capacity * 100, 2, MidpointRounding.AwayFromZero),
            })
            .OrderBy(r => r.bucketStart).ThenBy(r => r.workCenterCode)
            .ToList();

        return new
        {
            bucket,
            durationBasis = "placement-duration",
            calendarBasis = "resource_calendars open windows",
            rows = aggregated,
            unscheduled,
        };
    }

    private static DateTimeOffset BucketStart(DateTimeOffset at, string bucket) =>
        bucket == "week"
            ? new DateTimeOffset(at.Date.AddDays(-(int)at.Date.DayOfWeek), at.Offset)
            : new DateTimeOffset(at.Date, at.Offset);

    private decimal CapacityMinutes(string workCenterId, DateTimeOffset bucketStart, string bucket)
    {
        var bucketEnd = bucketStart.AddDays(bucket == "week" ? 7 : 1);
        return mfg.AvailableWindowsFor(workCenterId, DateOnly.FromDateTime(bucketStart.UtcDateTime), DateOnly.FromDateTime(bucketEnd.UtcDateTime))
            .Where(w => w.WindowStart < bucketEnd && w.WindowEnd > bucketStart)
            .Sum(w => (decimal)Math.Min((w.WindowEnd - bucketStart).TotalMinutes, (bucketEnd - w.WindowStart).TotalMinutes));
    }

    private static object ProjRun(ScheduleRun run, List<OperationPlacement> placements, List<ConflictReport> conflicts) => new
    {
        run.Id, run.OrderId, mode = run.Mode.ToString(), status = run.Status.ToString(),
        run.HorizonFrom, run.HorizonTo, run.CalendarVersion, run.AlgorithmVersion, run.CreatedBy, run.CreatedAt,
        placements = placements.Select(p => new
        {
            p.Id, p.Seq, p.OperationCode, p.WorkCenterId, p.StartAt, p.EndAt, p.Scheduled, p.UnscheduledReason,
        }).ToList(),
        conflicts = conflicts.Select(c => new { c.Id, c.Severity, c.Code, c.Detail, c.Resolved }).ToList(),
    };

    private static object ProjPublication(SchedulePublication p) => new
    {
        p.Id, p.ScheduleRunId, p.ReviewerId, p.OverrideReason, p.At,
    };
}
