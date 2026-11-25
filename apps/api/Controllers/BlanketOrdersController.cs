using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P3 blanket orders (054) — releases debit the ceiling atomically under a row lock.
[ApiController]
[Route("api/v1/blanket-orders")]
public sealed class BlanketOrdersController(IBlanketOrderService blankets) : ControllerBase
{
    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var detail = blankets.Detail(id);
        return detail is null ? Envelope.Fail("BLANKET_NOT_FOUND", "Blanket order not found.", 404) : Envelope.Ok<object>(detail);
    }

    [HttpPost]
    public IResult Create(CreateBlanketOrderRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("po:write")) return (Envelope.FailBody("FORBIDDEN", "po:write permission required."), 403);
        var (order, code, error) = blankets.Create(u, request.SupplierId, request.Currency, request.CeilingAmount, request.ValidFrom, request.ValidTo);
        return code is null ? (Envelope.OkBody(order!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/releases")]
    public IResult Release(string id, CreateBlanketReleaseRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("po:write")) return (Envelope.FailBody("FORBIDDEN", "po:write permission required."), 403);
        var (release, code, error) = blankets.Release(u, id, request.Amount, request.PoId);
        return code is null ? (Envelope.OkBody(release!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });
}
