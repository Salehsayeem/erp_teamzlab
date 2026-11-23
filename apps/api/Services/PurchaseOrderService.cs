using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class PurchaseOrderService(
    ISupplierRepository suppliers, IPurchaseOrderRepository orders, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec) : IPurchaseOrderService
{
    public object List() =>
        orders.All().Select(p => new
        {
            p.Id, p.SupplierId, p.Status, p.Lines, p.CreatedBy, p.CreatedAt, p.ApprovedBy, p.ApprovedAt, p.RejectionReason,
            total = p.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
        }).ToList();

    public (PurchaseOrder? order, string? code, string? message) Create(CurrentUser u, string supplierId, IReadOnlyList<PoLine> lines)
    {
        if (!suppliers.Exists(supplierId))
            return (null, "SUPPLIER_NOT_FOUND", "Supplier does not exist.");
        var po = new PurchaseOrder(numbers.Next("PO"), ec.TenantId, ec.EntityId, supplierId, PoStatus.Draft, lines, u.Id, DateTimeOffset.UtcNow);
        orders.Add(po);
        audit.Log(u, "create", "purchase_order", po.Id, $"PO created for supplier {supplierId} with {lines.Count} line(s)");
        orders.Save();
        return (po, null, null);
    }

    public (PurchaseOrder? order, string? code, string? message) Submit(CurrentUser u, string id)
    {
        var po = orders.Find(id);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (po.Status != PoStatus.Draft) return (null, "PO_NOT_SUBMITTABLE", "Only draft POs can be submitted.");
        orders.MarkSubmitted(po);
        audit.Log(u, "submit", "purchase_order", id, "PO submitted for approval");
        orders.Save();
        return (po, null, null);
    }

    public (PurchaseOrder? order, string? code, string? message) Approve(CurrentUser approver, string poId, string? reason)
    {
        var po = orders.Find(poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (po.Status != PoStatus.PendingApproval) return (null, "PO_APPROVAL_ERROR", $"Only pending POs can be approved (current: {po.Status}).");
        if (po.CreatedBy == approver.Id) return (null, "PO_APPROVAL_ERROR", "Maker-checker: the creator cannot approve their own purchase order.");
        if (!approver.Has("po:approve")) return (null, "FORBIDDEN", "po:approve permission required.");

        if (reason is null) orders.MarkApproved(po, approver.Id);
        else orders.MarkRejected(po, approver.Id, reason);
        audit.Log(approver, reason is null ? "approve" : "reject", "purchase_order", po.Id,
            reason is null ? $"PO approved by {approver.Email}" : $"PO rejected: {reason}");
        if (reason is null) audit.Emit(approver, "procurement.po.approved", new { poId = po.Id, approver = approver.Id });
        orders.Save();
        return (po, null, null);
    }
}
