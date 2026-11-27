using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// FINANCE-deep P1 (brief §5 partition): fiscal calendars (004) and close checklists (062/063).
// Reads gate periods:read (the maker R-FIN2 can see calendars/checklists), writes gate
// periods:write (calendars) / close:write (checklist mutations); the task-waiver variant gates
// close:approve (063 high-risk — distinct from periods:write per the §3 permission table).
// House envelope + Idem on POSTs.

[ApiController]
[Route("api/v1/fiscal-calendars")]
public sealed class FiscalCalendarsController(IFiscalCalendarService calendars) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("periods:read")
            ? Envelope.Ok<object>(calendars.List())
            : Envelope.Fail("FORBIDDEN", "periods:read permission required.", 403);
    }

    [HttpPost]
    public IResult Create(CreateFiscalCalendarRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("periods:write")) return (Envelope.FailBody("FORBIDDEN", "periods:write permission required."), 403);
        var (calendar, code, error) = calendars.Create(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(calendar!), 201);
    });

    // 004 AC2 — activation validates the coverage chain (CALENDAR_OVERLAP / CALENDAR_GAP) and
    // materializes the calendar's periods as FiscalPeriod rows
    [HttpPost("{id}/activations")]
    public IResult Activate(string id) => Idem.Wrap(HttpContext, payload: null, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("periods:write")) return (Envelope.FailBody("FORBIDDEN", "periods:write permission required."), 403);
        var (calendar, code, error) = calendars.Activate(u, id);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(calendar!), 200);
    });
}

[ApiController]
[Route("api/v1/close-checklists")]
public sealed class CloseChecklistsController(ICloseService close) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? periodId)
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("periods:read")
            ? Envelope.Ok<object>(close.List(periodId))
            : Envelope.Fail("FORBIDDEN", "periods:read permission required.", 403);
    }

    [HttpPost]
    public IResult Create(CreateCloseChecklistRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("close:write")) return (Envelope.FailBody("FORBIDDEN", "close:write permission required."), 403);
        var (tasks, code, error) = close.CreateChecklist(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(tasks!), 201);
    });

    [HttpPost("{periodId}/tasks")]
    public IResult AddTask(string periodId, AddCloseTaskRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("close:write")) return (Envelope.FailBody("FORBIDDEN", "close:write permission required."), 403);
        var (task, code, error) = close.AddTask(u, periodId, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(task!), 201);
    });

    // 062 AC1 — the waiver variant of a completion needs the authorize permission; evidence
    // completions ride close:write
    [HttpPost("{periodId}/tasks/{taskId}/completions")]
    public IResult Complete(string periodId, string taskId, CompleteCloseTaskRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("close:write")) return (Envelope.FailBody("FORBIDDEN", "close:write permission required."), 403);
        if (!string.IsNullOrWhiteSpace(request.WaiverReason) && !u.Has("close:approve"))
            return (Envelope.FailBody("FORBIDDEN", "close:approve permission required to waive a close task."), 403);
        var (task, code, error) = close.Complete(u, periodId, taskId, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(task!), 200);
    });

    // 062 AC3 — N/A with reason; the row is retained, never silently removed
    [HttpPost("{periodId}/tasks/{taskId}/na")]
    public IResult MarkNotApplicable(string periodId, string taskId, NaCloseTaskRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("close:write")) return (Envelope.FailBody("FORBIDDEN", "close:write permission required."), 403);
        var (task, code, error) = close.MarkNotApplicable(u, periodId, taskId, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(task!), 200);
    });
}
