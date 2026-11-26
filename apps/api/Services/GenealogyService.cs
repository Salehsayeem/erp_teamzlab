using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// MF10 — genealogy, rework, close reconciliation (ERP-MFG-064/065/066/068/070). Transformation
// events are immutable and captured forward-only from the landed completion path (Q-MF4) —
// pre-landing orders lack trace and the query reports that as an explicit GAP, never an
// inference (066). Rework orders are bounded by the identified affected quantity absent an
// approved adjustment and gated on a finalized passing reinspection (068). Close reconciliation
// exposes every quantity bucket with its recorded variances; an unresolved CRITICAL variance
// blocks close absent an approved override decision (070).
public sealed class GenealogyService(
    IMfgDeferredRepository mfg, IManufacturingRepository landed, IProductionOrderService production,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IGenealogyService
{
    // 064: bidirectional trace over immutable transformation events; unresolved input edges are
    // listed as gaps — completeness flips to incomplete the moment a link is missing
    public object Trace(string? lotSerial, string? outputSku)
    {
        var events = mfg.TransformationEventsFor(lotSerial, outputSku);
        var producedSkus = events.Select(e => e.OutputSku).ToHashSet();
        var gaps = new List<object>();
        foreach (var evt in events)
            foreach (var input in ParseInputs(evt.InputsJson).Where(i => i.Qty > 0))
                if (!producedSkus.Contains(input.ItemSku))
                    gaps.Add(new { itemSku = input.ItemSku, reason = "no producing transformation event (purchased material or pre-capture order)" });

        return new
        {
            events = events.Select(e => new
            {
                e.Id, e.OrderId, e.OutputSku, e.OutputQty, e.LotSerial, inputs = ParseInputs(e.InputsJson),
                e.EmittedBy, e.At,
            }).ToList(),
            gaps,
            complete = events.Count > 0 && gaps.Count == 0,
        };
    }

    private static List<(string ItemSku, decimal Qty)> ParseInputs(string json) =>
        (JsonSerializer.Deserialize<JsonElement>(json, Idem.JsonOptions) is { } doc
            ? doc.EnumerateArray().Select(i => (i.GetProperty("itemSku").GetString()!, i.GetProperty("qty").GetDecimal())).ToList()
            : []);

    // ---- 068: rework orders ----

    public (object data, string? code, string? message) CreateRework(CurrentUser user, Dtos.CreateReworkRequest request)
    {
        var order = landed.FindOrder(request.SourceOrderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Source order not found.");
        if (Guards.Check(request.ItemSku, "ItemSku") is { } skuError) return (null!, "VALIDATION_ERROR", skuError);
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);

        // identified affected quantity bounds the rework (068 AC1) — an approved adjustment
        // (reason + approver ≠ maker) is the only override
        decimal? affected = null;
        if (request.NonconformanceId is { } ncId)
            affected = mfg.FindNonconformance(ncId)?.AffectedQty;
        else if (request.HoldId is { } holdId)
            affected = mfg.FindQualityHold(holdId)?.Qty;
        if (affected is null)
            return (null!, "VALIDATION_ERROR", "Rework needs an identified affected quantity — attach an NC or hold.");

        var adjustment = !string.IsNullOrWhiteSpace(request.AdjustmentReason);
        if (request.Qty > affected && !adjustment)
            return (null!, "REWORK_QTY_OVER",
                $"Rework of {request.Qty} exceeds the identified affected quantity ({affected}) — an approved adjustment is required (068 AC1).");

        var rework = new ReworkOrder(numbers.Next("rwk"), ec.TenantId, ec.EntityId, request.NonconformanceId,
            request.HoldId, request.SourceOrderId, request.ItemSku, request.Qty, ReworkStatus.Draft,
            request.AdjustmentReason, adjustment ? user.Id : null, null, null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddReworkOrder(rework);
        audit.Log(user, "create", "rework_order", rework.Id,
            $"Rework drafted: {request.Qty} x {request.ItemSku} from {order.OrderNo} (affected {affected})");
        mfg.Save();
        return (ProjRework(rework), null, null);
    }

    public (object data, string? code, string? message) ApproveRework(CurrentUser approver, string reworkId, Dtos.ReworkApprovalRequest request)
    {
        var rework = mfg.FindReworkOrder(reworkId);
        if (rework is null) return (null!, "REWORK_NOT_FOUND", "Rework order not found.");
        if (rework.CreatedBy == approver.Id)
            return (null!, "REWORK_APPROVAL_ERROR", "Maker-checker: the rework creator cannot approve their own order (068).");
        if (rework.Status != ReworkStatus.Draft)
            return (null!, "REWORK_STATE_ERROR", $"Only draft rework can be approved (current: {rework.Status}).");
        if (!mfg.SetReworkStatus(rework, ReworkStatus.Approved, approver.Id, DateTimeOffset.UtcNow))
            return (null!, "REWORK_STATE_ERROR", "Approval raced another decision.");
        audit.Log(approver, "approve", "rework_order", rework.Id, "Rework approved");
        mfg.Save();
        return (ProjRework(rework), null, null);
    }

    public (object data, string? code, string? message) LinkReinspection(CurrentUser user, string reworkId, Dtos.ReworkReinspectionRequest request)
    {
        var rework = mfg.FindReworkOrder(reworkId);
        if (rework is null) return (null!, "REWORK_NOT_FOUND", "Rework order not found.");
        var inspection = mfg.FindInspection(request.InspectionId);
        if (inspection is null || inspection.Status != QualityInspectionStatus.Finalized)
            return (null!, "REINSPECTION_REQUIRED", "A FINALIZED inspection is required for the reinspection gate (068 AC3).");
        mfg.SetReworkReinspection(rework, request.InspectionId);
        audit.Log(user, "link", "rework_reinspection", reworkId, $"Reinspection {request.InspectionId} linked");
        mfg.Save();
        return (ProjRework(rework), null, null);
    }

    public (object data, string? code, string? message) CompleteRework(CurrentUser user, string reworkId)
    {
        var rework = mfg.FindReworkOrder(reworkId);
        if (rework is null) return (null!, "REWORK_NOT_FOUND", "Rework order not found.");
        if (rework.Status != ReworkStatus.Approved)
            return (null!, "REWORK_STATE_ERROR", "Only approved rework completes.");
        // 068 AC3: the reinspection gate — a finalized inspection that PASSED all typed results
        if (rework.ReinspectionInspectionId is not { } inspectionId || mfg.FindInspection(inspectionId) is not { } inspection
            || inspection.Status != QualityInspectionStatus.Finalized
            || mfg.ResultsFor(inspectionId).Any(r => r.EvaluatedPass == false))
            return (null!, "REINSPECTION_REQUIRED",
                "Completion requires a finalized, PASSING reinspection before release (068 AC3).");
        if (!mfg.SetReworkStatus(rework, ReworkStatus.Completed, user.Id, DateTimeOffset.UtcNow))
            return (null!, "REWORK_STATE_ERROR", "Completion raced another decision.");
        audit.Log(user, "complete", "rework_order", reworkId, "Rework completed past the reinspection gate");
        mfg.Save();
        return (ProjRework(rework), null, null);
    }

    public object ListRework() => mfg.AllReworkOrders().Select(r => ProjRework(r)).ToList();

    // ---- 070: close reconciliation ----

    public (object data, string? code, string? message) CloseReconciliation(string orderId)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        var issues = landed.IssuesFor(orderId);
        var issued = issues.Where(i => i.Kind == MovementKind.Issue).Sum(i => i.Qty);
        var returned = issues.Where(i => i.Kind == MovementKind.Return).Sum(i => i.Qty);
        var scrapped = landed.ScrapsFor(orderId).Sum(s => s.Qty);
        var reworked = mfg.AllReworkOrders().Where(r => r.SourceOrderId == orderId).Sum(r => r.Qty);
        var custodies = mfg.CustodiesFor(orderId);
        var held = mfg.OpenHoldsForOrder(orderId).Sum(h => h.Qty);
        var (check, _, _) = production.CloseCheck(orderId);
        dynamic ledger = check!;

        var buckets = new
        {
            planned = order.PlannedQty,
            issued,
            returned,
            consumed = issued - returned,
            completed = order.CompletedQty,
            scrapped,
            reworked,
            subcontractSent = custodies.Where(c => c.Kind == CustodyKind.Send).Sum(c => c.Qty),
            subcontractReturned = custodies.Where(c => c.Kind == CustodyKind.Return).Sum(c => c.Qty),
            heldQty = held,
            wipRemainder = (decimal)ledger.wipRemainder,
        };

        // every recorded variance carries owner + age + disposition (070 AC1–2)
        var variances = mfg.VariancesFor(orderId).Select(v => new
        {
            v.Id, v.BucketCode, v.QtyVariance, v.Owner, v.Disposition,
            v.IsCritical, v.Reason, v.CreatedBy,
            ageDays = (int)Math.Floor((DateTimeOffset.UtcNow - v.CreatedAt).TotalDays),
            v.DecidedBy, v.DecidedAt,
        }).ToList();

        var unresolvedCritical = variances.Where(v => v.IsCritical && v.Disposition == "Open").ToList();
        return (new
        {
            orderId, orderNo = order.OrderNo, buckets, variances,
            criticalUnresolved = unresolvedCritical.Count,
            closePolicy = unresolvedCritical.Count == 0
                ? "clear"
                : "blocked — unresolved critical variances require an accepted/overridden disposition (070 AC3)",
        }, null, null);
    }

    public (object data, string? code, string? message) DisposeVariance(CurrentUser user, string orderId, Dtos.VarianceDispositionRequest request)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (Guards.Check(request.BucketCode, "BucketCode") is { } bucketError) return (null!, "VALIDATION_ERROR", bucketError);

        var variance = new VarianceLedger(numbers.Next("vln"), ec.TenantId, ec.EntityId, orderId,
            request.BucketCode, request.QtyVariance, request.Owner ?? user.Id, "Open", null,
            request.IsCritical, null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddVariance(variance);
        audit.Log(user, "record", "variance", variance.Id,
            $"Variance recorded on {order.OrderNo}/{request.BucketCode}: {request.QtyVariance} (critical: {request.IsCritical})");
        mfg.Save();
        return (new { variance.Id, variance.OrderId, variance.BucketCode, variance.QtyVariance, variance.Owner, variance.Disposition, variance.IsCritical, variance.CreatedBy }, null, null);
    }

    public (object data, string? code, string? message) DecideVariance(CurrentUser user, string varianceId, Dtos.VarianceDecisionRequest request)
    {
        var variance = mfg.FindVariance(varianceId);
        if (variance is null) return (null!, "VARIANCE_NOT_FOUND", "Variance not found.");
        if (request.Disposition is not ("accepted" or "overridden"))
            return (null!, "VALIDATION_ERROR", "Disposition must be accepted or overridden.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (null!, "VALIDATION_ERROR", "A decision reason is required (070).");
        if (variance.CreatedBy == user.Id)
            return (null!, "VARIANCE_DECISION_ERROR", "Maker-checker: the variance recorder cannot decide their own variance (070).");
        if (variance.Disposition != "Open")
            return (null!, "VARIANCE_STATE_ERROR", "The variance is already decided.");

        mfg.SetVarianceDisposition(variance, request.Disposition == "accepted" ? "Accepted" : "Overridden",
            request.Reason, user.Id, DateTimeOffset.UtcNow);
        audit.Log(user, "decide", "variance", varianceId, $"Variance {variance.BucketCode} {request.Disposition}: {request.Reason}");
        mfg.Save();
        return (new { variance.Id, variance.BucketCode, variance.Disposition, variance.Reason, variance.DecidedBy, variance.DecidedAt }, null, null);
    }

    private static object ProjRework(ReworkOrder r) => new
    {
        r.Id, r.NonconformanceId, r.HoldId, r.SourceOrderId, r.ItemSku, r.Qty, status = r.Status.ToString(),
        r.AdjustmentReason, r.AdjustmentApprovedBy, r.ReinspectionInspectionId, r.CompletedBy, r.CompletedAt, r.CreatedBy, r.CreatedAt,
    };
}
