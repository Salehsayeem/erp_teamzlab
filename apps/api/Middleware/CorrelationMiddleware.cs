namespace Erp.Api.Middleware;

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
