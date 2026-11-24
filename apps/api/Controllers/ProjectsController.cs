using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Projects + minimal WBS + the gated, maker-checked closure (ERP-ASSETS-001/002/003/019)
[ApiController]
[Route("api/v1/projects")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("projects:read")) return Envelope.Fail("FORBIDDEN", "projects:read permission required.", 403);
        var (ok, value) = Query.Enum<ProjectStatus>(status);
        return ok ? Envelope.Ok<object>(projects.List(value))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown project status '{status}'.", 400);
    }

    [HttpPost]
    public IResult Create(CreateProjectRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.Name, "Name") ?? Guards.Check(request.ManagerId, "ManagerId")
            ?? (request.Budget < 0 ? "Budget must be 0 or greater." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("projects:write")) return (Envelope.FailBody("FORBIDDEN", "projects:write permission required."), 403);
        var (data, code, message) = projects.Create(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/tasks")]
    public IResult AddTask(string id, CreateTaskRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.Name, "Name") ?? Guards.CheckPositive(request.HoursEstimate, "HoursEstimate");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("projects:write")) return (Envelope.FailBody("FORBIDDEN", "projects:write permission required."), 403);
        var (data, code, message) = projects.AddTask(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/tasks/{taskId}/completion")]
    public IResult CompleteTask(string id, string taskId, CompleteTaskRequest request)
    {
        var err = request.HoursActual < 0 ? "HoursActual must be 0 or greater." : null;
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("projects:write")) return Envelope.Fail("FORBIDDEN", "projects:write permission required.", 403);
        var (data, code, message) = projects.CompleteTask(u, id, taskId, request.HoursActual);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/closure")]
    public IResult Close(string id)
    {
        // maker-checker + gate live in the service (019): creator-blocked, then projects:approve,
        // then the open-task gate listing offending ids
        var (data, code, message) = projects.Close(CurrentUser.From(HttpContext), id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
