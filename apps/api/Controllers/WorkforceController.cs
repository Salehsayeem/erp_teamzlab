using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM10 — workforce plans, allocation, analytics. workforce:write (R-HR) makes plans/sets;
// allocation approval is R-FIN (money-adjacent, approver ≠ maker); analytics enforce suppression
// server-side (people:analytics:read).
[ApiController]
[Route("api/v1/workforce-plans")]
public sealed class WorkforcePlansController(IWorkforceService workforce) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(workforce.ListPlans(u));
    }

    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        var (data, code, message) = workforce.GetPlan(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateWorkforcePlanRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? (request.HorizonStart == default ? "HorizonStart is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workforce:write")) return (Envelope.FailBody("FORBIDDEN", "workforce:write permission required."), 403);
        var (plan, code, message) = workforce.CreatePlan(u, request);
        return code is null ? (Envelope.OkBody(plan!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 059 AC2: promotion into positions is an explicit workflow creating linked records
    [HttpPost("{id}/promotions")]
    public IResult Promote(string id, Dtos.PromotePlanRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workforce:write")) return (Envelope.FailBody("FORBIDDEN", "workforce:write permission required."), 403);
        var (data, code, message) = workforce.Promote(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/allocation-sets")]
public sealed class AllocationSetsController(IWorkforceService workforce) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        return Envelope.Ok<object>(workforce.ListAllocationSets(u));
    }

    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = workforce.GetAllocationSet(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateAllocationSetRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Unit, "Unit", 20)
            ?? (request.Unit is not ("Pct" or "Hours") ? "Unit must be Pct or Hours." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workforce:write")) return (Envelope.FailBody("FORBIDDEN", "workforce:write permission required."), 403);
        var (set, code, message) = workforce.CreateAllocationSet(u, request);
        return code is null ? (Envelope.OkBody(set!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 060: R-FIN approval, 100% gate, approver ≠ maker
    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, Dtos.ApproveAllocationSetRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:approve")) return (Envelope.FailBody("FORBIDDEN", "payroll:approve permission required."), 403);
        var (data, code, message) = workforce.ApproveAllocationSet(u, id);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/reconciliations")]
    public IResult Reconcile(string id, [FromQuery] string runId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        if (string.IsNullOrWhiteSpace(runId)) return Envelope.Fail("VALIDATION_ERROR", "runId is required.", 400);
        var (data, code, message) = workforce.ReconcileAllocation(u, id, runId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

[ApiController]
[Route("api/v1/people-analytics")]
public sealed class PeopleAnalyticsController(IWorkforceService workforce) : ControllerBase
{
    // 061: suppression enforced SERVER-side (UI never receives suppressed values); the CSV
    // export re-applies the same rule including the complementary total cell
    [HttpGet("{metric}")]
    public IResult Metric(string metric, [FromQuery] string? format)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("people:analytics:read")) return Envelope.Fail("FORBIDDEN", "people:analytics:read permission required.", 403);
        var (data, code, message) = workforce.Analytics(u, metric, csv: format == "csv");
        if (code is not null) return Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
        return format == "csv"
            ? Results.Text((string)data!, "text/csv")
            : Envelope.Ok<object>(data!);
    }
}
