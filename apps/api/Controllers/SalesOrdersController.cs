using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/sales-orders")]
public sealed class SalesOrdersController(ISalesOrderService orders) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(orders.List());

    [HttpPost]
    public IResult Create(CreateSoRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("so:write")) return (Envelope.FailBody("FORBIDDEN", "so:write permission required."), 403);
        var lineErr = Guards.CheckLines(request.Lines);
        if (lineErr is not null) return (Envelope.FailBody("VALIDATION_ERROR", lineErr), 400);
        var (so, code, error) = orders.Create(u, request.CustomerId, request.Lines, request.SourceQuoteRevisionId);
        return code is null ? (Envelope.OkBody(so!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    // shortcut: SO submit has no separate permission gate — the approval gate is maker-checker
    [HttpPost("{id}/submit")]
    public IResult Submit(string id)
    {
        var (data, code, message) = orders.Submit(CurrentUser.From(HttpContext), id);
        // 013 AC2: the onboarding gate returns the unmet control ids as items
        if (code == "ONBOARDING_CONTROLS_UNMET")
            return Envelope.From(Envelope.FailBodyItems(code, message!, (object[])data!), ErrorStatus.StatusFor(code));
        return data is null ? Envelope.Fail(code!, message!, ErrorStatus.StatusFor(code!)) : Envelope.Ok<object>(data);
    }

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, ApproveSoRequest request)
    {
        var (so, code, error) = orders.Approve(CurrentUser.From(HttpContext), id, request.Reason);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(so!);
    }

    [HttpPost("{id}/shipments")]
    public IResult Ship(string id, ShipRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("shipments:write")) return Envelope.Fail("FORBIDDEN", "shipments:write permission required.", 403);
        var (shipment, journal, code, error) = orders.Ship(u, id, request.ItemSku, request.Qty);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(new { shipment, journal }, 201);
    }

    // R-T7f: the journal-only /sales-orders/{id}/invoices endpoint was RETIRED — invoice
    // documents (draft → maker-checker posting) live in InvoicesController instead.

    // 057 AC2: releasing a credit-held order is a credit:override act, audited + outboxed
    [HttpPost("{id}/credit-releases")]
    public IResult ReleaseCredit(string id, ReleaseCreditRequest request)
    {
        var (so, code, error) = orders.ReleaseCreditHold(CurrentUser.From(HttpContext), id);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(so!);
    }
}
