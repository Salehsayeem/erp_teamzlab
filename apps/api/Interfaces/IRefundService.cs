using Erp.Api;

namespace Erp.Api.Interfaces;

// 062: refunds are the money-leaves-the-company boundary — approval is refunds:approve
// (R-FIN only), approver ≠ requester, idempotent per credit adjustment
public interface IRefundService
{
    (object? data, bool replayed, string? code, string? message) Request(CurrentUser u, string adjustmentId);
    (object? data, string? code, string? message) Approve(CurrentUser u, string refundId, string? reason);
    (object? data, string? code, string? message) List();
}
