using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/receipts")]
public sealed class ReceiptsController(IReceiptService receipts) : ControllerBase
{
    [HttpPost]
    public IResult Create(CreateReceiptRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts:write")) return (Envelope.FailBody("FORBIDDEN", "receipts:write permission required."), 403);
        var (receipt, journal, code, error) = receipts.Receive(u, request.PurchaseOrderId, request.ItemSku, request.Qty);
        return code is not null
            ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(new { receipt, journal }), 201);
    });
}
