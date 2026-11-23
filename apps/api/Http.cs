namespace Erp.Api;

// Response envelope + middleware pipeline (05_API_Spec §1–2, CLAUDE.md backend rules)

public static class Envelope
{
    public static IResult Ok<T>(T data, int status = 200) =>
        Results.Json(new { success = true, data }, statusCode: status);

    public static IResult Fail(string code, string message, int status = 400) =>
        Results.Json(new { success = false, error = new { code, message } }, statusCode: status);

    // payload forms for Idem.Wrap (serialized once, stored, replayed verbatim)
    public static object OkBody<T>(T data) => new { success = true, data };
    public static object FailBody(string code, string message) => new { success = false, error = new { code, message } };
    public static IResult From(object body, int status) => Results.Json(body, statusCode: status);
}

// finding 3: POST create endpoints honor Idempotency-Key — stored response is replayed without re-running the handler
public static class Idem
{
    private static readonly System.Text.Json.JsonSerializerOptions Opts =
        new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static IResult Wrap(HttpContext ctx, Data.Db db, Func<(object body, int status)> run)
    {
        var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            var fresh = run();
            return Envelope.From(fresh.body, fresh.status);
        }
        var mapKey = $"{db.Tenant.Id}:{db.Entity.Id}:{key}";
        var stored = db.WithLock(() => db.Idempotency.TryGetValue(mapKey, out var hit) ? hit : ((int Status, string Body)?)null);
        if (stored is not null) return Results.Content(stored.Value.Body, "application/json", statusCode: stored.Value.Status);
        var (body, status) = run();
        var json = System.Text.Json.JsonSerializer.Serialize(body, Opts);
        db.WithLock(() => db.Idempotency[mapKey] = (status, json));
        return Results.Content(json, "application/json", statusCode: status);
    }
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

// finding 9: framework-level binding failures (malformed JSON) bypass the envelope — buffer POST responses
// under /api and rewrite any non-envelope 400 to VALIDATION_ERROR
public sealed class BindingEnvelopeMiddleware(RequestDelegate next)
{
    private const string Body = """{"success":false,"error":{"code":"VALIDATION_ERROR","message":"Request body could not be parsed."}}""";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api") || !HttpMethods.IsPost(ctx.Request.Method))
        {
            await next(ctx);
            return;
        }

        var original = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try { await next(ctx); }
        catch (Exception ex) when (ex.GetType().Name == "BadHttpRequestException")
        {
            ctx.Response.Body = original;
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.Headers.ContentLength = null;
            await ctx.Response.WriteAsync(Body, System.Text.Encoding.UTF8);
            return;
        }
        finally { ctx.Response.Body = original; }

        if (ctx.Response.StatusCode == StatusCodes.Status400BadRequest && !IsEnvelope(buffer.ToArray()))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.Headers.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers.ContentLength = null;
            await ctx.Response.WriteAsync(Body, System.Text.Encoding.UTF8);
            return;
        }
        if (buffer.Length > 0)
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(original);
        }
    }

    private static bool IsEnvelope(byte[] raw)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("success", out _);
        }
        catch { return false; }
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
            var m = db.Memberships.First(x => x.UserId == user.Id && x.TenantId == db.Tenant.Id && x.LegalEntityId == db.Entity.Id);
            var role = db.Roles.First(r => r.Id == m.RoleId);
            ctx.Items["user"] = new CurrentUser(user.Id, user.Email, user.DisplayName, role.Id, role.Permissions);
            ctx.Items["entity"] = db.Entity;
        }
        await next(ctx);
    }
}
