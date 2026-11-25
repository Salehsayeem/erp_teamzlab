using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Route resolution (014) + delegation consult (015). Q-P2 fixed maker-checker: the single active
// rule resolves to one approver role; its version is snapshotted onto the requisition.
public sealed class ApprovalRouteService(IRequisitionRepository requisitions, IIdentityRepository identities, EntityContext ec) : IApprovalRouteService
{
    public (ApprovalRule? Rule, string? Code, string? Message) ResolveActiveRule()
    {
        var rule = requisitions.ActiveRule();
        return rule is null
            ? (null, "RULE_NOT_RESOLVED", "No active approval rule is configured; the route cannot be resolved (014 AC2).")
            : (rule, null, null);
    }

    // Resolves an acting user who lacks requisitions:approve to an active delegation from a
    // delegator who still holds that authority (never broader than the delegator, 015 AC1).
    // Returns the matching delegation, or a scope-denied code when delegations exist but none
    // covers this amount/category (015 AC2 — out-of-scope is denied even with other delegations).
    public (ApprovalDelegation? Delegation, string? Code, string? Message) ResolveDelegation(
        CurrentUser actor, decimal amount, string? categoryId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var d in requisitions.ActiveDelegationsFor(actor.Id, now))
        {
            if (!DelegatorCanApprove(d.DelegatorId)) continue;
            var inAmount = d.MaxAmount is null || amount <= d.MaxAmount;
            var inCategory = d.CategoryId is null || d.CategoryId == categoryId;
            if (inAmount && inCategory) return (d, null, null);
            return (null, "DELEGATION_SCOPE_DENIED",
                $"Delegation from {d.DelegatorId} does not cover amount {amount:0.##} / category {categoryId ?? "mixed"} (015 AC2).");
        }
        return (null, null, null); // no active delegation at all → plain FORBIDDEN upstream
    }

    private bool DelegatorCanApprove(string delegatorId)
    {
        var user = identities.FindActiveUserById(delegatorId);
        if (user is null) return false;
        var membership = identities.MembershipFor(user.Id, ec.TenantId, ec.EntityId);
        var role = identities.RoleById(membership.RoleId);
        return role.Permissions.Contains("*") || role.Permissions.Contains("requisitions:approve");
    }
}
