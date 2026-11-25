using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// MF5 — shop-floor reporting extensions (ERP-MFG-044..049). mfg:shopfloor on every write; the
// service enforces maker-checker (labor approval ≠ reporter, correction approval ≠ proposer).
[ApiController]
[Route("api/v1/production-orders/{orderId}/labor-time")]
public sealed class LaborTimeController(IShopFloorService shopFloor) : ControllerBase
{
    [HttpGet]
    public IResult List(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(shopFloor.LaborEntries(orderId));
    }

    [HttpPost]
    public IResult Report(string orderId, LaborTimeRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:shopfloor")) return (Envelope.FailBody("FORBIDDEN", "mfg:shopfloor permission required."), 403);
        var (data, code, message) = shopFloor.ReportLabor(u, orderId, request);
        return code switch
        {
            null => (Envelope.OkBody(data!), 201),
            "SHOPFLOOR_REPLAY" => (Envelope.OkBody(data!), 200),
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });

    [HttpPost("{entryId}/approvals")]
    public IResult Approve(string orderId, string entryId, [FromBody] CorrectionApprovalRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:shopfloor")) return (Envelope.FailBody("FORBIDDEN", "mfg:shopfloor permission required."), 403);
        var (data, code, message) = shopFloor.ApproveLabor(u, orderId, entryId);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/production-orders/{orderId}/counter-events")]
public sealed class CounterEventsController(IShopFloorService shopFloor) : ControllerBase
{
    [HttpGet]
    public IResult List(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(shopFloor.CounterEvents(orderId));
    }

    [HttpPost]
    public IResult Report(string orderId, CounterEventRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:shopfloor")) return (Envelope.FailBody("FORBIDDEN", "mfg:shopfloor permission required."), 403);
        var (data, code, message) = shopFloor.ReportCounter(u, orderId, request);
        return code switch
        {
            null => (Envelope.OkBody(data!), 201),
            "SHOPFLOOR_REPLAY" => (Envelope.OkBody(data!), 200), // duplicate source id — idempotent
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });
}

[ApiController]
[Route("api/v1/production-orders/{orderId}/downtime-events")]
public sealed class DowntimeEventsController(IShopFloorService shopFloor) : ControllerBase
{
    [HttpGet]
    public IResult List(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(shopFloor.DowntimeEvents(orderId));
    }

    [HttpPost]
    public IResult Report(string orderId, DowntimeEventRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:shopfloor")) return (Envelope.FailBody("FORBIDDEN", "mfg:shopfloor permission required."), 403);
        var (data, code, message) = shopFloor.ReportDowntime(u, orderId, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}

[ApiController]
[Route("api/v1/production-orders/{orderId}")]
public sealed class ShopFloorOrderController(IShopFloorService shopFloor) : ControllerBase
{
    [HttpGet("wip-projection")]
    public IResult Projection(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        var (data, code, message) = shopFloor.WipProjection(orderId);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpGet("wip-transfers")]
    public IResult Transfers(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(shopFloor.WipTransfers(orderId));
    }

    [HttpPost("wip-transfers")]
    public IResult Transfer(string orderId, WipTransferRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:shopfloor")) return (Envelope.FailBody("FORBIDDEN", "mfg:shopfloor permission required."), 403);
        var (data, code, message) = shopFloor.TransferWip(u, orderId, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}

[ApiController]
[Route("api/v1/production-corrections")]
public sealed class ProductionCorrectionsController(IShopFloorService shopFloor) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(shopFloor.Corrections());
    }

    [HttpPost]
    public IResult Propose(CorrectionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:shopfloor")) return (Envelope.FailBody("FORBIDDEN", "mfg:shopfloor permission required."), 403);
        var (data, code, message) = shopFloor.ProposeCorrection(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, [FromBody] CorrectionApprovalRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:shopfloor")) return (Envelope.FailBody("FORBIDDEN", "mfg:shopfloor permission required."), 403);
        var (data, code, message) = shopFloor.ApproveCorrection(u, id, request ?? new CorrectionApprovalRequest(null));
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}
