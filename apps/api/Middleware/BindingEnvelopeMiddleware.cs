namespace Erp.Api.Middleware;

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
