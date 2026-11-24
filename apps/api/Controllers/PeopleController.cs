using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Worker register + lifecycle + compensation + pay-group membership (R-PEO3 one-step register).
// DTOs are validated Guards-style BEFORE the service runs; creates are Idem.Wrap'd per the brief.
[ApiController]
[Route("api/v1/workers")]
public sealed class WorkersController(IWorkerService workers) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status, [FromQuery] string? search)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        var (okStatus, statusValue) = Query.Enum<EmploymentStatus>(status);
        return okStatus
            ? Envelope.Ok<object>(workers.List(u, statusValue, search))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown worker status '{status}'.", 400);
    }

    [HttpPost]
    public IResult Create(Dtos.CreateWorkerRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.FullName, "FullName")
            ?? Guards.Check(request.CategoryCode, "CategoryCode", 50)
            ?? (request.WeeklyPlannedHours <= 0 ? "WeeklyPlannedHours must be greater than 0." : null)
            ?? (request.Email is { Length: > 0 } && !request.Email.Contains('@', StringComparison.Ordinal)
                ? "Email must be valid when provided." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:write")) return (Envelope.FailBody("FORBIDDEN", "workers:write permission required."), 403);
        var (worker, code, message) = workers.Create(u, request);
        return code is null ? (Envelope.OkBody(worker!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // lifecycle transitions are single acts (no Idem — a repeated transition is a conflict)
    [HttpPost("{id}/transitions")]
    public IResult Transition(string id, Dtos.TransitionEmploymentRequest request)
    {
        var err = (request.Reason is { Length: > 200 } ? "Reason must be at most 200 characters." : null)
            ?? (request.Status == EmploymentStatus.Terminated && string.IsNullOrWhiteSpace(request.Reason)
                ? "A termination requires a Reason (007 AC2)." : null);
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:write")) return Envelope.Fail("FORBIDDEN", "workers:write permission required.", 403);
        var (worker, code, message) = workers.Transition(u, id, request);
        return code is null ? Envelope.Ok<object>(worker!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/compensations")]
    public IResult AddCompensation(string id, Dtos.AddCompensationRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Reason, "Reason")
            ?? Guards.CheckPositive(request.Amount, "Amount")
            ?? Guards.Check(request.Currency, "Currency")
            ?? Guards.Check(request.PayBasis, "PayBasis", 20);
        if (err is null && request.PayBasis is not ("Monthly" or "Hourly"))
            err = "PayBasis must be Monthly or Hourly.";
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:write")) return (Envelope.FailBody("FORBIDDEN", "workers:write permission required."), 403);
        var (evt, code, message) = workers.AddCompensation(u, id, request);
        return code is null ? (Envelope.OkBody(evt!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 024 AC1: as-of query returns the applicable terms
    [HttpGet("{id}/compensations")]
    public IResult Compensations(string id, [FromQuery] DateOnly? asOf)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(workers.Compensations(u, id, asOf));
    }

    [HttpPost("{id}/pay-group-memberships")]
    public IResult AddMembership(string id, Dtos.AddPayGroupMembershipRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.PayGroupId, "PayGroupId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:calculate")) return (Envelope.FailBody("FORBIDDEN", "payroll:calculate permission required."), 403);
        var (membership, code, message) = workers.AddMembership(u, id, request);
        return code is null ? (Envelope.OkBody(membership!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/pay-history")]
    public IResult PayHistory(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = workers.PayHistory(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

// pay groups are payroll configuration (035 trim: no calendar generation — R-PEO5)
[ApiController]
[Route("api/v1/pay-groups")]
public sealed class PayGroupsController(IWorkerService workers) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok<object>(workers.ListPayGroups(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreatePayGroupRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Frequency, "Frequency", 20);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:calculate")) return (Envelope.FailBody("FORBIDDEN", "payroll:calculate permission required."), 403);
        var (group, code, message) = workers.CreatePayGroup(u, request);
        return code is null ? (Envelope.OkBody(group!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
