using Erp.Api.Interfaces;

namespace Erp.Api.Middleware;

// stub auth — token is just the user id, verified against the users table via the identity
// repository; the scoped EntityContext is fed from the verified membership (task-2 §3)
public sealed class AuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, IIdentityRepository identities, EntityContext ec)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            var token = ctx.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "", StringComparison.Ordinal);
            var user = token is null ? null : identities.FindActiveUserById(token);
            if (user is null)
            {
                await Envelope.Fail("UNAUTHORIZED", "Valid bearer token required.", 401).ExecuteAsync(ctx);
                return;
            }
            var m = identities.MembershipFor(user.Id, ec.TenantId, ec.EntityId);
            var role = identities.RoleById(m.RoleId);
            ec.Set(m.TenantId, m.LegalEntityId);
            ctx.Items["user"] = new CurrentUser(user.Id, user.Email, user.DisplayName, role.Id, role.Permissions);
            ctx.Items["entity"] = EntityContext.SeedEntity;
        }
        await next(ctx);
    }
}
