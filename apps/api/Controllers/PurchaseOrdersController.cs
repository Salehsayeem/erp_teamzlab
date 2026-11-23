using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/purchase-orders")]
public sealed class PurchaseOrdersController(IPurchaseOrderService orders) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(orders.List());

    [HttpPost]
    public IResult Create(CreatePoRequest request) => Idem.Wrap(HttpContext, Db.I, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("po:write")) return (Envelope.FailBody("FORBIDDEN", "po:write permission required."), 403);
        var lineErr = Guards.CheckLines(request.Lines);
        if (lineErr is not null) return (Envelope.FailBody("VALIDATION_ERROR", lineErr), 400);
        var (po, code, error) = orders.Create(u, request.SupplierId, request.Lines);
        return code is null ? (Envelope.OkBody(po!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/submit")]
    public IResult Submit(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("po:write")) return Envelope.Fail("FORBIDDEN", "po:write permission required.", 403);
        var (updated, code, message) = orders.Submit(u, id);
        return updated is null ? Envelope.Fail(code!, message!, ErrorStatus.StatusFor(code!)) : Envelope.Ok<object>(updated);
    }

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, ApprovePoRequest request)
    {
        var (po, code, error) = orders.Approve(CurrentUser.From(HttpContext), id, request.Reason);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(po!);
    }
}
