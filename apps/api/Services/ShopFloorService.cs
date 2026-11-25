using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// MF5 — shop-floor reporting extensions (ERP-MFG-044..049). Workers report labor time, a
// different officer approves, APPROVED entries are immutable except through a linked correcting
// entry (049); machine counters dedupe on the source event id and flag rollovers with a
// non-negative delta (045); downtime validates reason codes against the effective profile and
// rejects overlaps (046); the WIP projection is computed-on-read from accepted events with a
// watermark (047, Q-MF5); WIP transfers respect routing precedence and the transferable open
// quantity, deviations need a reason + a non-maker approver (048).
public sealed class ShopFloorService(
    IMfgDeferredRepository mfg, IManufacturingRepository landed, IProductionOrderService production,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : IShopFloorService
{
    // ---- 044: labor time ----

    public (object data, string? code, string? message) ReportLabor(CurrentUser user, string orderId, Dtos.LaborTimeRequest request)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (Guards.Check(request.WorkerEmploymentId, "WorkerEmploymentId") is { } workerError) return (null!, "VALIDATION_ERROR", workerError);
        if (request.EndAt <= request.StartAt) return (null!, "VALIDATION_ERROR", "EndAt must be after StartAt.");

        // overlapping intervals for the same worker are rejected, not silently stacked (044 AC3)
        foreach (var existing in mfg.LaborEntriesForWorker(request.WorkerEmploymentId))
            if (existing.StartAt < request.EndAt && existing.EndAt > request.StartAt)
                return (null!, "LABOR_TIME_OVERLAP",
                    $"{request.WorkerEmploymentId} already has {existing.Id} covering {existing.StartAt:u}..{existing.EndAt:u}.");

        var entry = new LaborTimeEntry(numbers.Next("lbt"), ec.TenantId, ec.EntityId, orderId,
            request.WorkerEmploymentId, request.StartAt, request.EndAt,
            decimal.Round((decimal)(request.EndAt - request.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero),
            LaborEntryStatus.Reported, null, null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddLaborEntry(entry);
        audit.Log(user, "report", "labor_time", entry.Id,
            $"Labor reported on {order.OrderNo}: {entry.Minutes} min by {request.WorkerEmploymentId}");
        mfg.Save();
        return (ProjLabor(entry), null, null);
    }

    public (object data, string? code, string? message) ApproveLabor(CurrentUser approver, string orderId, string entryId)
    {
        var entry = mfg.FindLaborEntry(entryId);
        if (entry is null || entry.OrderId != orderId) return (null!, "LABOR_ENTRY_NOT_FOUND", "Labor entry not found.");
        if (entry.ReportedBy == approver.Id)
            return (null!, "LABOR_APPROVAL_ERROR", "Maker-checker: the reporter cannot approve their own time (044).");
        if (!mfg.SetLaborApproved(entry, approver.Id, DateTimeOffset.UtcNow))
            return (null!, "LABOR_ALREADY_DECIDED", $"The entry is already {entry.Status} — approved entries are immutable (044).");
        audit.Log(approver, "approve", "labor_time", entry.Id, $"Labor approved by {approver.Email} ({entry.Minutes} min)");
        mfg.Save();
        return (ProjLabor(entry), null, null);
    }

    public object LaborEntries(string orderId) => mfg.LaborEntriesForOrder(orderId).Select(ProjLabor).ToList();

    // ---- 045: machine counters ----

    public (object data, string? code, string? message) ReportCounter(CurrentUser user, string orderId, Dtos.CounterEventRequest request)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (Guards.Check(request.SourceEventId, "SourceEventId") is { } sourceError) return (null!, "VALIDATION_ERROR", sourceError);
        if (request.CounterValue < 0) return (null!, "VALIDATION_ERROR", "CounterValue must be >= 0.");

        // duplicate source event ids are ignored idempotently (045 AC1)
        if (mfg.FindCounterEventBySource(request.SourceEventId) is { } existing)
            return (ProjCounter(existing), "SHOPFLOOR_REPLAY", null);

        var previous = mfg.LatestCounterForOrder(orderId);
        var rollover = previous is not null && request.CounterValue < previous.CounterValue;
        // delta = the raw reading on rollover, the difference otherwise — NEVER negative (045 AC2)
        var delta = previous is null ? request.CounterValue
            : rollover ? request.CounterValue : request.CounterValue - previous.CounterValue;

        var evt = new CounterEvent(numbers.Next("cme"), ec.TenantId, ec.EntityId, orderId,
            request.SourceEventId, request.CounterValue, decimal.Round(delta, 4, MidpointRounding.AwayFromZero),
            rollover, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddCounterEvent(evt);
        audit.Log(user, "report", "counter_event", evt.Id,
            $"Counter {request.SourceEventId}: reading {request.CounterValue}, delta {evt.DeltaQty}{(rollover ? " (ROLLOVER)" : "")}");
        mfg.Save();
        return (ProjCounter(evt), null, null);
    }

    public object CounterEvents(string orderId) => mfg.CounterEventsForOrder(orderId).Select(ProjCounter).ToList();

    // ---- 046: downtime ----

    public (object data, string? code, string? message) ReportDowntime(CurrentUser user, string orderId, Dtos.DowntimeEventRequest request)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (mfg.FindWorkCenter(request.WorkCenterId) is null) return (null!, "WORK_CENTER_NOT_FOUND", "Work center not found.");
        if (request.EndAt <= request.StartAt) return (null!, "VALIDATION_ERROR", "EndAt must be after StartAt.");
        if (Guards.Check(request.ReasonCode, "ReasonCode") is { } reasonError) return (null!, "VALIDATION_ERROR", reasonError);

        // the effective profile is tenant data; a missing profile fails closed (046 AC1)
        var profile = mfg.Setting("mfg.downtime_reasons");
        if (string.IsNullOrWhiteSpace(profile))
            return (null!, "DOWNTIME_PROFILE_MISSING",
                "No downtime reason profile is configured — configure mfg.downtime_reasons before reporting downtime.");
        var allowed = profile.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!allowed.Contains(request.ReasonCode))
            return (null!, "VALIDATION_ERROR", $"Reason code '{request.ReasonCode}' is not in the effective downtime profile.");

        if (mfg.DowntimeForWorkCenterOverlapping(request.WorkCenterId, request.StartAt, request.EndAt).Count > 0)
            return (null!, "DOWNTIME_OVERLAP", "An overlapping downtime window already exists for this work center (046).");

        var evt = new DowntimeEvent(numbers.Next("dwn"), ec.TenantId, ec.EntityId, orderId,
            request.WorkCenterId, request.StartAt, request.EndAt, request.ReasonCode, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddDowntimeEvent(evt);
        audit.Log(user, "report", "downtime_event", evt.Id,
            $"Downtime {request.ReasonCode} on work center {request.WorkCenterId}: {(evt.EndAt - evt.StartAt).TotalMinutes} min");
        mfg.Save();
        return (ProjDowntime(evt), null, null);
    }

    public object DowntimeEvents(string orderId) => mfg.DowntimeEventsForOrder(orderId).Select(ProjDowntime).ToList();

    // ---- 047: WIP projection — computed on read (Q-MF5), never stored ----

    public (object data, string? code, string? message) WipProjection(string orderId)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        var (check, _, _) = production.CloseCheck(orderId);
        // the landed close-check already computes the WIP ledger (debits/credits/remainder) —
        // reuse it; the anonymous shape is read dynamically here (same assembly)
        dynamic ledger = check!;
        var issues = landed.IssuesFor(orderId);
        var completions = landed.CompletionsFor(orderId);
        var scraps = landed.ScrapsFor(orderId);
        var labor = mfg.LaborEntriesForOrder(orderId);
        var counters = mfg.CounterEventsForOrder(orderId);
        var downtime = mfg.DowntimeEventsForOrder(orderId);

        DateTimeOffset? watermark = null;
        foreach (var at in issues.Select(i => i.ReportedAt)
                     .Concat(completions.Select(c => c.ReportedAt))
                     .Concat(scraps.Select(s => s.ReportedAt))
                     .Concat(labor.Select(l => l.ReportedAt))
                     .Concat(counters.Select(c => c.At))
                     .Concat(downtime.Select(d => d.At)))
            watermark = watermark is null || at > watermark ? at : watermark;

        return (new
        {
            orderId, orderNo = order.OrderNo,
            wipDebits = (decimal)ledger.wipDebits, wipCredits = (decimal)ledger.wipCredits,
            wipRemainder = (decimal)ledger.wipRemainder,
            sourceVersions = new
            {
                issues = issues.Count, completions = completions.Count, scraps = scraps.Count,
                labor = labor.Count, counters = counters.Count, downtime = downtime.Count,
            },
            watermark, computedAt = DateTimeOffset.UtcNow, // recomputed every read — never stale-overwritten
        }, null, null);
    }

    // ---- 048: WIP transfers ----

    public (object data, string? code, string? message) TransferWip(CurrentUser user, string orderId, Dtos.WipTransferRequest request)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        var routingId = order.ReleasedRoutingRevisionId ?? order.PlannedRoutingRevisionId;
        if (routingId is null) return (null!, "SCHEDULING_NO_ROUTING", "The order has no frozen routing revision.");
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);
        var ops = mfg.RoutingOperationsFor(routingId);
        if (ops.FirstOrDefault(o => o.Seq == request.FromSeq) is null
            || ops.FirstOrDefault(o => o.Seq == request.ToSeq) is null)
            return (null!, "WIP_PRECEDENCE_ERROR", $"Routing steps {request.FromSeq}/{request.ToSeq} must exist on the frozen routing.");

        // precedence: the destination is the next routing step, or an approved deviation
        var nextSeq = ops.Where(o => o.Seq > request.FromSeq).Select(o => o.Seq).OrderBy(s => s).FirstOrDefault();
        if (request.ToSeq != nextSeq)
        {
            if (string.IsNullOrWhiteSpace(request.DeviationReason))
                return (null!, "WIP_PRECEDENCE_ERROR",
                    $"Destination step {request.ToSeq} breaks routing precedence (next is {nextSeq}) — an approved deviation is required (048 AC2).");
            if (request.DeviationReason == user.Id)
                return (null!, "WIP_PRECEDENCE_ERROR", "Maker-checker: the deviation approver cannot be the transfer maker.");
        }

        // bounded by the transferable open quantity at the source step (048 AC1)
        var openQty = Math.Max(order.PlannedQty - mfg.WipTransfersFor(orderId)
            .Where(t => t.FromSeq == request.FromSeq).Sum(t => t.Qty), 0);
        if (request.Qty > openQty)
            return (null!, "WIP_TRANSFER_OVER", $"Transferable open qty at step {request.FromSeq} is {openQty}; requested {request.Qty}.");

        var transfer = new WipTransfer(numbers.Next("wtr"), ec.TenantId, ec.EntityId, orderId,
            request.FromSeq, request.ToSeq, request.Qty, request.DeviationReason,
            request.DeviationReason is null ? null : user.Id, user.Id, DateTimeOffset.UtcNow);
        mfg.AddWipTransfer(transfer);
        audit.Log(user, "transfer", "wip_transfer", transfer.Id,
            $"WIP {request.Qty} moved step {request.FromSeq} → {request.ToSeq} on {order.OrderNo}");
        mfg.Save();
        return (new { transfer.Id, transfer.OrderId, transfer.FromSeq, transfer.ToSeq, transfer.Qty, transfer.DeviationReason, transfer.CreatedBy, transfer.CreatedAt }, null, null);
    }

    public object WipTransfers(string orderId) => mfg.WipTransfersFor(orderId).ToList();

    // ---- 049: linked corrections ----

    public (object data, string? code, string? message) ProposeCorrection(CurrentUser user, Dtos.CorrectionRequest request)
    {
        if (request.TargetType != "labor_time")
            return (null!, "VALIDATION_ERROR", "Only labor_time corrections are supported in this slice (049).");
        if (mfg.FindLaborEntry(request.TargetId) is null) return (null!, "LABOR_ENTRY_NOT_FOUND", "Target labor entry not found.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return (null!, "VALIDATION_ERROR", "A correction reason is required.");
        if (Guards.Check(request.CompensatingJson, "CompensatingJson") is { } jsonError) return (null!, "VALIDATION_ERROR", jsonError);

        var correction = new CorrectionRecord(numbers.Next("mcr"), ec.TenantId, ec.EntityId,
            request.TargetType, request.TargetId, request.Reason, request.CompensatingJson,
            "Proposed", null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddCorrection(correction);
        audit.Log(user, "propose", "correction", correction.Id, $"Correction proposed on {request.TargetId}: {request.Reason}");
        mfg.Save();
        return (ProjCorrection(correction), null, null);
    }

    public (object data, string? code, string? message) ApproveCorrection(CurrentUser approver, string correctionId, Dtos.CorrectionApprovalRequest request)
    {
        var correction = mfg.FindCorrection(correctionId);
        if (correction is null) return (null!, "CORRECTION_NOT_FOUND", "Correction not found.");
        if (correction.ProposedBy == approver.Id)
            return (null!, "CORRECTION_APPROVAL_ERROR", "Maker-checker: the proposer cannot approve their own correction (049).");
        if (correction.TargetType != "labor_time") return (null!, "VALIDATION_ERROR", "Unsupported correction target.");

        // post-close corrections defer to the finance period policy (049 edge): the TARGET's
        // work date must sit in an OPEN fiscal period
        var target = mfg.FindLaborEntry(correction.TargetId);
        if (target is null) return (null!, "LABOR_ENTRY_NOT_FOUND", "Target labor entry no longer exists.");
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(target.StartAt.UtcDateTime));
        if (guardCode is not null) return (null!, guardCode, guardMsg);

        if (!mfg.SetCorrectionApproved(correction, approver.Id, DateTimeOffset.UtcNow))
            return (null!, "CORRECTION_ALREADY_DECIDED", "The correction was already decided.");

        // the compensating event is a NEW linked entry — the original is never touched (049 AC1)
        var compensating = new LaborTimeEntry(numbers.Next("lbt"), ec.TenantId, ec.EntityId, target.OrderId,
            target.WorkerEmploymentId, target.StartAt, target.EndAt, target.Minutes * -1m,
            LaborEntryStatus.Approved, approver.Id, DateTimeOffset.UtcNow, target.Id, approver.Id, DateTimeOffset.UtcNow);
        mfg.AddLaborEntry(compensating);
        audit.Log(approver, "approve", "correction", correction.Id,
            $"Correction approved — compensating entry {compensating.Id} linked to {target.Id}");
        mfg.Save();
        return (new { correction.Id, correction.Status, compensating = ProjLabor(compensating) }, null, null);
    }

    public object Corrections() => db.CorrectionRecords
        .Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId)
        .OrderByDescending(c => c.ProposedAt).Select(ProjCorrection).ToList();

    private static object ProjLabor(LaborTimeEntry e) => new
    {
        e.Id, e.OrderId, e.WorkerEmploymentId, e.StartAt, e.EndAt, e.Minutes,
        status = e.Status.ToString(), e.ApprovedBy, e.ApprovedAt, e.CorrectionOfId, e.ReportedBy, e.ReportedAt,
    };

    private static object ProjCounter(CounterEvent e) => new
    {
        e.Id, e.OrderId, e.SourceEventId, e.CounterValue, e.DeltaQty, e.Rollover, e.ReportedBy, e.At,
    };

    private static object ProjDowntime(DowntimeEvent e) => new
    {
        e.Id, e.OrderId, e.WorkCenterId, e.StartAt, e.EndAt, e.ReasonCode, e.ReportedBy, e.At,
    };

    private static object ProjCorrection(CorrectionRecord c) => new
    {
        c.Id, c.TargetType, c.TargetId, c.Reason, c.CompensatingJson,
        status = c.Status, c.ApprovedBy, c.ApprovedAt, c.ProposedBy, c.ProposedAt,
    };
}
