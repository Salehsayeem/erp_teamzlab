using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Rebates (ERP-SALES-063): agreements, accruals, claims, settlement approvals. Settlement
// authority (rebates:settle) is R-FIN; claims are capped at the approved accrual.
[ApiController]
[Route("api/v1")]
public sealed class RebatesController(IRebateService rebates) : ControllerBase
{
    [HttpGet("rebate-agreements")]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("rebates:write")) return Envelope.Fail("FORBIDDEN", "rebates:write permission required.", 403);
        var (data, code, message) = rebates.ListAgreements();
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("rebate-agreements")]
    public IResult CreateAgreement(CreateRebateAgreementRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CustomerId, "CustomerId") ?? Guards.Check(request.Basis, "Basis", 50)
            ?? Guards.Check(request.AccrualPeriod, "AccrualPeriod", 20);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = rebates.CreateAgreement(u, request.CustomerId, request.Basis,
            request.RatePct, request.AccrualPeriod, request.ValidFrom);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("rebate-agreements/{id}/accruals")]
    public IResult CalculateAccrual(string id, CalculateAccrualRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Period, "Period", 7);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = rebates.CalculateAccrual(u, id, request.Period);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("rebate-agreements/{id}/accruals/{period}/approvals")]
    public IResult ApproveAccrual(string id, string period) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = rebates.ApproveAccrual(u, id, period);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("rebate-claims")]
    public IResult CreateClaim(CreateRebateClaimRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.AgreementId, "AgreementId") ?? Guards.Check(request.Period, "Period", 7)
            ?? Guards.CheckPositive(request.Amount, "Amount")
            ?? (request.EligibleTxnIds is null || request.EligibleTxnIds.Count == 0
                ? "At least one eligible transaction id is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = rebates.CreateClaim(u, request.AgreementId, request.Period, request.Amount, request.EligibleTxnIds);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("rebate-claims/{id}/approvals")]
    public IResult ApproveClaim(string id, ApproveRebateClaimRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = rebates.ApproveClaim(u, id, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // retro-change edge: preview the delta before any approved adjustment posts
    [HttpGet("rebate-agreements/{id}/recalculation-previews")]
    public IResult RecalculationPreview(string id, [FromQuery] string period, [FromQuery] decimal newRatePct)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("rebates:write")) return Envelope.Fail("FORBIDDEN", "rebates:write permission required.", 403);
        var (data, code, message) = rebates.RecalculationPreview(id, period, newRatePct);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
