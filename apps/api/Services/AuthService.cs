using Erp.Api.Interfaces;
using Erp.Api.Data;

namespace Erp.Api.Services;

public sealed class AuthService(Db db) : IAuthService
{
    public (object? data, string? code, string? message) Exchange(string email)
    {
        var user = db.WithLock(() => db.Users.FirstOrDefault(u => u.Email == email && u.Active));
        return user is null
            ? (null, "USER_NOT_FOUND", "No active user with that email.")
            : (new { token = user.Id, user.Id, user.Email, user.DisplayName }, null, null);
    }

    public object Me(CurrentUser u)
    {
        var (role, entity) = db.WithLock(() => (db.Roles.First(r => r.Id == u.RoleId), db.Entity));
        return new
        {
            user = new { u.Id, u.Email, u.Name },
            role = role.Name,
            permissions = u.Permissions,
            tenant = db.Tenant,
            legalEntity = entity,
        };
    }

    public object ListEntities() => new[] { db.Entity };
}
