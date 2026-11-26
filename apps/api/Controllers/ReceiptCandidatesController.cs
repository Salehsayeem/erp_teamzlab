using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Receipt candidates + AR reconciliation (ERP-SALES-054/055/056). The recon read is open to
// ar:read OR invoices:read holders (R-FIN and R-SALES both qualify); candidates stay
// receipts-candidates:write. No allocation write exists here — allocation is finance-owned.
[ApiController]
[Route("api/v1")]
public sealed class ReceiptCandidatesController(IReceiptCandidatesService candidates) : ControllerBase
{
    [HttpGet("receipt-candidates")]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts-candidates:write")) return Envelope.Fail("FORBIDDEN", "receipts-candidates:write permission required.", 403);
        var (data, code, message) = candidates.List();
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("receipt-candidates")]
    public IResult Create(CreateReceiptCandidateRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.BankRef, "BankRef") ?? Guards.CheckPositive(request.Amount, "Amount")
            ?? Guards.Check(request.Currency, "Currency", 3) ?? Guards.Check(request.Channel, "Channel", 50);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = candidates.Create(u, request.CustomerId, request.BankRef,
            request.Amount, request.Currency, request.ValueDate, request.Channel);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("receipt-candidates/{id}/payer-links")]
    public IResult LinkPayer(string id, LinkPayerRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CustomerId, "CustomerId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = candidates.LinkPayer(u, id, request.CustomerId);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("ar-reconciliation")]
    public IResult Reconciliation([FromQuery] string? asOf)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("ar:read") && !u.Has("invoices:read"))
            return Envelope.Fail("FORBIDDEN", "ar:read or invoices:read permission required.", 403);
        var (data, code, message) = candidates.Reconciliation(asOf);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
