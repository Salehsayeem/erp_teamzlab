using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P6 (063) — analytics:read guarded, read-only, tenant-scoped.
[ApiController]
[Route("api/v1/procurement-analytics")]
public sealed class ProcurementAnalyticsController(IProcurementAnalyticsService analytics) : ControllerBase
{
    [HttpGet("spend")]
    public IResult Spend([FromQuery] string? groupBy, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("analytics:read")) return Envelope.Fail("FORBIDDEN", "analytics:read permission required.", 403);
        return Envelope.Ok<object>(analytics.Spend(groupBy, from, to));
    }

    [HttpGet("demand")]
    public IResult Demand([FromQuery] string? groupBy)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("analytics:read")) return Envelope.Fail("FORBIDDEN", "analytics:read permission required.", 403);
        return Envelope.Ok<object>(analytics.Demand(groupBy));
    }

    [HttpGet("sourcing")]
    public IResult Sourcing()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("analytics:read")) return Envelope.Fail("FORBIDDEN", "analytics:read permission required.", 403);
        return Envelope.Ok<object>(analytics.Sourcing());
    }
}
