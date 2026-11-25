using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P4 receipt workspace (055–059). This slice: dispatch evidence (055) — order-version-pinned,
// explicit Sent/Failed/Unknown state, retry-safe through the idempotency store; blocked while a
// change order is pending (052 AC2).
public sealed class ReceiptInspectionService(
    IReceiptInspectionRepository receipts, IPurchaseOrderChangeRepository orders,
    IAuditRepository audit, EntityContext ec) : IReceiptInspectionService
{
    public (Dispatch? Dispatch, string? Code, string? Message) SendDispatch(
        CurrentUser u, string poId, int orderVersion, DispatchState state, string? detail)
    {
        var po = orders.FindPo(poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (orders.HasPendingChange(poId))
            return (null, "PO_CHANGE_PENDING", "A change order is pending; the PO cannot be transmitted until approval completes (052 AC2).");
        if (orderVersion != po.Version)
            return (null, "PO_ORDER_VERSION_CONFLICT", $"Dispatch names version {orderVersion} but the order is at version {po.Version}.");
        if (!Enum.IsDefined(state))
            return (null, "VALIDATION_ERROR", "Dispatch state must be Sent, Failed or Unknown (055 AC3).");

        var dispatch = new Dispatch($"{poId}:dsp{DateTimeOffset.UtcNow.Ticks}", ec.TenantId, ec.EntityId,
            poId, orderVersion, state, detail, u.Id, DateTimeOffset.UtcNow);
        receipts.AddDispatch(dispatch);
        if (state is DispatchState.Sent && po.Status is PoStatus.Approved)
            orders.SetField(po, nameof(PurchaseOrder.Status), PoStatus.Sent);
        audit.Log(u, "dispatch", "purchase_order", poId, $"Dispatch evidence {dispatch.Id}: {state} (order v{orderVersion})");
        audit.Emit(u, "procurement.po.dispatched", new { poId, orderVersion, state, dispatchId = dispatch.Id });
        receipts.Save();
        return (dispatch, null, null);
    }
}
