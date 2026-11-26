using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Sales analytics (ERP-SALES-065) — read-only, role-filtered per domain.
[ApiController]
[Route("api/v1")]
public sealed class SalesAnalyticsController(ISalesAnalyticsService analytics) : ControllerBase
{
    [HttpGet("sales-analytics")]
    public IResult Report([FromQuery] string domain, [FromQuery] string? groupBy)
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = analytics.Report(u, domain, groupBy);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
