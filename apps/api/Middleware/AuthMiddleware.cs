namespace Erp.Api.Middleware;

public sealed class AuthMiddleware(RequestDelegate next)
{
    // shortcut: stub auth — token is just the user id, no IdP yet; upgrade when D-13 provider chosen
    public async Task InvokeAsync(HttpContext ctx, Data.Db db)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            var token = ctx.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "", StringComparison.Ordinal);
            var user = token is null ? null : db.WithLock(() => db.Users.FirstOrDefault(u => u.Id == token && u.Active));
            if (user is null)
            {
                await Envelope.Fail("UNAUTHORIZED", "Valid bearer token required.", 401).ExecuteAsync(ctx);
                return;
            }
            var m = db.Memberships.First(x => x.UserId == user.Id && x.TenantId == db.Tenant.Id && x.LegalEntityId == db.Entity.Id);
            var role = db.Roles.First(r => r.Id == m.RoleId);
            ctx.Items["user"] = new CurrentUser(user.Id, user.Email, user.DisplayName, role.Id, role.Permissions);
            ctx.Items["entity"] = db.Entity;
        }
        await next(ctx);
    }
}
