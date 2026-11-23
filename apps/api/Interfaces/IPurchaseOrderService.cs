namespace Erp.Api.Interfaces;

public interface IPurchaseOrderService
{
    object List();
    (PurchaseOrder? order, string? code, string? message) Create(CurrentUser user, string supplierId, IReadOnlyList<PoLine> lines);
    (PurchaseOrder? order, string? code, string? message) Submit(CurrentUser user, string id);
    (PurchaseOrder? order, string? code, string? message) Approve(CurrentUser approver, string id, string? reason);
}
