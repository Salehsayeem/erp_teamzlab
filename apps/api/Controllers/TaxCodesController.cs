using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/tax-codes")]
public sealed class TaxCodesController(ITaxCodeService taxCodes) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("tax:read")
            ? Envelope.Ok<object>(taxCodes.List())
            : Envelope.Fail("FORBIDDEN", "tax:read permission required.", 403);
    }

    [HttpPost]
    public IResult Create(CreateTaxCodeRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        // 032 edge: a zero/exempt rate is distinct from an unconfigured rate and needs a reason;
        // fix round 1: Zero/Exempt with a non-zero rate would tax exempt sales at that rate —
        // rejected at publish so the computation never sees the contradiction
        var err = Guards.Check(request.Code, "Code", 50)
            ?? (request.RatePct < 0 ? "RatePct must be >= 0." : null)
            ?? (request.Basis is TaxBasis.Zero or TaxBasis.Exempt && string.IsNullOrWhiteSpace(request.Reason)
                ? "A reason is required for Zero or Exempt basis tax codes." : null)
            ?? (request.Basis is TaxBasis.Zero or TaxBasis.Exempt && request.RatePct != 0m
                ? "Zero-rated and exempt tax codes must carry rate 0." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("tax:write")) return (Envelope.FailBody("FORBIDDEN", "tax:write permission required."), 403);
        var jurisdiction = ((LegalEntity)HttpContext.Items["entity"]!).Jurisdiction; // 033: entity-scoped, no fallback
        var (taxCode, code, error) = taxCodes.Create(u, request, jurisdiction);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(taxCode!), 201);
    });
}
