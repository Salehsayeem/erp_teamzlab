using Erp.Api;

namespace Erp.Api.Interfaces;

// Approval route resolution (014) + delegation consult (015). Q-P2: no matrix engine — the active
// rule row is resolved as-is and its version snapshot travels with the approval.
public interface IApprovalRouteService
{
    (ApprovalRule? Rule, string? Code, string? Message) ResolveActiveRule();
    (ApprovalDelegation? Delegation, string? Code, string? Message) ResolveDelegation(CurrentUser actor, decimal amount, string? categoryId);
}
