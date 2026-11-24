using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/boms")]
public sealed class BomsController(IBomService boms) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? outputSku, [FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("boms:read")) return Envelope.Fail("FORBIDDEN", "boms:read permission required.", 403);
        BomStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<BomStatus>(status, ignoreCase: true, out var parsed))
                return Envelope.Fail("VALIDATION_ERROR", $"status '{status}' is not a valid BOM status (Draft, Approved, Retired).", 400);
            filter = parsed;
        }
        return Envelope.Ok<object>(boms.List(outputSku, filter));
    }

    [HttpPost]
    public IResult Create(CreateBomRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.OutputSku, "OutputSku")
            ?? (request.Revision < 1 ? "Revision must be at least 1." : null)
            ?? Guards.CheckPositive(request.OutputQtyPer, "OutputQtyPer");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("boms:write")) return (Envelope.FailBody("FORBIDDEN", "boms:write permission required."), 403);
        var (data, code, message) = boms.Create(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/lines")]
    public IResult AddLine(string id, AddBomLineRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.ComponentSku, "ComponentSku")
            ?? Guards.CheckPositive(request.QtyPer, "QtyPer")
            ?? Guards.Check(request.Unit, "Unit")
            ?? (request.UnitCost < 0 ? "UnitCost must be >= 0." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("boms:write")) return (Envelope.FailBody("FORBIDDEN", "boms:write permission required."), 403);
        var (data, code, message) = boms.AddLine(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, [FromBody] BomDecisionRequest? request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("boms:approve")) return Envelope.Fail("FORBIDDEN", "boms:approve permission required.", 403);
        var (data, code, message) = boms.Approve(u, id, request?.Reason);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpPost("{id}/retirements")]
    public IResult Retire(string id, [FromBody] BomDecisionRequest? request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("boms:write")) return Envelope.Fail("FORBIDDEN", "boms:write permission required.", 403);
        var (data, code, message) = boms.Retire(u, id, request?.Reason);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }
}
