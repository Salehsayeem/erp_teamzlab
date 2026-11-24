using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Time capture + corrections (ERP-PEOPLE-026/027). Creates are Idem.Wrap'd — the same key replays
// the ORIGINAL entry (026 AC2) and the key doubles as the row's IdemKey. Decisions are single acts
// with approver ≠ requester enforced in the service.
[ApiController]
[Route("api/v1/time-entries")]
public sealed class TimeEntriesController(ITimeService time) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId, [FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:read")) return Envelope.Fail("FORBIDDEN", "time:read permission required.", 403);
        var (okStatus, statusValue) = Query.Enum<TimeEntryStatus>(status);
        return okStatus
            ? Envelope.Ok<object>(time.List(u, employmentId, statusValue))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown time entry status '{status}'.", 400);
    }

    [HttpPost]
    public IResult Create(Dtos.CreateTimeEntryRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId")
            ?? (request.Hours < 0 || request.OvertimeHours < 0 ? "Hours and OvertimeHours must be 0 or greater." : null)
            ?? (request.Hours + request.OvertimeHours > 24 ? "Hours + OvertimeHours cannot exceed 24 in one day." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:write")) return (Envelope.FailBody("FORBIDDEN", "time:write permission required."), 403);
        var idemKey = HttpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
        var (entry, code, message) = time.Create(u, request, string.IsNullOrWhiteSpace(idemKey) ? null : idemKey);
        return code is null ? (Envelope.OkBody(entry!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/decisions")]
    public IResult Decide(string id, Dtos.DecideTimeEntryRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:approve")) return Envelope.Fail("FORBIDDEN", "time:approve permission required.", 403);
        var (entry, code, message) = time.Decide(u, id, request);
        return code is null ? Envelope.Ok<object>(entry!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/time-corrections")]
public sealed class TimeCorrectionsController(ITimeService time) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId, [FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:read")) return Envelope.Fail("FORBIDDEN", "time:read permission required.", 403);
        var (okStatus, statusValue) = Query.Enum<TimeCorrectionStatus>(status);
        return okStatus
            ? Envelope.Ok<object>(time.ListCorrections(u, employmentId, statusValue))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown correction status '{status}'.", 400);
    }

    [HttpPost]
    public IResult Create(Dtos.CreateTimeCorrectionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.TimeEntryId, "TimeEntryId")
            ?? Guards.Check(request.Reason, "Reason")
            ?? (request.ProposedHours < 0 || request.ProposedOvertimeHours < 0
                ? "Proposed hours must be 0 or greater." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:write")) return (Envelope.FailBody("FORBIDDEN", "time:write permission required."), 403);
        var (correction, code, message) = time.CreateCorrection(u, request);
        return code is null ? (Envelope.OkBody(correction!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/decisions")]
    public IResult Decide(string id, Dtos.DecideTimeCorrectionRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:approve")) return Envelope.Fail("FORBIDDEN", "time:approve permission required.", 403);
        var (correction, code, message) = time.DecideCorrection(u, id, request);
        return code is null ? Envelope.Ok<object>(correction!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
