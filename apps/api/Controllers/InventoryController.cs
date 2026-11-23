using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// No single prefix — this slice spans /stock, /warehouses and /locations (all under /api/v1)
[ApiController]
[Route("api/v1")]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    [HttpGet("stock")]
    public IResult Stock() => Envelope.Ok<object>(inventory.ListStock());

    [HttpGet("warehouses")]
    public IResult ListWarehouses() => Envelope.Ok<object>(inventory.ListWarehouses());

    [HttpPost("warehouses")]
    public IResult CreateWarehouse(CreateWarehouseRequest request) => Idem.Wrap(HttpContext, Db.I, () =>
    {
        var err = Guards.Check(request.Code, "Code") ?? Guards.Check(request.Name, "Name");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("inventory:write")) return (Envelope.FailBody("FORBIDDEN", "inventory:write permission required."), 403);
        var (wh, code, error) = inventory.CreateWarehouse(u, request.Code, request.Name);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(wh!), 201);
    });

    [HttpGet("locations")]
    public IResult ListLocations() => Envelope.Ok<object>(inventory.ListLocations());

    [HttpPost("locations")]
    public IResult CreateLocation(CreateLocationRequest request) => Idem.Wrap(HttpContext, Db.I, () =>
    {
        var err = Guards.Check(request.Code, "Code") ?? Guards.Check(request.Name, "Name");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("inventory:write")) return (Envelope.FailBody("FORBIDDEN", "inventory:write permission required."), 403);
        var (loc, code, error) = inventory.CreateLocation(u, request.WarehouseId, request.Code, request.Name);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(loc!), 201);
    });

    [HttpPost("stock/transfers")]
    public IResult Transfer(CreateTransferRequest request) => Idem.Wrap(HttpContext, Db.I, () =>
    {
        var err = Guards.Check(request.FromLocationId, "FromLocationId") ?? Guards.Check(request.ToLocationId, "ToLocationId")
            ?? Guards.Check(request.ItemSku, "ItemSku")
            ?? (request.Qty <= 0 ? "Transfer qty must be greater than 0." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("inventory:transfer")) return (Envelope.FailBody("FORBIDDEN", "inventory:transfer permission required."), 403);
        var (transfer, code, error) = inventory.Transfer(u, request.FromLocationId, request.ToLocationId, request.ItemSku, request.Qty);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(transfer!), 201);
    });

    [HttpPost("stock/counts")]
    public IResult OpenCount(CreateCountRequest request) => Idem.Wrap(HttpContext, Db.I, () =>
    {
        var lineErr = Guards.CheckCountItems(request.Items);
        if (lineErr is not null) return (Envelope.FailBody("VALIDATION_ERROR", lineErr), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("inventory:write")) return (Envelope.FailBody("FORBIDDEN", "inventory:write permission required."), 403);
        var (count, code, error) = inventory.OpenCount(u, request.LocationId, request.Items);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(count!), 201);
    });

    [HttpPost("stock/counts/{id}/close")]
    public IResult CloseCount(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("inventory:write")) return Envelope.Fail("FORBIDDEN", "inventory:write permission required.", 403);
        var (count, adjustments, code, error) = inventory.Close(u, id);
        return code is not null
            ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(new { count, adjustments }, 201);
    }

    [HttpGet("stock/valuation")]
    public IResult Valuation()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("journals:read")
            ? Envelope.Ok<object>(inventory.Valuation())
            : Envelope.Fail("FORBIDDEN", "journals:read permission required.", 403);
    }
}
