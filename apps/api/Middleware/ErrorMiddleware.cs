namespace Erp.Api.Middleware;

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
