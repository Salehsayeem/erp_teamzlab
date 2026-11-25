using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM3 — job catalog & compensation structure (022/023). compmaster:write gates the catalog;
// out-of-policy comp changes route to the R-FIN exception approver inside the comp endpoint.
[ApiController]
[Route("api/v1/job-profiles")]
public sealed class JobCatalogController(IJobCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(catalog.ListProfiles(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateJobProfileRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Title, "Title")
            ?? (request.EffectiveFrom == default ? "EffectiveFrom is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("compmaster:write")) return (Envelope.FailBody("FORBIDDEN", "compmaster:write permission required."), 403);
        var (profile, code, message) = catalog.CreateProfile(u, request);
        return code is null ? (Envelope.OkBody(profile!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/versions")]
    public IResult CreateVersion(string id, Dtos.CreateJobProfileVersionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Title, "Title")
            ?? (request.EffectiveFrom == default ? "EffectiveFrom is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("compmaster:write")) return (Envelope.FailBody("FORBIDDEN", "compmaster:write permission required."), 403);
        var (version, code, message) = catalog.CreateProfileVersion(u, id, request);
        return code is null ? (Envelope.OkBody(version!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/grade-ranges")]
public sealed class GradeRangesController(IJobCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(catalog.ListGradeRanges(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateGradeRangeRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.GradeCode, "GradeCode", 50) ?? Guards.Check(request.Currency, "Currency")
            ?? Guards.Check(request.Frequency, "Frequency", 20)
            ?? (request.MaxAmount <= 0 ? "MaxAmount must be greater than 0." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("compmaster:write")) return (Envelope.FailBody("FORBIDDEN", "compmaster:write permission required."), 403);
        var (range, code, message) = catalog.CreateGradeRange(u, request);
        return code is null ? (Envelope.OkBody(range!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/comp-changes")]
public sealed class CompChangesController(IJobCatalogService catalog) : ControllerBase
{
    // 023 AC2 gate feeding the comp endpoint: resolves the worker's graded position → range and
    // reports inPolicy/approvalRequired BEFORE a change is attempted
    [HttpPost("{workerId}/policy-checks")]
    public IResult PolicyCheck(string workerId, Dtos.CompPolicyCheckRequest request)
    {
        var err = request.Amount <= 0 ? "Amount must be greater than 0." : null;
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("compmaster:write")) return Envelope.Fail("FORBIDDEN", "compmaster:write permission required.", 403);
        var (data, code, message) = catalog.CompPolicyCheck(u, workerId, request);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
