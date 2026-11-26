using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// MF2 — schedule runs / scenarios / publications (ERP-MFG-027..030) and MF3 — dispatch list +
// capacity load (031/032). All writes sit behind mfg:schedule; reads behind mfg:read.
[ApiController]
[Route("api/v1/schedule-runs")]
public sealed class SchedulingController(ISchedulingService scheduling) : ControllerBase
{
    [HttpGet]
    public IResult ListRuns([FromQuery] string? orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(scheduling.RunsForOrder(orderId));
    }

    [HttpPost]
    public IResult Create(CreateScheduleRunRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.OrderId, "OrderId")
            ?? Guards.Check(request.CalendarVersion, "CalendarVersion")
            ?? Guards.Check(request.AlgorithmVersion, "AlgorithmVersion");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:schedule")) return (Envelope.FailBody("FORBIDDEN", "mfg:schedule permission required."), 403);
        var (data, code, message) = scheduling.CreateRun(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpGet("{id}/placements")]
    public IResult Placements(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(scheduling.Placements(id));
    }

    [HttpGet("{id}/conflicts")]
    public IResult Conflicts(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(scheduling.Conflicts(id));
    }

    [HttpPost("{id}/publications")]
    public IResult Publish(string id, [FromBody] SchedulePublicationRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:schedule")) return (Envelope.FailBody("FORBIDDEN", "mfg:schedule permission required."), 403);
        var (data, code, message) = scheduling.Publish(u, id, request ?? new SchedulePublicationRequest(null));
        return code switch
        {
            "SCHEDULE_PUBLISH_BLOCKED" => (Envelope.FailBodyItems(code, message!,
                Array.Empty<object>()), 409),
            null => (Envelope.OkBody(data!), 200),
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });
}

[ApiController]
[Route("api/v1/schedule-scenarios")]
public sealed class ScheduleScenariosController(ISchedulingService scheduling) : ControllerBase
{
    [HttpPost]
    public IResult Create(CreateScenarioRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Name, "Name") ?? Guards.Check(request.BaseRunId, "BaseRunId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:schedule")) return (Envelope.FailBody("FORBIDDEN", "mfg:schedule permission required."), 403);
        var (data, code, message) = scheduling.CreateScenario(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/placement-moves")]
    public IResult MovePlacement(string id, PlacementMoveRequest request)
    {
        var err = Guards.Check(request.PlacementId, "PlacementId");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:schedule")) return Envelope.Fail("FORBIDDEN", "mfg:schedule permission required.", 403);
        var (data, code, message) = scheduling.MovePlacement(u, id, request);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpGet("{id}/overrides")]
    public IResult Overrides(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(scheduling.Overrides(id));
    }
}

[ApiController]
[Route("api/v1/dispatch-list")]
public sealed class DispatchController(ISchedulingService scheduling) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? workCenter)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(scheduling.DispatchList(CurrentUser.From(HttpContext), workCenter));
    }
}

[ApiController]
[Route("api/v1/capacity-load")]
public sealed class CapacityLoadController(ISchedulingService scheduling) : ControllerBase
{
    [HttpGet]
    public IResult Load([FromQuery] string? bucket)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(scheduling.CapacityLoad(bucket ?? "day"));
    }
}
