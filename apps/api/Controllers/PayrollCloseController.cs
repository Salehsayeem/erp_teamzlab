using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM8 — effective-dated payroll account mappings (053 AC2) + period close (054 maker-checker).
// paycodes:write (R-FIN) maintains mappings; payroll:close (R-FIN) closes/waives/reopens.
[ApiController]
[Route("api/v1/payroll-account-mappings")]
public sealed class PayrollAccountMappingsController(IPayrollCloseService close) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("paycodes:read")) return Envelope.Fail("FORBIDDEN", "paycodes:read permission required.", 403);
        return Envelope.Ok<object>(close.ListMappings(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateAccountMappingRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Element, "Element", 50) ?? Guards.Check(request.AccountCode, "AccountCode", 50)
            ?? (request.EffectiveFrom == default ? "EffectiveFrom is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("paycodes:write")) return (Envelope.FailBody("FORBIDDEN", "paycodes:write permission required."), 403);
        var (mapping, code, message) = close.CreateMapping(u, request);
        return code is null ? (Envelope.OkBody(mapping!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/pay-periods/{periodId}")]
public sealed class PayrollCloseController(IPayrollCloseService close) : ControllerBase
{
    [HttpGet("close-checklist")]
    public IResult Checklist(string periodId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = close.CloseChecklist(u, periodId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    // 054: blocked closes surface the named items INSIDE the error object (FailBodyItems)
    [HttpPost("closures")]
    public IResult Close(string periodId, Dtos.ClosePayPeriodRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:close")) return (Envelope.FailBody("FORBIDDEN", "payroll:close permission required."), 403);
        var (data, code, message, items) = close.Close(u, periodId, request);
        return code switch
        {
            null => (Envelope.OkBody(data!), 200),
            "CLOSE_BLOCKED" => (Envelope.FailBodyItems(code, message!, items!), 409),
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });

    [HttpPost("reopenings")]
    public IResult Reopen(string periodId, Dtos.ReopenPayPeriodRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Envelope.Fail("VALIDATION_ERROR", "A reopening requires a Reason.", 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:close")) return Envelope.Fail("FORBIDDEN", "payroll:close permission required.", 403);
        var (data, code, message) = close.Reopen(u, periodId, request);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
