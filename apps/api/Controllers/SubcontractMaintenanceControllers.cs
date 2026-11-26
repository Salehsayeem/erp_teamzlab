using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// MF6 — subcontract operations (ERP-MFG-050/051/052) and MF7 — maintenance interface
// (ERP-MFG-053/054). Writes need mfg:write / mfg:read respectively; the completion gate lives
// with the landed completion path (accepted-quantity bound).
[ApiController]
[Route("api/v1/routings/{routingId}/subcontract-operations")]
public sealed class SubcontractOperationsController(ISubcontractService subcontract) : ControllerBase
{
    [HttpPost]
    public IResult Add(string routingId, AddSubcontractOperationRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.OperationCode, "OperationCode")
            ?? Guards.Check(request.WorkCenterId, "WorkCenterId")
            ?? Guards.Check(request.SupplierId, "SupplierId")
            ?? Guards.Check(request.ServiceDescription, "ServiceDescription");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = subcontract.AddOperation(u, routingId, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}

[ApiController]
[Route("api/v1/subcontract-custodies")]
public sealed class SubcontractCustodiesController(ISubcontractService subcontract) : ControllerBase
{
    [HttpPost]
    public IResult Record(CustodyRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.OrderId, "OrderId") ?? Guards.Check(request.InventoryMovementId, "InventoryMovementId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = subcontract.RecordCustody(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}

[ApiController]
[Route("api/v1/subcontract-orders/{orderId}")]
public sealed class SubcontractOrdersController(ISubcontractService subcontract) : ControllerBase
{
    [HttpGet("custodies")]
    public IResult Custodies(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(subcontract.Custodies(orderId));
    }

    [HttpGet("receipts")]
    public IResult Receipts(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(subcontract.Receipts(orderId));
    }

    [HttpPost("receipts")]
    public IResult Receive(string orderId, SubcontractReceiptRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = subcontract.Receive(u, orderId, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}

[ApiController]
[Route("api/v1/maintenance-availability-events")]
public sealed class MaintenanceController(IMaintenanceService maintenance) : ControllerBase
{
    [HttpPost]
    public IResult Ingest(MaintenanceEventRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = maintenance.Ingest(u, request);
        return code switch
        {
            null => (Envelope.OkBody(data!), 201),
            "MAINTENANCE_REPLAY" => (Envelope.OkBody(data!), 200), // duplicate / out-of-order — idempotent
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });
}

[ApiController]
[Route("api/v1/schedule-impacts")]
public sealed class ScheduleImpactsController(IMaintenanceService maintenance) : ControllerBase
{
    [HttpGet]
    public IResult Impacts([FromQuery] string? outage)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        if (string.IsNullOrWhiteSpace(outage))
            return Envelope.Fail("VALIDATION_ERROR", "The outage query parameter is required.", 400);
        var (data, code, message) = maintenance.Impacts(outage);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpPost("resolutions")]
    public IResult Resolve(MaintenanceResolutionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:schedule")) return (Envelope.FailBody("FORBIDDEN", "mfg:schedule permission required."), 403);
        var (data, code, message) = maintenance.ResolveImpact(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}
