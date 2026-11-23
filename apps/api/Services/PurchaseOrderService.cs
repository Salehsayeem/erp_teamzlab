using Erp.Api.Interfaces;
using Erp.Api.Data;

namespace Erp.Api.Services;

public sealed class PurchaseOrderService(Db db) : IPurchaseOrderService
{
    public object List() =>
        db.WithLock(() => db.PurchaseOrders.Select(p => new
        {
            p.Id, p.SupplierId, p.Status, p.Lines, p.CreatedBy, p.CreatedAt, p.ApprovedBy, p.ApprovedAt, p.RejectionReason,
            total = p.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
        }).ToList());

    public (PurchaseOrder? order, string? code, string? message) Create(CurrentUser u, string supplierId, IReadOnlyList<PoLine> lines) =>
        db.WithLock(() =>
        {
            if (!db.Suppliers.Any(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.Id == supplierId))
                return ((PurchaseOrder?)null, (string?)"SUPPLIER_NOT_FOUND", (string?)"Supplier does not exist.");
            var po = new PurchaseOrder(db.NextDoc("PO"), db.Tenant.Id, db.Entity.Id, supplierId, PoStatus.Draft, lines, u.Id, DateTimeOffset.UtcNow);
            db.PurchaseOrders.Add(po);
            Audit.Log(db, u, "create", "purchase_order", po.Id, $"PO created for supplier {supplierId} with {lines.Count} line(s)");
            return ((PurchaseOrder?)po, (string?)null, (string?)null);
        });

    public (PurchaseOrder? order, string? code, string? message) Submit(CurrentUser u, string id) =>
        db.WithLock(() =>
        {
            var po = db.PurchaseOrders.FirstOrDefault(p => p.TenantId == db.Tenant.Id && p.LegalEntityId == db.Entity.Id && p.Id == id);
            if (po is null) return ((PurchaseOrder?)null, (string?)"PO_NOT_FOUND", (string?)"Purchase order not found.");
            if (po.Status != PoStatus.Draft) return ((PurchaseOrder?)null, (string?)"PO_NOT_SUBMITTABLE", (string?)"Only draft POs can be submitted.");
            db.PurchaseOrders[db.PurchaseOrders.IndexOf(po)] = po with { Status = PoStatus.PendingApproval };
            Audit.Log(db, u, "submit", "purchase_order", id, "PO submitted for approval");
            return ((PurchaseOrder?)db.PurchaseOrders.First(p => p.Id == id), (string?)null, (string?)null);
        });

    public (PurchaseOrder? order, string? code, string? message) Approve(CurrentUser approver, string poId, string? reason)
    {
        var po = db.PurchaseOrders.FirstOrDefault(p => p.TenantId == db.Tenant.Id && p.LegalEntityId == db.Entity.Id && p.Id == poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (po.Status != PoStatus.PendingApproval) return (null, "PO_APPROVAL_ERROR", $"Only pending POs can be approved (current: {po.Status}).");
        if (po.CreatedBy == approver.Id) return (null, "PO_APPROVAL_ERROR", "Maker-checker: the creator cannot approve their own purchase order.");
        if (!approver.Has("po:approve")) return (null, "FORBIDDEN", "po:approve permission required.");

        var approved = reason is null
            ? po with { Status = PoStatus.Approved, ApprovedBy = approver.Id, ApprovedAt = DateTimeOffset.UtcNow }
            : po with { Status = PoStatus.Rejected, ApprovedBy = approver.Id, ApprovedAt = DateTimeOffset.UtcNow, RejectionReason = reason };
        db.PurchaseOrders[db.PurchaseOrders.IndexOf(po)] = approved;
        Audit.Log(db, approver, reason is null ? "approve" : "reject", "purchase_order", po.Id,
            reason is null ? $"PO approved by {approver.Email}" : $"PO rejected: {reason}");
        if (reason is null) Audit.Emit(db, approver, "procurement.po.approved", new { poId = po.Id, approver = approver.Id });
        return (approved, null, null);
    }
}
