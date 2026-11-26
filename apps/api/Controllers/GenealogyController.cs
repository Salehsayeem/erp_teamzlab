using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// MF10 — genealogy trace, rework orders, close reconciliation (ERP-MFG-064..070).
// Reads need mfg:read; rework writes need mfg:write with approval behind mfg:rework.
[ApiController]
[Route("api/v1/genealogy")]
public sealed class GenealogyController(IGenealogyService genealogy) : ControllerBase
{
    [HttpGet]
    public IResult Trace([FromQuery] string? lot, [FromQuery] string? sku)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        if (string.IsNullOrWhiteSpace(lot) && string.IsNullOrWhiteSpace(sku))
            return Envelope.Fail("VALIDATION_ERROR", "A lot or sku query parameter is required.", 400);
        return Envelope.Ok<object>(genealogy.Trace(lot, sku));
    }
}

[ApiController]
[Route("api/v1/rework-orders")]
public sealed class ReworkOrdersController(IGenealogyService genealogy) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(genealogy.ListRework());
    }

    [HttpPost]
    public IResult Create(CreateReworkRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.SourceOrderId, "SourceOrderId") ?? Guards.Check(request.ItemSku, "ItemSku");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = genealogy.CreateRework(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, [FromBody] ReworkApprovalRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:rework")) return (Envelope.FailBody("FORBIDDEN", "mfg:rework permission required."), 403);
        var (data, code, message) = genealogy.ApproveRework(u, id, request ?? new ReworkApprovalRequest(null));
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });

    [HttpPost("{id}/reinspections")]
    public IResult LinkReinspection(string id, ReworkReinspectionRequest request)
    {
        var err = Guards.Check(request.InspectionId, "InspectionId");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return Envelope.Fail("FORBIDDEN", "mfg:write permission required.", 403);
        var (data, code, message) = genealogy.LinkReinspection(u, id, request);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpPost("{id}/completions")]
    public IResult Complete(string id) => Idem.Wrap(HttpContext, (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = genealogy.CompleteRework(u, id);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/production-orders/{orderId}/close-reconciliation")]
public sealed class CloseReconciliationController(IGenealogyService genealogy) : ControllerBase
{
    [HttpGet]
    public IResult Reconcile(string orderId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        var (data, code, message) = genealogy.CloseReconciliation(orderId);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpPost("variance-dispositions")]
    public IResult DisposeVariance(string orderId, VarianceDispositionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:close")) return (Envelope.FailBody("FORBIDDEN", "mfg:close permission required."), 403);
        var (data, code, message) = genealogy.DisposeVariance(u, orderId, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}

[ApiController]
[Route("api/v1/variance-dispositions")]
public sealed class VarianceDispositionsController(IGenealogyService genealogy) : ControllerBase
{
    [HttpPost("{id}/decisions")]
    public IResult Decide(string id, VarianceDecisionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:close")) return (Envelope.FailBody("FORBIDDEN", "mfg:close permission required."), 403);
        var (data, code, message) = genealogy.DecideVariance(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}
