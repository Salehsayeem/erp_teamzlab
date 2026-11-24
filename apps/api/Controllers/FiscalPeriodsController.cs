using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/fiscal-periods")]
public sealed class FiscalPeriodsController(IFiscalPeriodService periods) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("periods:read")) return Envelope.Fail("FORBIDDEN", "periods:read permission required.", 403);
        PeriodStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<PeriodStatus>(status, ignoreCase: true, out var parsed))
                return Envelope.Fail("VALIDATION_ERROR", $"status '{status}' is not a valid period status (Open, SoftClosed, Closed).", 400);
            filter = parsed;
        }
        return Envelope.Ok<object>(periods.List(filter, from, to));
    }

    [HttpPost]
    public IResult Create(CreatePeriodRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Name, "Name", 50)
            ?? (request.StartDate >= request.EndDate ? "StartDate must be before EndDate." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("periods:write")) return (Envelope.FailBody("FORBIDDEN", "periods:write permission required."), 403);
        var (period, code, error) = periods.Create(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(period!), 201);
    });

    [HttpPost("{id}/close")]
    public IResult Close(string id, [FromBody] ClosePeriodRequest? request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("periods:write")) return Envelope.Fail("FORBIDDEN", "periods:write permission required.", 403);
        var (period, code, error) = periods.Close(u, id, request?.Reason);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(period!);
    }

    [HttpPost("{id}/reopen")]
    public IResult Reopen(string id, [FromBody] ReopenPeriodRequest? request)
    {
        // 005 AC2: a reopen without a reason never reaches the service
        var reason = request?.Reason;
        if (string.IsNullOrWhiteSpace(reason))
            return Envelope.Fail("VALIDATION_ERROR", "A reopen reason is required.", 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("periods:write")) return Envelope.Fail("FORBIDDEN", "periods:write permission required.", 403);
        var (period, code, error) = periods.Reopen(u, id, reason);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(period!);
    }
}
