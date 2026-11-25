using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerService customers, ICollectionService collections, ICustomer360Service customer360) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(customers.List());

    [HttpPost]
    public IResult Create(CreateCustomerRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        // review 7b: the extension fields are guarded when supplied (absent stays defaulted)
        var err = Guards.Check(request.Name, "Name") ?? Guards.Check(request.TaxId, "TaxId") ?? Guards.CheckEmail(request.Email)
            ?? (request.CreditLimit is < 0 ? "CreditLimit must be >= 0." : null)
            ?? (request.PaymentTermNetDays is < 1 ? "PaymentTermNetDays must be >= 1." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return (Envelope.FailBody("FORBIDDEN", "customers:write permission required."), 403);
        var (data, code, message) = customers.Create(u, request);
        return code is null ? (Envelope.OkBody(data), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 057: credit exposure read — credit:read gate (R-FIN seed), explicit no-data flag (057 edge)
    [HttpGet("{id}/credit-exposure")]
    public IResult CreditExposure(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("credit:read")) return Envelope.Fail("FORBIDDEN", "credit:read permission required.", 403);
        var (data, code, message) = collections.CreditExposure(id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    // --- deferred-screens wave (S3): composite detail / hierarchy / onboarding / merge ---

    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return Envelope.Fail("FORBIDDEN", "customers:write permission required.", 403);
        var (data, code, message) = customer360.Detail(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/hierarchy")]
    public IResult SetHierarchy(string id, SetHierarchyRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return (Envelope.FailBody("FORBIDDEN", "customers:write permission required."), 403);
        var (data, code, message) = customer360.SetHierarchy(u, id, request.ParentId);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/onboarding-checks")]
    public IResult OnboardingChecks(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return Envelope.Fail("FORBIDDEN", "customers:write permission required.", 403);
        var (data, code, message) = customer360.ChecksList(id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/onboarding-checks")]
    public IResult AddOnboardingCheck(string id, AddOnboardingCheckRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CheckCode, "CheckCode", 100);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return (Envelope.FailBody("FORBIDDEN", "customers:write permission required."), 403);
        var (data, code, message) = customer360.AddOnboardingCheck(u, id, request.CheckCode, request.Mandatory);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/onboarding-checks/{checkCode}/met")]
    public IResult MarkCheckMet(string id, string checkCode) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return (Envelope.FailBody("FORBIDDEN", "customers:write permission required."), 403);
        var (data, code, message) = customer360.MarkCheckMet(u, id, checkCode);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // merge routes hang off their own prefix (leading slash overrides the class route);
    // preview needs customers:write, execution the elevated customers:merge (Q-S3: R-ADMIN)
    [HttpPost("/api/v1/customer-merges/preview")]
    public IResult MergePreview(MergePreviewRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.SurvivorId, "SurvivorId") ?? Guards.Check(request.MergedId, "MergedId")
            ?? Guards.Check(request.Reason, "Reason");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return (Envelope.FailBody("FORBIDDEN", "customers:write permission required."), 403);
        var (data, code, message) = customer360.MergePreview(u, request.SurvivorId, request.MergedId, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("/api/v1/customer-merges/{previewId}/executions")]
    public IResult MergeExecute(string previewId, MergeExecuteRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = customer360.MergeExecute(u, previewId);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
