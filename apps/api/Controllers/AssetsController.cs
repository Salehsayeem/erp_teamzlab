using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Fixed assets: register + capitalization (maker-checker) + depreciation runs + transfers +
// impairments + disposals (ERP-ASSETS-041..056). Approval endpoints take the pending event id.
[ApiController]
[Route("api/v1/assets")]
public sealed class AssetsController(IAssetService assets) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("assets:read")) return Envelope.Fail("FORBIDDEN", "assets:read permission required.", 403);
        var (ok, value) = Query.Enum<AssetStatus>(status);
        return ok ? Envelope.Ok<object>(assets.List(value))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown asset status '{status}'.", 400);
    }

    [HttpPost]
    public IResult Create(CreateAssetRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.Tag, "Tag") ?? Guards.Check(request.Serial, "Serial") ?? Guards.Check(request.Name, "Name")
            ?? Guards.Check(request.Category, "Category") ?? Guards.Check(request.LocationId, "LocationId")
            ?? Guards.CheckPositive(request.Cost, "Cost");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("assets:write")) return (Envelope.FailBody("FORBIDDEN", "assets:write permission required."), 403);
        var (data, code, message) = assets.Create(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/capitalizations")]
    public IResult ProposeCapitalization(string id, CapitalizeAssetRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.CheckPositive(request.Amount, "Amount");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("assets:write")) return (Envelope.FailBody("FORBIDDEN", "assets:write permission required."), 403);
        var (data, code, message) = assets.ProposeCapitalization(u, id, request.SourceProjectId, request.Amount);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/capitalizations/{capEventId}/approvals")]
    public IResult ApproveCapitalization(string id, string capEventId)
    {
        var (data, code, message) = assets.ApproveCapitalization(CurrentUser.From(HttpContext), id, capEventId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("depreciation-runs")]
    public IResult RunDepreciation(RunDepreciationRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.PeriodId, "PeriodId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("assets:approve")) return (Envelope.FailBody("FORBIDDEN", "assets:approve permission required."), 403);
        var key = HttpContext.Request.Headers["Idempotency-Key"].FirstOrDefault(); // the key IS the run id
        var (data, code, message) = assets.RunDepreciation(u, key, request.PeriodId);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/transfers")]
    public IResult ProposeTransfer(string id, TransferAssetRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.ToLocationId, "ToLocationId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("assets:write")) return (Envelope.FailBody("FORBIDDEN", "assets:write permission required."), 403);
        var (data, code, message) = assets.ProposeTransfer(u, id, request.ToLocationId, request.EffectiveAt);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/impairments")]
    public IResult ProposeImpairment(string id, ImpairAssetRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.CheckPositive(request.Amount, "Amount") ?? Guards.Check(request.Reason, "Reason");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("assets:write")) return (Envelope.FailBody("FORBIDDEN", "assets:write permission required."), 403);
        var (data, code, message) = assets.ProposeImpairment(u, id, request.Amount, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/impairments/{eventId}/approvals")]
    public IResult ApproveImpairment(string id, string eventId)
    {
        var (data, code, message) = assets.ApproveImpairment(CurrentUser.From(HttpContext), id, eventId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/disposals")]
    public IResult ProposeDisposal(string id, DisposeAssetRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = (request.Proceeds < 0 ? "Proceeds must be 0 or greater." : null) ?? Guards.Check(request.Reason, "Reason");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("assets:write")) return (Envelope.FailBody("FORBIDDEN", "assets:write permission required."), 403);
        var (data, code, message) = assets.ProposeDisposal(u, id, request.Proceeds, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/disposals/{eventId}/approvals")]
    public IResult ApproveDisposal(string id, string eventId)
    {
        var (data, code, message) = assets.ApproveDisposal(CurrentUser.From(HttpContext), id, eventId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
