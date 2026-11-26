using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/purchase-orders")]
public sealed class PurchaseOrdersController(IPurchaseOrderService orders, IPurchaseOrderChangeService changes) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(orders.List());

    // deferred PROCUREMENT wave: order detail with version (052/053 ack + dispatch pin)
    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var detail = changes.PoDetail(id);
        return detail is null ? Envelope.Fail("PO_NOT_FOUND", "Purchase order not found.", 404) : Envelope.Ok<object>(detail);
    }

    [HttpPost]
    public IResult Create(CreatePoRequest request) => Idem.Wrap(HttpContext, () =>
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

    // -------- deferred PROCUREMENT wave: change control (052) + supplier acks (053) --------

    [HttpGet("{id}/changes")]
    public IResult Changes(string id) => Envelope.Ok<object>(changes.Changes(id));

    [HttpPost("{id}/changes")]
    public IResult RequestChange(string id, CreatePoChangeRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("po:write")) return (Envelope.FailBody("FORBIDDEN", "po:write permission required."), 403);
        var (change, code, error) = changes.Create(u, id, request.Rationale, request.SupplierId, request.Lines);
        return code is null ? (Envelope.OkBody(ProjChange(change!)), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/changes/{changeId}/decisions")]
    public IResult DecideChange(string id, string changeId, DecidePoChangeRequest request)
    {
        var (change, code, error) = changes.Decide(CurrentUser.From(HttpContext), id, changeId, request.Approve);
        return code is null ? Envelope.Ok<object>(ProjChange(change!)) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/acknowledgements")]
    public IResult Acknowledge(string id, CreatePoAckRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("po:write")) return (Envelope.FailBody("FORBIDDEN", "po:write permission required."), 403);
        var (ack, code, error) = changes.Acknowledge(u, id, request.SupplierId, request.OrderVersion, request.Lines);
        return code is null ? (Envelope.OkBody(ProjAck(ack!)), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    private static object ProjChange(PurchaseOrderChange c) => new
    {
        c.Id, c.PoId, c.ChangeNo, c.Status, c.Rationale, c.IsMaterial, c.BaseOrderVersion,
        c.RequestedBy, c.RequestedAt, c.ApprovedBy, c.ApprovedAt,
    };

    private static object ProjAck(PurchaseOrderAcknowledgement a) => new
    {
        a.Id, a.PoId, a.OrderVersion, a.SupplierId, a.Status, a.HasDiscrepancy, a.ReceivedBy, a.ReceivedAt,
        late = a.Status is AckStatus.Superseded,
    };
}
