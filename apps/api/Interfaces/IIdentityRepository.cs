namespace Erp.Api.Interfaces;

// Task-2 §3 — users / roles / memberships behind the auth slice
public interface IIdentityRepository
{
    User? FindActiveUserByEmail(string email);
    User? FindActiveUserById(string userId);
    Role RoleById(string roleId);
    Membership MembershipFor(string userId, string tenantId, string legalEntityId);
    int Save();
}
