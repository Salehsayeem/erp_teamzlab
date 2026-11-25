using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// MF1 — routings + work centers (ERP-MFG-024/025/026). Validation first, permission second,
// service third — the landed controller discipline. Gate rejections are 409 state conflicts,
// missing rows 404, shape problems 400.
[ApiController]
[Route("api/v1/routings")]
public sealed class RoutingsController(IRoutingService routings) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? outputSku, [FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        RoutingStatus? filter = ParseStatus(status);
        if (status is not null && filter is null)
            return Envelope.Fail("VALIDATION_ERROR", $"status '{status}' is not a valid routing status (Draft, Approved, Retired).", 400);
        return Envelope.Ok<object>(routings.List(outputSku, filter));
    }

    [HttpPost]
    public IResult Create(CreateRoutingRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = routings.Create(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(new { id = data!.Id, outputSku = data.OutputSku, revision = data.Revision, status = data.Status.ToString() }), 201);
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, [FromBody] RoutingDecisionRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("boms:approve")) return (Envelope.FailBody("FORBIDDEN", "boms:approve permission required."), 403);
        var (data, code, message) = routings.Approve(u, id, request?.Reason);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });

    [HttpPost("{id}/operations")]
    public IResult AddOperation(string id, AddRoutingOperationRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.OperationCode, "OperationCode")
            ?? Guards.Check(request.WorkCenterId, "WorkCenterId")
            ?? Guards.Check(request.RequiredCapabilities, "RequiredCapabilities")
            ?? Guards.Check(request.TimeUnit, "TimeUnit");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = routings.AddOperation(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/operations/{opId}/moves")]
    public IResult MoveOperation(string id, string opId, MoveRoutingOperationRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return Envelope.Fail("FORBIDDEN", "mfg:write permission required.", 403);
        var (data, code, message) = routings.MoveOperation(u, id, opId, request);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpGet("{id}/operations")]
    public IResult Operations(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(routings.Operations(id));
    }

    private static RoutingStatus? ParseStatus(string? status) =>
        status is null ? null : Enum.TryParse<RoutingStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
}

[ApiController]
[Route("api/v1/work-centers")]
public sealed class WorkCentersController(IRoutingService routings) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(routings.ListWorkCenters());
    }

    [HttpPost]
    public IResult Create(CreateWorkCenterRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = routings.CreateWorkCenter(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpGet("{id}/calendar")]
    public IResult Calendar(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(routings.Calendar(id));
    }

    [HttpPost("{id}/calendar")]
    public IResult AddWindow(string id, AddCalendarWindowRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = routings.AddCalendarWindow(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}
