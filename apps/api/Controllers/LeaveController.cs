using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM5 — leave & attendance exceptions. Permissions per brief §3: leave:write/leave:approve sit
// on R-HR (approver ≠ requester enforced in the service), leave:adjust (manual ledger entries)
// is R-FIN's elevated act, attendance dispositions ride time:approve.
[ApiController]
[Route("api/v1/leave-types")]
public sealed class LeaveTypesController(ILeaveService leave) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(leave.ListLeaveTypes(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateLeaveTypeRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Units, "Units", 20)
            ?? (request.Units is not ("Days" or "Hours") ? "Units must be Days or Hours." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leave:write")) return (Envelope.FailBody("FORBIDDEN", "leave:write permission required."), 403);
        var (type, code, message) = leave.CreateLeaveType(u, request);
        return code is null ? (Envelope.OkBody(type!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 030: idempotent per period+source — a replay creates nothing and reports created=0
    [HttpPost("{id}/accrual-runs")]
    public IResult RunAccrual(string id, Dtos.RunAccrualRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Period, "Period", 7)
            ?? (request.AsOf == default ? "AsOf is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leave:write")) return (Envelope.FailBody("FORBIDDEN", "leave:write permission required."), 403);
        var (data, code, message) = leave.RunAccrual(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/leave-requests")]
public sealed class LeaveRequestsController(ILeaveService leave) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId, [FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        var (okStatus, statusValue) = Query.Enum<LeaveRequestStatus>(status);
        return okStatus
            ? Envelope.Ok<object>(leave.ListLeaveRequests(u, employmentId, statusValue))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown leave request status '{status}'.", 400);
    }

    [HttpPost]
    public IResult Create(Dtos.CreateLeaveRequestRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId") ?? Guards.Check(request.LeaveTypeId, "LeaveTypeId")
            ?? Guards.Check(request.Reason, "Reason")
            ?? (request.Days <= 0 ? "Days must be greater than 0." : null)
            ?? (request.FromDate == default ? "FromDate is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leave:write")) return (Envelope.FailBody("FORBIDDEN", "leave:write permission required."), 403);
        var (leaveRequest, code, message) = leave.CreateLeaveRequest(u, request);
        return code is null ? (Envelope.OkBody(leaveRequest!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/decisions")]
    public IResult Decide(string id, Dtos.DecideLeaveRequestRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leave:approve")) return Envelope.Fail("FORBIDDEN", "leave:approve permission required.", 403);
        var (leaveRequest, code, message) = leave.DecideLeaveRequest(u, id, request);
        return code is null ? Envelope.Ok<object>(leaveRequest!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    // 031 AC2: withdrawal only via this recorded workflow — never a field edit
    [HttpPost("{id}/withdrawals")]
    public IResult Withdraw(string id, Dtos.DecideOnboardingTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Envelope.Fail("VALIDATION_ERROR", "A withdrawal requires a Reason.", 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leave:write")) return Envelope.Fail("FORBIDDEN", "leave:write permission required.", 403);
        var (leaveRequest, code, message) = leave.Withdraw(u, id, request.Reason);
        return code is null ? Envelope.Ok<object>(leaveRequest!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/leave-adjustments")]
public sealed class LeaveAdjustmentsController(ILeaveService leave) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok<object>(leave.ListLeaveAdjustments(u, employmentId));
    }

    // 032: elevated rights — leave:adjust sits on R-FIN only; independence enforced at decision
    [HttpPost]
    public IResult Create(Dtos.CreateLeaveAdjustmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId") ?? Guards.Check(request.LeaveTypeId, "LeaveTypeId")
            ?? Guards.Check(request.Reason, "Reason");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leave:adjust")) return (Envelope.FailBody("FORBIDDEN", "leave:adjust permission required (manual entries are elevated, 032)."), 403);
        var (adjustment, code, message) = leave.CreateLeaveAdjustment(u, request);
        return code is null ? (Envelope.OkBody(adjustment!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/approvals")]
    public IResult Decide(string id, Dtos.DecideLeaveAdjustmentRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leave:adjust")) return Envelope.Fail("FORBIDDEN", "leave:adjust permission required.", 403);
        var (adjustment, code, message) = leave.DecideLeaveAdjustment(u, id, request);
        return code is null ? Envelope.Ok<object>(adjustment!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/workers/{workerId}/leave-balances")]
public sealed class LeaveBalancesController(ILeaveService leave) : ControllerBase
{
    // 030 AC2: the as-of balance is opening + dated entries — reproducible
    [HttpGet]
    public IResult Balance(string workerId, [FromQuery] string? leaveTypeId, [FromQuery] string? asOf)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        var parsed = DateOnly.TryParse(asOf, out var at);
        var (data, code, message) = leave.Balance(u, workerId, leaveTypeId, parsed ? at : null);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/attendance-exceptions")]
public sealed class AttendanceExceptionsController(ILeaveService leave) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:read")) return Envelope.Fail("FORBIDDEN", "time:read permission required.", 403);
        return Envelope.Ok<object>(leave.ListAttendanceExceptions(u, employmentId));
    }

    [HttpPost("generations")]
    public IResult Generate(Dtos.GenerateAttendanceExceptionsRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = request.From == default || request.To == default ? "From and To are required."
            : request.To < request.From ? "To must not precede From." : null;
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:approve")) return (Envelope.FailBody("FORBIDDEN", "time:approve permission required."), 403);
        var (data, code, message) = leave.GenerateAttendanceExceptions(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 033 AC2: the disposition never touches the immutable source fact
    [HttpPost("{id}/dispositions")]
    public IResult Dispose(string id, Dtos.DisposeAttendanceExceptionRequest request)
    {
        var err = Guards.Check(request.Disposition, "Disposition", 50) ?? Guards.Check(request.Reason, "Reason");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("time:approve")) return Envelope.Fail("FORBIDDEN", "time:approve permission required.", 403);
        var (attendanceException, code, message) = leave.DisposeAttendanceException(u, id, request);
        return code is null ? Envelope.Ok<object>(attendanceException!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
