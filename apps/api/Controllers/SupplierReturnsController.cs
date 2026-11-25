using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P4 returns/claims (059): linked records only, closure needs outcome + reconciliation status and a
// reviewer other than the claim's creator (suppliers:review).
[ApiController]
[Route("api/v1/supplier-returns")]
public sealed class SupplierReturnsController(IReceiptInspectionService receipts) : ControllerBase
{
    [HttpPost]
    public IResult Create(CreateSupplierReturnRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts:write")) return (Envelope.FailBody("FORBIDDEN", "receipts:write permission required."), 403);
        var (ret, code, error) = receipts.CreateReturn(u, request.ReceiptId, request.PoId, request.ItemSku, request.Qty, request.Reason, request.Kind);
        return code is null ? (Envelope.OkBody(ret!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/supplier-claims")]
public sealed class SupplierClaimsController(IReceiptInspectionService receipts) : ControllerBase
{
    [HttpPost]
    public IResult Create(CreateSupplierClaimRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts:write")) return (Envelope.FailBody("FORBIDDEN", "receipts:write permission required."), 403);
        var (claim, code, error) = receipts.CreateClaim(u, request.ReceiptId, request.PoId, request.Kind, request.Amount);
        return code is null ? (Envelope.OkBody(claim!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/closures")]
    public IResult Close(string id, CloseClaimRequest request)
    {
        var (claim, code, error) = receipts.CloseClaim(CurrentUser.From(HttpContext), id, request.OutcomeCode, request.ReconciliationStatus);
        return code is null ? Envelope.Ok<object>(claim!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }
}
