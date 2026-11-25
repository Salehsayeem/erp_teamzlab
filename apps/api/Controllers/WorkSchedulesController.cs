using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM4 — work schedules & calendars (025): versioned schedules with weekly planned hours;
// expansion produces unambiguous UTC instants from the named timezone.
[ApiController]
[Route("api/v1/work-schedules")]
public sealed class WorkSchedulesController(IWorkScheduleService schedules) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(schedules.ListSchedules(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateWorkScheduleRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Timezone, "Timezone", 60) ?? Guards.Check(request.StartTimeLocal, "StartTimeLocal", 5);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("schedules:write")) return (Envelope.FailBody("FORBIDDEN", "schedules:write permission required."), 403);
        var (schedule, code, message) = schedules.CreateSchedule(u, request);
        return code is null ? (Envelope.OkBody(schedule!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/versions")]
    public IResult CreateVersion(string id, Dtos.CreateScheduleVersionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = request.WeeklyPlannedHours <= 0 ? "WeeklyPlannedHours must be greater than 0."
            : request.EffectiveFrom == default ? "EffectiveFrom is required." : null;
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("schedules:write")) return (Envelope.FailBody("FORBIDDEN", "schedules:write permission required."), 403);
        var (version, code, message) = schedules.CreateScheduleVersion(u, id, request);
        return code is null ? (Envelope.OkBody(version!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/expansions")]
    public IResult Expand(string id, [FromQuery] string from, [FromQuery] string to)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
            return Envelope.Fail("VALIDATION_ERROR", "from and to must be yyyy-MM-dd dates.", 400);
        var (data, code, message) = schedules.Expand(u, id, fromDate, toDate);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/holiday-calendars")]
public sealed class HolidayCalendarsController(IWorkScheduleService schedules) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(schedules.ListHolidayCalendars(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateHolidayCalendarRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("schedules:write")) return (Envelope.FailBody("FORBIDDEN", "schedules:write permission required."), 403);
        var (calendar, code, message) = schedules.CreateHolidayCalendar(u, request);
        return code is null ? (Envelope.OkBody(calendar!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
