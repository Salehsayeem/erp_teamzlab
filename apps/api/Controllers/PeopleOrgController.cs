using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM1 — org hierarchy, work locations, positions, assignments (R-HR org:write makes; the
// exception decision is R-FIN's payroll:approve act — never the maker's).
[ApiController]
[Route("api/v1/org-units")]
public sealed class PeopleOrgController(IPeopleOrgService org) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? asOf)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        var parsed = DateOnly.TryParse(asOf, out var at);
        return Envelope.Ok<object>(org.ListOrgUnits(u, parsed ? at : null));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateOrgUnitRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? (request.EffectiveFrom == default ? "EffectiveFrom is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("org:write")) return (Envelope.FailBody("FORBIDDEN", "org:write permission required."), 403);
        var (unit, code, message) = org.CreateOrgUnit(u, request);
        return code is null ? (Envelope.OkBody(unit!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/versions")]
    public IResult CreateVersion(string id, Dtos.CreateOrgUnitVersionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Name, "Name")
            ?? (request.EffectiveFrom == default ? "EffectiveFrom is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("org:write")) return (Envelope.FailBody("FORBIDDEN", "org:write permission required."), 403);
        var (unit, code, message) = org.CreateOrgUnitVersion(u, id, request);
        return code is null ? (Envelope.OkBody(unit!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/work-locations")]
public sealed class WorkLocationsController(IPeopleOrgService org) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(org.ListWorkLocations(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateWorkLocationRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Timezone, "Timezone", 60);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("org:write")) return (Envelope.FailBody("FORBIDDEN", "org:write permission required."), 403);
        var (location, code, message) = org.CreateWorkLocation(u, request);
        return code is null ? (Envelope.OkBody(location!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/positions")]
public sealed class PositionsController(IPeopleOrgService org) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(org.ListPositions(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreatePositionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Title, "Name");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("org:write")) return (Envelope.FailBody("FORBIDDEN", "org:write permission required."), 403);
        var (position, code, message) = org.CreatePosition(u, request);
        return code is null ? (Envelope.OkBody(position!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // the headcount/FTE/credential gate — 409 without an exception reason, PendingException with one
    [HttpPost("{id}/assignments")]
    public IResult CreateAssignment(string id, Dtos.CreatePositionAssignmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId")
            ?? (request.Fte <= 0 ? "Fte must be greater than 0." : null)
            ?? (request.EffectiveFrom == default ? "EffectiveFrom is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("org:write")) return (Envelope.FailBody("FORBIDDEN", "org:write permission required."), 403);
        var (assignment, code, message) = org.CreateAssignment(u, id, request);
        return code is null ? (Envelope.OkBody(assignment!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("/api/v1/position-assignments")]
    public IResult ListAssignments([FromQuery] string? employmentId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(org.ListAssignments(u, employmentId));
    }
}

[ApiController]
[Route("api/v1/position-assignments")]
public sealed class PositionAssignmentsController(IPeopleOrgService org) : ControllerBase
{
    // R-FIN exception decision — activates or rejects a PendingException assignment
    [HttpPost("{id}/exception-decisions")]
    public IResult Decide(string id, Dtos.DecideAssignmentExceptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Envelope.Fail("VALIDATION_ERROR", "A decision requires a Reason.", 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:approve")) return Envelope.Fail("FORBIDDEN", "payroll:approve permission required.", 403);
        var (assignment, code, message) = org.DecideAssignmentException(u, id, request);
        return code is null ? Envelope.Ok<object>(assignment!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
