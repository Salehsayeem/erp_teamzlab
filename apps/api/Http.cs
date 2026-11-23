namespace Erp.Api;

// Response envelope + middleware pipeline (05_API_Spec §1–2, CLAUDE.md backend rules)

public static class Envelope
{
    public static IResult Ok<T>(T data, int status = 200) =>
        Results.Json(new { success = true, data }, statusCode: status);

    public static IResult Fail(string code, string message, int status = 400) =>
        Results.Json(new { success = false, error = new { code, message } }, statusCode: status);
}

public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var cid = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        ctx.Items["correlationId"] = cid;
        ctx.Response.Headers["X-Correlation-Id"] = cid;
        await next(ctx);
    }
}

public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled {Path} cid={Cid}", ctx.Request.Path, ctx.Items["correlationId"]);
            // stack trace never leaves the server (CLAUDE.md security rules)
            if (!ctx.Response.HasStarted)
                await Envelope.Fail("INTERNAL", "Unexpected error. Correlation id: " + ctx.Items["correlationId"], 500).ExecuteAsync(ctx);
        }
    }
}

public sealed class CurrentUser(string id, string email, string name, string roleId, string[] permissions)
{
    public string Id { get; } = id;
    public string Email { get; } = email;
    public string Name { get; } = name;
    public string RoleId { get; } = roleId;
    public string[] Permissions { get; } = permissions;
    public bool Has(string perm) => Permissions.Contains("*") || Permissions.Contains(perm);
}

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
            var m = db.Memberships.First(x => x.UserId == user.Id);
            var role = db.Roles.First(r => r.Id == m.RoleId);
            ctx.Items["user"] = new CurrentUser(user.Id, user.Email, user.DisplayName, role.Id, role.Permissions);
            ctx.Items["entity"] = db.Entity;
        }
        await next(ctx);
    }
}
