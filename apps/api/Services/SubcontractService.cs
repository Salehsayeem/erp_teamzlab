using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// MF6 — subcontract operations (ERP-MFG-050/051/052). Custody rows MUST reference an accepted
// inventory movement (issue, transfer, receipt) — the send/return reconciliation reads only from
// here (051). Receipts are quality-gated: an approved quality plan on the output item holds the
// receipt until an accepted disposition lands (052). The completion gate (in
// ProductionOrderService) refuses completion beyond the accepted returned quantity.
public sealed class SubcontractService(
    IMfgDeferredRepository mfg, IManufacturingRepository landed, IInventoryRepository inventory,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : ISubcontractService
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // a subcontract operation on a DRAFT routing revision — supplier + service config recorded now,
    // validated at RELEASE (050 AC1) so the release gate can name a blocked supplier
    public (object data, string? code, string? message) AddOperation(CurrentUser user, string routingId, Dtos.AddSubcontractOperationRequest request)
    {
        var routing = mfg.FindRouting(routingId);
        if (routing is null) return (null!, "ROUTING_NOT_FOUND", "Routing revision not found.");
        if (routing.Status != RoutingStatus.Draft)
            return (null!, "ROUTING_STATE_ERROR", "Approved routings are immutable — draft a successor revision (024).");
        if (Guards.Check(request.OperationCode, "OperationCode") is { } codeError) return (null!, "VALIDATION_ERROR", codeError);
        if (mfg.FindWorkCenter(request.WorkCenterId) is null) return (null!, "WORK_CENTER_NOT_FOUND", "Work center not found.");
        var supplier = db.Suppliers.FirstOrDefault(s => s.Id == request.SupplierId
            && s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
        if (supplier is null) return (null!, "SUPPLIER_NOT_FOUND", "Supplier not found.");
        if (Guards.Check(request.ServiceDescription, "ServiceDescription") is { } svcError) return (null!, "VALIDATION_ERROR", svcError);
        if (request.Seq < 1) return (null!, "VALIDATION_ERROR", "Seq must be 1 or greater.");

        var op = new RoutingOperation($"{routingId}:{request.Seq}", ec.TenantId, ec.EntityId, routingId,
            request.Seq, request.OperationCode, request.WorkCenterId, "", "min",
            request.SetupMin, request.PerUnitMin, request.SetupMin <= 0 && request.PerUnitMin <= 0,
            OperationKind.Subcontract, supplier.Id, request.ServiceDescription, null);
        mfg.AddRoutingOperation(op);
        audit.Log(user, "add", "subcontract_operation", op.Id,
            $"Subcontract operation {request.OperationCode} added at seq {request.Seq} (supplier {supplier.Name})");
        mfg.Save();
        return (ProjOp(op), null, null);
    }

    public (object data, string? code, string? message) RecordCustody(CurrentUser user, Dtos.CustodyRequest request)
    {
        var order = landed.FindOrder(request.OrderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        var routingId = order.ReleasedRoutingRevisionId ?? order.PlannedRoutingRevisionId;
        var op = routingId is null ? null : mfg.RoutingOperationsFor(routingId).FirstOrDefault(o => o.Seq == request.RoutingOperationSeq);
        if (op is null || op.Kind != OperationKind.Subcontract)
            return (null!, "SUBCONTRACT_OP_NOT_FOUND", $"Routing step {request.RoutingOperationSeq} is not a subcontract operation.");
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);

        // custody MUST reference an accepted inventory movement (051 AC1)
        if (!MovementExists(request.InventoryMovementId))
            return (null!, "MOVEMENT_NOT_FOUND",
                $"Inventory movement {request.InventoryMovementId} does not exist for this entity — custody references accepted movements only.");

        var custody = new SubcontractCustody(numbers.Next("sub"), ec.TenantId, ec.EntityId, request.OrderId,
            request.RoutingOperationSeq, request.Kind, request.Qty, request.InventoryMovementId,
            user.Id, DateTimeOffset.UtcNow);
        mfg.AddCustody(custody);
        audit.Log(user, "record", "subcontract_custody", custody.Id,
            $"{request.Kind} custody {request.Qty} on {order.OrderNo} (movement {request.InventoryMovementId})");
        mfg.Save();
        return (ProjCustody(custody), null, null);
    }

    // accepted movements: material issues, stock transfers, receipts — scoped, any of the three
    private bool MovementExists(string movementId) =>
        db.MaterialIssues.Any(i => i.Id == movementId && i.TenantId == ec.TenantId && i.LegalEntityId == ec.EntityId)
        || db.Transfers.Any(t => t.Id == movementId && t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId)
        || db.Receipts.Any(r => r.Id == movementId && r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    public object Custodies(string orderId)
    {
        var rows = mfg.CustodiesFor(orderId);
        var sent = rows.Where(c => c.Kind == CustodyKind.Send).Sum(c => c.Qty);
        var returned = rows.Where(c => c.Kind == CustodyKind.Return).Sum(c => c.Qty);
        return new
        {
            orderId,
            rows = rows.Select(ProjCustody).ToList(),
            reconciliation = new { sent, returned, outstanding = sent - returned }, // 051 AC2
        };
    }

    // 052: quality-required output is held until an accepted disposition lands; otherwise the
    // receipt accepts directly and feeds the completion gate
    public (object data, string? code, string? message) Receive(CurrentUser user, string orderId, Dtos.SubcontractReceiptRequest request)
    {
        var order = landed.FindOrder(orderId);
        if (order is null) return (null!, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        var routingId = order.ReleasedRoutingRevisionId ?? order.PlannedRoutingRevisionId;
        var op = routingId is null ? null : mfg.RoutingOperationsFor(routingId).FirstOrDefault(o => o.Seq == request.RoutingOperationSeq);
        if (op is null || op.Kind != OperationKind.Subcontract)
            return (null!, "SUBCONTRACT_OP_NOT_FOUND", $"Routing step {request.RoutingOperationSeq} is not a subcontract operation.");
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);

        var qualityRequired = mfg.ApprovedQualityPlanFor(order.OutputSku, Today) is not null;
        QualityHold? hold = null;
        if (qualityRequired)
        {
            hold = new QualityHold(numbers.Next("qhd"), ec.TenantId, ec.EntityId, order.OutputSku,
                null, request.Qty, orderId, null, null, QualityHoldStatus.Open, null, null, null,
                user.Id, DateTimeOffset.UtcNow, null, null);
            mfg.AddQualityHold(hold);
        }
        var receipt = new SubcontractReceipt(numbers.Next("sub"), ec.TenantId, ec.EntityId, orderId,
            request.RoutingOperationSeq, request.Qty, qualityRequired ? 0m : request.Qty,
            qualityRequired ? "PendingQuality" : "Accepted", hold?.Id, user.Id, DateTimeOffset.UtcNow);
        mfg.AddSubcontractReceipt(receipt);
        audit.Log(user, "receive", "subcontract_receipt", receipt.Id,
            $"Receipt {request.Qty} on {order.OrderNo} — {(qualityRequired ? "held pending quality" : "accepted")}");
        mfg.Save();
        return (ProjReceipt(receipt), null, null);
    }

    public object Receipts(string orderId) => mfg.SubcontractReceiptsFor(orderId).Select(ProjReceipt).ToList();

    private static object ProjOp(RoutingOperation o) => new
    {
        o.Id, o.RoutingRevisionId, o.Seq, o.OperationCode, o.WorkCenterId, kind = o.Kind.ToString(),
        o.SupplierId, o.ServiceDescription, o.SetupMin, o.PerUnitMin,
    };

    private static object ProjCustody(SubcontractCustody c) => new
    {
        c.Id, c.OrderId, c.RoutingOperationSeq, kind = c.Kind.ToString(), c.Qty, c.InventoryMovementId, c.CreatedBy, c.CreatedAt,
    };

    private static object ProjReceipt(SubcontractReceipt r) => new
    {
        r.Id, r.OrderId, r.RoutingOperationSeq, r.Qty, r.AcceptedQty, r.Status, r.QualityHoldId, r.CreatedBy, r.CreatedAt,
    };
}
