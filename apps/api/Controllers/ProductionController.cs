using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Production-order lifecycle + shop-floor reporting. Gate rejections lift their machine-readable
// lists into the error object: MFG_RELEASE_BLOCKED → error.prerequisites, MFG_CLOSE_BLOCKED →
// error.items (010 AC1 / 013 AC1).
[ApiController]
[Route("api/v1/production-orders")]
public sealed class ProductionController(IProductionOrderService production) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status, [FromQuery] string? outputSku)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        ProductionOrderStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ProductionOrderStatus>(status, ignoreCase: true, out var parsed))
                return Envelope.Fail("VALIDATION_ERROR",
                    $"status '{status}' is not a valid production order status (Draft, Planned, Released, InProcess, Completed, Closed, Cancelled).", 400);
            filter = parsed;
        }
        return Envelope.Ok<object>(production.List(filter, outputSku));
    }

    [HttpPost]
    public IResult Create(CreateProductionOrderRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.OutputSku, "OutputSku")
            ?? Guards.CheckPositive(request.DemandedQty, "DemandedQty")
            ?? Guards.Check(request.Unit, "Unit");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = production.Create(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/transitions")]
    public IResult Transition(string id, TransitionOrderRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return Envelope.Fail("FORBIDDEN", "mfg:write permission required.", 403);
        if (request.NewPlannedQty is { } qty && qty <= 0)
            return Envelope.Fail("VALIDATION_ERROR", "NewPlannedQty must be greater than 0.", 400);
        var (data, code, message) = production.Transition(u, id, request);
        return code switch
        {
            // fix round 1 (F2.1): the Completed gate lifts its mismatch list like the close gate
            "MFG_CLOSE_BLOCKED" => Envelope.From(Envelope.FailBodyItems(code, message!, (object[])data!), 409),
            null => Envelope.Ok<object>(data!),
            _ => Envelope.Fail(code, message!, ErrorStatus.StatusFor(code)),
        };
    }

    [HttpPost("{id}/releases")]
    public IResult Release(string id, [FromBody] ReleaseOrderRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:release")) return (Envelope.FailBody("FORBIDDEN", "mfg:release permission required."), 403);
        var (data, code, message) = production.Release(u, id, request?.Reason);
        return code switch
        {
            "MFG_RELEASE_BLOCKED" => (Envelope.FailBodyPrerequisites(code, message!, (object[])data!), 409),
            null => (Envelope.OkBody(data!), 200),
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });

    [HttpPost("{id}/material-issues")]
    public IResult IssueMaterial(string id, MaterialIssueRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.ComponentSku, "ComponentSku") ?? Guards.CheckPositive(request.Qty, "Qty");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = production.IssueMaterial(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/issue-returns")]
    public IResult ReturnIssue(string id, IssueReturnRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.IssueId, "IssueId") ?? Guards.CheckPositive(request.Qty, "Qty");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = production.ReturnIssue(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/completions")]
    public IResult ReportCompletion(string id, CompletionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        if (Guards.CheckPositive(request.Qty, "Qty") is { } err)
            return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = production.ReportCompletion(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/scrap-reports")]
    public IResult ReportScrap(string id, ScrapRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.ItemSku, "ItemSku")
            ?? Guards.CheckPositive(request.Qty, "Qty")
            ?? Guards.Check(request.ReasonCode, "ReasonCode"); // 067 AC1: no scrap without a reason code
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:write")) return (Envelope.FailBody("FORBIDDEN", "mfg:write permission required."), 403);
        var (data, code, message) = production.ReportScrap(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpGet("{id}/close-check")]
    public IResult CloseCheck(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        var (data, code, message) = production.CloseCheck(id);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }

    [HttpPost("{id}/closures")]
    public IResult Close(string id, [FromBody] CloseOrderRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:close")) return (Envelope.FailBody("FORBIDDEN", "mfg:close permission required."), 403);
        var (data, code, message) = production.Close(u, id, request?.ResidualAcknowledgment);
        return code switch
        {
            "MFG_CLOSE_BLOCKED" => (Envelope.FailBodyItems(code, message!, (object[])data!), 409),
            null => (Envelope.OkBody(data!), 200),
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });

    [HttpPost("{id}/reopenings")]
    public IResult Reopen(string id, [FromBody] ReopenOrderRequest? request)
    {
        // 013 AC3: a reopen without a reason never reaches the service
        if (string.IsNullOrWhiteSpace(request?.Reason))
            return Envelope.Fail("VALIDATION_ERROR", "A reopen reason is required.", 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:reopen")) return Envelope.Fail("FORBIDDEN", "mfg:reopen permission required.", 403);
        var (data, code, message) = production.Reopen(u, id, request.Reason);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }
}
