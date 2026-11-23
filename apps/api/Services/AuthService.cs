using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class AuthService(IIdentityRepository identities) : IAuthService
{
    public (object? data, string? code, string? message) Exchange(string email)
    {
        var user = identities.FindActiveUserByEmail(email);
        return user is null
            ? (null, "USER_NOT_FOUND", "No active user with that email.")
            : (new { token = user.Id, user.Id, user.Email, user.DisplayName }, null, null);
    }

    public object Me(CurrentUser u)
    {
        var role = identities.RoleById(u.RoleId);
        return new
        {
            user = new { u.Id, u.Email, u.Name },
            role = role.Name,
            permissions = u.Permissions,
            tenant = EntityContext.SeedTenant,
            legalEntity = EntityContext.SeedEntity,
        };
    }

    public object ListEntities() => new[] { EntityContext.SeedEntity };
}
