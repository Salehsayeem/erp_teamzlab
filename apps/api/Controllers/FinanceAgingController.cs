using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// R9a: receivables aging ONLY — no payables endpoint exists (FINANCE-031 is receivables-only;
// AP aging arrives with FINANCE-023 if the controller mandates it later).
[ApiController]
[Route("api/v1/finance")]
public sealed class FinanceAgingController(IFinanceAgingService aging) : ControllerBase
{
    [HttpGet("receivables/aging")]
    public IResult Receivables([FromQuery] DateOnly? asOf, [FromQuery] string? basis)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("ar:read")) return Envelope.Fail("FORBIDDEN", "ar:read permission required.", 403);
        var effectiveBasis = basis ?? "due";
        if (effectiveBasis is not ("due" or "invoice"))
            return Envelope.Fail("VALIDATION_ERROR", "basis must be 'due' or 'invoice'.", 400);
        var (data, code, error) = aging.Receivables(asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), effectiveBasis);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(data!);
    }
}
