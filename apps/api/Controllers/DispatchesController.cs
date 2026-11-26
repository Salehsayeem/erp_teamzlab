using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P4 dispatch evidence (055) — the brief's receipt-workspace "new controllers" home for the
// /purchase-orders/{id}/dispatches route; Idempotency-Key makes retries duplicate-free.
[ApiController]
[Route("api/v1/purchase-orders/{poId}/dispatches")]
public sealed class DispatchesController(IReceiptInspectionService receipts) : ControllerBase
{
    [HttpPost]
    public IResult Send(string poId, CreateDispatchRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts:write")) return (Envelope.FailBody("FORBIDDEN", "receipts:write permission required."), 403);
        var (dispatch, code, error) = receipts.SendDispatch(u, poId, request.OrderVersion, request.State, request.Detail);
        return code is null ? (Envelope.OkBody(dispatch!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });
}
