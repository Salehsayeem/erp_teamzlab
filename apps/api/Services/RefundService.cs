using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Refunds (ERP-SALES-062): a posted credit NEVER completes a refund on its own — the refund
// request links an approved credit and waits for a finance officer (refunds:approve, R-FIN,
// approver ≠ requester). House reversal pattern: the credit's journal already exists; the
// refund records the cash-out decision against it with provenance.
public sealed class RefundService(
    IInvoiceRepository invoices, ISalesDeferredRepository deferred, IAuditRepository audit,
    EntityContext ec) : IRefundService
{
    public (object? data, bool replayed, string? code, string? message) Request(CurrentUser u, string adjustmentId)
    {
        if (!u.Has("returns:write")) return (null, false, "FORBIDDEN", "returns:write permission required.");
        var adjustment = invoices.FindAdjustment(adjustmentId);
        if (adjustment is null) return (null, false, "ADJUSTMENT_NOT_FOUND", "Adjustment not found.");
        if (adjustment.Kind != AdjustmentKind.Credit)
            return (null, false, "REFUND_STATE_ERROR", "Refunds link approved CREDIT adjustments only.");
        if (adjustment.Status != AdjustmentStatus.Approved)
            return (null, false, "REFUND_STATE_ERROR", "The credit adjustment must be approved before a refund can be requested.");

        // idempotent on retry — one refund per credit adjustment
        var existing = deferred.FindRefundByAdjustment(adjustmentId);
        if (existing is not null) return (View(existing), true, null, null);

        var refund = new RefundRequest($"{adjustmentId}:REF", ec.TenantId, ec.EntityId, adjustmentId,
            adjustment.InvoiceId, adjustment.Total, RefundStatus.Requested, u.Id, DateTimeOffset.UtcNow);
        deferred.AddRefund(refund);
        audit.Log(u, "request_refund", "refund_request", refund.Id,
            $"Refund {adjustment.Total:0.00} requested against credit {adjustmentId}");
        audit.Emit(u, "sales.refund.requested", new { refundId = refund.Id, adjustmentId, amount = adjustment.Total });
        deferred.Save();
        return (View(refund), false, null, null);
    }

    public (object? data, string? code, string? message) Approve(CurrentUser u, string refundId, string? reason)
    {
        var refund = deferred.FindRefund(refundId);
        if (refund is null) return (null, "REFUND_NOT_FOUND", "Refund request not found.");
        // money leaves the company — finance-only authority (062), and never the requester
        if (!u.Has("refunds:approve")) return (null, "FORBIDDEN", "refunds:approve permission required (R-FIN).");
        if (refund.RequestedBy == u.Id)
            return (null, "REFUND_STATE_ERROR", "Maker-checker: the requester cannot approve their own refund.");
        deferred.SetRefundStatus(refund, RefundStatus.Approved, u.Id);
        audit.Log(u, "approve_refund", "refund_request", refundId, $"Refund {refund.Amount:0.00} approved: {reason ?? "no reason given"}");
        audit.Emit(u, "sales.refund.approved", new { refundId, amount = refund.Amount, approvedBy = u.Id });
        deferred.Save();
        return (View(deferred.FindRefund(refundId)!), null, null);
    }

    public (object? data, string? code, string? message) List() =>
        (deferred.FindRefunds().Select(View).ToList(), null, null);

    private static object View(RefundRequest r) => new
    {
        r.Id, r.AdjustmentId, r.InvoiceId, r.Amount, r.Status, requestedBy = r.RequestedBy,
        r.RequestedAt, r.ApprovedBy, r.ApprovedAt, r.RejectionReason,
    };
}
