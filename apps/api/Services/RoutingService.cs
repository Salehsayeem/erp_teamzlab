using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// MF1 — routings / work centers / operation time model (ERP-MFG-024/025/026). Approved revisions
// are immutable; the approval gate walks delegation edges for cycles, checks every operation's
// work center is active and carries the required capabilities, and rejects zero/negative
// durations except ops that declare zero-duration (026 AC3).
public sealed class RoutingService(IMfgDeferredRepository mfg, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IRoutingService
{
    public object List(string? outputSku, RoutingStatus? status) =>
        mfg.AllRoutings(outputSku, status).Select(r => new
        {
            r.Id, r.OutputSku, r.Revision, status = r.Status.ToString(), r.EffectiveFrom, r.EffectiveTo,
            r.ApprovedBy, r.ApprovedAt, r.CreatedBy, r.CreatedAt,
            operations = mfg.RoutingOperationsFor(r.Id).Select(o => ProjOp(o)),
        }).ToList();

    public (RoutingRevision routing, string? code, string? message) Create(CurrentUser user, Dtos.CreateRoutingRequest request)
    {
        if (Guards.Check(request.OutputSku, "OutputSku") is { } skuError) return (null!, "VALIDATION_ERROR", skuError);
        if (request.Revision < 1) return (null!, "VALIDATION_ERROR", "Revision must be 1 or greater.");
        // effective intervals of live revisions for one sku must not overlap (BOM 014 discipline)
        var from = request.EffectiveFrom;
        var to = request.EffectiveTo;
        if (to is { } t && t < from) return (null!, "VALIDATION_ERROR", "Effective-to cannot be before effective-from.");
        foreach (var live in mfg.LiveRoutingsFor(request.OutputSku))
            if (from <= (live.EffectiveTo ?? DateOnly.MaxValue) && (to ?? DateOnly.MaxValue) >= live.EffectiveFrom)
                return (null!, "ROUTING_OVERLAP",
                    $"Effective interval overlaps live revision {live.Revision} of {request.OutputSku} (024).");

        var routing = new RoutingRevision(numbers.Next("rot"), ec.TenantId, ec.EntityId, request.OutputSku,
            request.Revision, RoutingStatus.Draft, from, to, null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddRouting(routing);
        audit.Log(user, "create", "routing_revision", routing.Id,
            $"Routing revision {request.Revision} drafted for {request.OutputSku}");
        mfg.Save();
        return (routing, null, null);
    }

    public (object data, string? code, string? message) Approve(CurrentUser approver, string routingId, string? reason)
    {
        var routing = mfg.FindRouting(routingId);
        if (routing is null) return (null!, "ROUTING_NOT_FOUND", "Routing revision not found.");
        if (routing.Status != RoutingStatus.Draft)
            return (null!, "ROUTING_STATE_ERROR", $"Only draft routings can be approved (current: {routing.Status}).");
        if (routing.CreatedBy == approver.Id)
            return (null!, "ROUTING_APPROVAL_ERROR", "Maker-checker: the routing author cannot approve their own revision (024).");

        var ops = mfg.RoutingOperationsFor(routingId);
        if (ops.Count == 0) return (null!, "ROUTING_NO_OPERATIONS", "A routing without operations cannot be approved.");

        // 026 AC3: zero/negative durations rejected except permitted zero-duration ops
        foreach (var op in ops)
            if (!op.IsZeroDuration && (op.SetupMin <= 0 || op.PerUnitMin <= 0))
                return (null!, "ROUTING_DURATION_INVALID",
                    $"Operation {op.OperationCode} (seq {op.Seq}) carries a zero/negative duration; declare it zero-duration explicitly (026).");

        // delegation graph + cycle detection with a path diagnostic (024 AC2)
        foreach (var op in ops.Where(o => o.SubRoutingId is not null))
        {
            var target = mfg.FindRouting(op.SubRoutingId!);
            if (target is null)
                return (null!, "ROUTING_NOT_FOUND", $"Operation {op.OperationCode} delegates to unknown routing {op.SubRoutingId}.");
            if (target.Id == routing.Id || DelegatesBackTo(routing.Id, target.Id, new HashSet<string> { routing.Id }))
                return (null!, "ROUTING_CYCLE",
                    $"Approval rejected: delegation cycle {routing.OutputSku}({routing.Revision}) → {op.SubRoutingId} → … → back (024 AC2).");
        }

        // capability + activity: every op's work center must be active and carry the capabilities
        foreach (var op in ops)
        {
            var wc = mfg.FindWorkCenter(op.WorkCenterId);
            if (wc is null)
                return (null!, "WORK_CENTER_NOT_FOUND", $"Operation {op.OperationCode} references unknown work center {op.WorkCenterId}.");
            if (!wc.Active)
                return (null!, "ROUTING_INACTIVE_RESOURCE", $"Operation {op.OperationCode} targets inactive work center {wc.Code} (024 AC2).");
            var missing = op.RequiredCapabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(cap => !wc.Capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(cap))
                .ToList();
            if (missing.Count > 0)
                return (null!, "ROUTING_CAPABILITY_MISSING",
                    $"Operation {op.OperationCode} requires capability '{string.Join(",", missing)}' that work center {wc.Code} does not carry (025 AC1).");
        }

        mfg.SetRoutingApproved(routing, approver.Id, DateTimeOffset.UtcNow);
        audit.Log(approver, "approve", "routing_revision", routing.Id,
            $"Routing revision {routing.Revision} of {routing.OutputSku} approved{Append(reason)}");
        mfg.Save();
        return (new { routing = Proj(routing), operations = ops.Select(ProjOp).ToList() }, null, null);
    }

    // walk the delegation graph FROM routingId — a path back to rootId is a cycle
    private bool DelegatesBackTo(string rootId, string routingId, HashSet<string> visited)
    {
        foreach (var op in mfg.RoutingOperationsFor(routingId).Where(o => o.SubRoutingId is not null))
        {
            if (op.SubRoutingId == rootId) return true;
            if (visited.Add(op.SubRoutingId!) && DelegatesBackTo(rootId, op.SubRoutingId!, visited)) return true;
        }
        return false;
    }

    public (object data, string? code, string? message) AddOperation(CurrentUser user, string routingId, Dtos.AddRoutingOperationRequest request)
    {
        var routing = mfg.FindRouting(routingId);
        if (routing is null) return (null!, "ROUTING_NOT_FOUND", "Routing revision not found.");
        if (routing.Status != RoutingStatus.Draft)
            return (null!, "ROUTING_STATE_ERROR", "Approved routings are immutable — draft a successor revision (024).");
        if (Guards.Check(request.OperationCode, "OperationCode") is { } codeError) return (null!, "VALIDATION_ERROR", codeError);
        if (mfg.FindWorkCenter(request.WorkCenterId) is null) return (null!, "WORK_CENTER_NOT_FOUND", "Work center not found.");
        if (request.Seq < 1) return (null!, "VALIDATION_ERROR", "Seq must be 1 or greater.");
        if (!request.IsZeroDuration && (request.SetupMin <= 0 || request.PerUnitMin <= 0))
            return (null!, "VALIDATION_ERROR", "Durations must be greater than 0 unless the operation is declared zero-duration (026).");
        if (Guards.Check(request.TimeUnit, "TimeUnit") is { } unitError) return (null!, "VALIDATION_ERROR", unitError);

        // 025 AC1: an operation is assignable only to resources carrying its capabilities
        var wc = mfg.FindWorkCenter(request.WorkCenterId)!;
        var missing = request.RequiredCapabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(cap => !wc.Capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(cap))
            .ToList();
        if (missing.Count > 0)
            return (null!, "ROUTING_CAPABILITY_MISSING",
                $"Work center {wc.Code} does not carry required capability '{string.Join(",", missing)}' (025 AC1).");

        var op = new RoutingOperation($"{routingId}:{request.Seq}", ec.TenantId, ec.EntityId, routingId,
            request.Seq, request.OperationCode, request.WorkCenterId, request.RequiredCapabilities,
            request.TimeUnit, request.SetupMin, request.PerUnitMin, request.IsZeroDuration,
            OperationKind.Internal, null, null, request.SubRoutingId);
        mfg.AddRoutingOperation(op);
        audit.Log(user, "add", "routing_operation", op.Id, $"Operation {request.OperationCode} added at seq {request.Seq}");
        mfg.Save();
        return (ProjOp(op), null, null);
    }

    public (object data, string? code, string? message) MoveOperation(CurrentUser user, string routingId, string opId, Dtos.MoveRoutingOperationRequest request)
    {
        var routing = mfg.FindRouting(routingId);
        if (routing is null) return (null!, "ROUTING_NOT_FOUND", "Routing revision not found.");
        if (routing.Status != RoutingStatus.Draft)
            return (null!, "ROUTING_STATE_ERROR", "Approved routings are immutable — draft a successor revision (024).");
        var op = mfg.FindRoutingOperationById(opId);
        if (op is null || op.RoutingRevisionId != routingId)
            return (null!, "ROUTING_OPERATION_NOT_FOUND", "Routing operation not found.");
        if (request.Seq < 1) return (null!, "VALIDATION_ERROR", "Seq must be 1 or greater.");
        if (mfg.FindRoutingOperation(routingId, request.Seq) is { } occupant && occupant.Id != opId)
            return (null!, "ROUTING_SEQ_CONFLICT", $"Seq {request.Seq} is already occupied by {occupant.OperationCode}.");

        mfg.SetRoutingOperationSubRouting(op, request.SubRoutingId, request.Seq);
        audit.Log(user, "move", "routing_operation", op.Id, $"Operation re-sequenced to {request.Seq} (delegation: {request.SubRoutingId ?? "none"})");
        mfg.Save();
        return (ProjOp(op), null, null);
    }

    public object Operations(string routingId) =>
        mfg.RoutingOperationsFor(routingId).Select(ProjOp).ToList();

    public (WorkCenter workCenter, string? code, string? message) CreateWorkCenter(CurrentUser user, Dtos.CreateWorkCenterRequest request)
    {
        if (Guards.Check(request.Code, "Code") is { } codeError) return (null!, "VALIDATION_ERROR", codeError);
        if (Guards.Check(request.Name, "Name") is { } nameError) return (null!, "VALIDATION_ERROR", nameError);
        if (Guards.Check(request.Capabilities, "Capabilities") is { } capError) return (null!, "VALIDATION_ERROR", capError);
        if (mfg.FindWorkCenterByCode(request.Code) is not null)
            return (null!, "WORK_CENTER_EXISTS", $"Work center code {request.Code} already exists.");

        var wc = new WorkCenter(numbers.Next("wkc"), ec.TenantId, ec.EntityId, request.Code, request.Name,
            request.Capabilities, request.Active, user.Id, DateTimeOffset.UtcNow);
        mfg.AddWorkCenter(wc);
        audit.Log(user, "create", "work_center", wc.Id, $"Work center {request.Code} created with capabilities {request.Capabilities}");
        mfg.Save();
        return (wc, null, null);
    }

    public object ListWorkCenters() => mfg.AllWorkCenters().Select(w => new
    {
        w.Id, w.Code, w.Name, w.Capabilities, w.Active, w.CreatedBy, w.CreatedAt,
    }).ToList();

    public (ResourceCalendar window, string? code, string? message) AddCalendarWindow(CurrentUser user, string workCenterId, Dtos.AddCalendarWindowRequest request)
    {
        if (mfg.FindWorkCenter(workCenterId) is null) return (null!, "WORK_CENTER_NOT_FOUND", "Work center not found.");
        if (request.WindowEnd <= request.WindowStart)
            return (null!, "VALIDATION_ERROR", "Window end must be after window start.");
        if (request.Source is not ("manual" or "maintenance"))
            return (null!, "VALIDATION_ERROR", "Source must be manual or maintenance.");

        var window = new ResourceCalendar(numbers.Next("wkc"), ec.TenantId, ec.EntityId, workCenterId,
            request.WindowStart, request.WindowEnd, request.IsAvailable, request.Source, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddCalendarWindow(window);
        audit.Log(user, "add", "resource_calendar", window.Id,
            $"{(request.IsAvailable ? "Availability" : "Outage")} window added to work center {workCenterId}");
        mfg.Save();
        return (window, null, null);
    }

    public object Calendar(string workCenterId) => mfg.CalendarFor(workCenterId).Select(c => new
    {
        c.Id, c.WorkCenterId, c.WindowStart, c.WindowEnd, c.IsAvailable, c.Source, c.SourceEventId,
    }).ToList();

    private static object Proj(RoutingRevision r) => new
    {
        r.Id, r.OutputSku, r.Revision, status = r.Status.ToString(), r.EffectiveFrom, r.EffectiveTo,
        r.ApprovedBy, r.ApprovedAt, r.CreatedBy, r.CreatedAt,
    };

    private static object ProjOp(RoutingOperation o) => new
    {
        o.Id, o.RoutingRevisionId, o.Seq, o.OperationCode, o.WorkCenterId, o.RequiredCapabilities,
        o.TimeUnit, o.SetupMin, o.PerUnitMin, o.IsZeroDuration, kind = o.Kind.ToString(),
        o.SupplierId, o.ServiceDescription, o.SubRoutingId,
    };

    private static string Append(string? reason) => string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}";
}
