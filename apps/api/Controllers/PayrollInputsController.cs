using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM6 — gross-to-net inputs. payinputs:write (R-HR) makes; garnishment RELEASE is a funds
// authorization and rides payroll:release (R-FIN) — never the maker's permission.
[ApiController]
[Route("api/v1/tax-elections")]
public sealed class TaxElectionsController(IPayrollInputsService inputs) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok<object>(inputs.ListTaxElections(u, employmentId));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateTaxElectionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId") ?? Guards.Check(request.Kind, "Kind", 50);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (election, code, message) = inputs.CreateTaxElection(u, request);
        return code is null ? (Envelope.OkBody(election!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // verification is explicit — a Draft election never satisfies the mandatory-election gate
    [HttpPost("{id}/verifications")]
    public IResult Verify(string id, Dtos.VerifyElectionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (election, code, message) = inputs.VerifyElection(u, id);
        return code is null ? (Envelope.OkBody(election!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/payroll-config")]
public sealed class PayrollConfigController(IPayrollInputsService inputs) : ControllerBase
{
    [HttpGet]
    public IResult Read()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok(inputs.ReadConfig());
    }

    [HttpPost("updates")]
    public IResult Update(Dtos.UpdatePayrollConfigRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (data, code, message) = inputs.UpdatePayrollConfig(u, request);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/contribution-rules")]
public sealed class ContributionRulesController(IPayrollInputsService inputs) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok<object>(inputs.ListContributionRules(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateContributionRuleRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Party, "Party", 20) ?? Guards.Check(request.PayCodeId, "PayCodeId", 50)
            ?? (request.Party is not ("Employee" or "Employer") ? "Party must be Employee or Employer." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (rule, code, message) = inputs.CreateContributionRule(u, request);
        return code is null ? (Envelope.OkBody(rule!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/reconciliations")]
    public IResult Reconcile(string id, [FromQuery] string runId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        if (string.IsNullOrWhiteSpace(runId)) return Envelope.Fail("VALIDATION_ERROR", "runId is required.", 400);
        var (data, code, message) = inputs.ReconcileContribution(u, id, runId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/garnishment-orders")]
public sealed class GarnishmentOrdersController(IPayrollInputsService inputs) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok<object>(inputs.ListGarnishments(u, employmentId));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateGarnishmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId") ?? Guards.Check(request.Kind, "Kind", 50)
            ?? Guards.Check(request.CourtRef, "CourtRef", 50)
            ?? (request.Priority <= 0 ? "Priority must be greater than 0." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (order, code, message) = inputs.CreateGarnishment(u, request);
        return code is null ? (Envelope.OkBody(order!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 043 AC2: authorized status change — payroll:release (R-FIN) + evidence
    [HttpPost("{id}/releases")]
    public IResult Release(string id, Dtos.ReleaseGarnishmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Evidence)) return Envelope.Fail("VALIDATION_ERROR", "A release requires Evidence (043 AC2).", 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:release")) return Envelope.Fail("FORBIDDEN", "payroll:release permission required.", 403);
        var (order, code, message) = inputs.ReleaseGarnishment(u, id, request);
        return code is null ? Envelope.Ok<object>(order!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/benefit-plans")]
public sealed class BenefitPlansController(IPayrollInputsService inputs) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok<object>(inputs.ListBenefitPlans(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateBenefitPlanRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.PayCodeId, "PayCodeId", 50);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (plan, code, message) = inputs.CreateBenefitPlan(u, request);
        return code is null ? (Envelope.OkBody(plan!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/benefit-enrollments")]
public sealed class BenefitEnrollmentsController(IPayrollInputsService inputs) : ControllerBase
{
    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = inputs.EnrollmentDetail(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateEnrollmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId") ?? Guards.Check(request.PlanId, "PlanId")
            ?? (request.EffectiveFrom == default ? "EffectiveFrom is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (enrollment, code, message) = inputs.CreateEnrollment(u, request);
        return code is null ? (Envelope.OkBody(enrollment!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/provider-feeds")]
    public IResult ProviderFeed(string id, Dtos.PostProviderFeedRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Period, "Period", 7) ?? Guards.Check(request.SourceEventId, "SourceEventId", 100);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payinputs:write")) return (Envelope.FailBody("FORBIDDEN", "payinputs:write permission required."), 403);
        var (data, code, message) = inputs.PostProviderFeed(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/reconciliations")]
    public IResult Reconcile(string id, [FromQuery] string period)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        if (string.IsNullOrWhiteSpace(period)) return Envelope.Fail("VALIDATION_ERROR", "period is required.", 400);
        var (data, code, message) = inputs.ReconcileEnrollment(u, id, period);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
