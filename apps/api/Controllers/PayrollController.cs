using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Pay runs + earning/deduction catalog (ERP-PEOPLE-034/037/038/039/046/047/048/049/051/053/055).
// Maker-checker and posting gates live in the service (R-PEO9: R-FIN holds payroll:approve);
// creates are Idem.Wrap'd so run creation, calculation, approval, posting and reversal replays
// never duplicate documents or journals.
[ApiController]
[Route("api/v1/pay-codes")]
public sealed class PayCodesController(IPayrollService payroll) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? kind)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (okKind, kindValue) = Query.Enum<PayCodeKind>(kind);
        return okKind
            ? Envelope.Ok<object>(payroll.ListPayCodes(u, kindValue))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown pay code kind '{kind}'.", 400);
    }

    // 037 AC1: mapping is required before activation — the missing mapping is a 400 here
    [HttpPost]
    public IResult Create(Dtos.CreatePayCodeRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50)
            ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.CalcBasis, "CalcBasis", 20)
            ?? (string.IsNullOrWhiteSpace(request.DebitAccount) ? "DebitAccount is required (037 AC1)." : null)
            ?? (string.IsNullOrWhiteSpace(request.CreditAccount) ? "CreditAccount is required (037 AC1)." : null)
            ?? (request.CalcBasis is not ("Hours" or "Amount" or "Percent") ? "CalcBasis must be Hours, Amount or Percent." : null)
            ?? (request.Kind == PayCodeKind.Deduction && request.CalcBasis == "Hours"
                ? "Deduction codes cannot use the Hours basis." : null)
            ?? (request.CalcBasis == "Percent" && request.RatePct is null ? "Percent codes need a RatePct." : null)
            ?? (request.RatePct is { } rate && (rate < 0 || rate > 100) && request.CalcBasis == "Percent"
                ? "Percent RatePct must be between 0 and 100." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("paycodes:write")) return (Envelope.FailBody("FORBIDDEN", "paycodes:write permission required."), 403);
        var (code, errorCode, message) = payroll.CreatePayCode(u, request);
        return errorCode is null ? (Envelope.OkBody(code!), 201) : (Envelope.FailBody(errorCode, message!), ErrorStatus.StatusFor(errorCode));
    });
}

[ApiController]
[Route("api/v1/pay-runs")]
public sealed class PayRunsController(IPayrollService payroll) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status, [FromQuery] string? periodId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (okStatus, statusValue) = Query.Enum<PayRunStatus>(status);
        return okStatus
            ? Envelope.Ok<object>(payroll.ListRuns(u, statusValue, periodId))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown pay run status '{status}'.", 400);
    }

    // 049 AC1: the run key (tenant, entity, group, period) — repeat requests return the SAME run
    [HttpPost]
    public IResult Create(Dtos.CreatePayRunRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.PayGroupId, "PayGroupId") ?? Guards.Check(request.PeriodId, "PeriodId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:calculate")) return (Envelope.FailBody("FORBIDDEN", "payroll:calculate permission required."), 403);
        var (run, code, message) = payroll.CreateRun(u, request);
        return code is null ? (Envelope.OkBody(run!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/calculations")]
    public IResult Calculate(string id) => Idem.Wrap(HttpContext, new Dtos.CalculatePayRunRequest(), () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:calculate")) return (Envelope.FailBody("FORBIDDEN", "payroll:calculate permission required."), 403);
        var (run, code, message) = payroll.Calculate(u, id);
        return code is null ? (Envelope.OkBody(run!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/validations")]
    public IResult Validations(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = payroll.Validations(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpGet("{id}/preview")]
    public IResult Preview(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = payroll.Preview(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, Dtos.ApprovePayRunRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (run, code, message) = payroll.Approve(u, id, request.Reason);
        return code is null ? (Envelope.OkBody(run!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/postings")]
    public IResult Post(string id) => Idem.Wrap(HttpContext, new Dtos.PostPayRunRequest(), () =>
    {
        var (data, code, message) = payroll.Post(CurrentUser.From(HttpContext), id);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/payslips/{workerId}")]
    public IResult Payslip(string id, string workerId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = payroll.Payslip(u, id, workerId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/reversals")]
    public IResult Reverse(string id, Dtos.ReversePayRunRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        // 055 AC2: reason + funds-released flag are both required
        var err = Guards.Check(request.Reason, "Reason")
            ?? (request.FundsReleased is null ? "FundsReleased is required for a reversal (055 AC2)." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var (data, code, message) = payroll.Reverse(CurrentUser.From(HttpContext), id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
