using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Returns/RMA + refunds (ERP-SALES-061/062). Refund requests hang off approved credit
// adjustments; their approval is the finance-only refunds:approve act.
[ApiController]
[Route("api/v1")]
public sealed class SalesReturnsController(
    ISalesReturnService returns, IRefundService refunds) : ControllerBase
{
    [HttpGet("sales-returns")]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("returns:write")) return Envelope.Fail("FORBIDDEN", "returns:write permission required.", 403);
        var (data, code, message) = returns.List();
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpGet("sales-returns/{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("returns:write")) return Envelope.Fail("FORBIDDEN", "returns:write permission required.", 403);
        var (data, code, message) = returns.Detail(id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("sales-returns")]
    public IResult Create(CreateReturnRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.OrderId, "OrderId")
            ?? (request.Lines is null || request.Lines.Count == 0 ? "At least one return line is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = returns.Create(u, request.OrderId, request.Lines, request.OverrideJustification);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("sales-returns/{id}/receipts")]
    public IResult Receive(string id, ReceiveReturnRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        if (request.ReceivedQty <= 0) return (Envelope.FailBody("VALIDATION_ERROR", "Received qty must be greater than 0."), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = returns.Receive(u, id, request.ReceivedQty);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // refund requests link an approved credit; idempotent retry returns the same record
    [HttpPost("adjustments/{id}/refund-requests")]
    public IResult RequestRefund(string id) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, replayed, code, message) = refunds.Request(u, id);
        return code is null ? (Envelope.OkBody(data!), replayed ? 200 : 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("refund-requests/{id}/approvals")]
    public IResult ApproveRefund(string id, ApproveRefundRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = refunds.Approve(u, id, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // read-gap: RefundService.List existed service-side with no route — the read-back
    // mirrors the write family (refunds:approve settles, returns:write requests)
    [HttpGet("refunds")]
    public IResult ListRefunds()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("refunds:approve") && !u.Has("returns:write"))
            return Envelope.Fail("FORBIDDEN", "refunds:approve or returns:write permission required.", 403);
        var (data, code, message) = refunds.List();
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
