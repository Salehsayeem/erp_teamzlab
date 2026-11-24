using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Opportunities (ERP-SALES-015/016/017): fixed stage map Open → Won/Lost (R-T7i), line sums,
// closed-won needs an order link, lost needs a reason.
[ApiController]
[Route("api/v1/opportunities")]
public sealed class OpportunitiesController(IOpportunityService opportunities) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status, [FromQuery] string? ownerId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("opps:read")) return Envelope.Fail("FORBIDDEN", "opps:read permission required.", 403);
        OpportunityStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<OpportunityStatus>(status, ignoreCase: true, out var parsed))
                return Envelope.Fail("VALIDATION_ERROR", $"status '{status}' is not a valid opportunity status (Open, Won, Lost).", 400);
            statusFilter = parsed;
        }
        return Envelope.Ok<object>(opportunities.List(statusFilter, ownerId));
    }

    [HttpPost]
    public IResult Create(CreateOpportunityRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Title, "Title") ?? Guards.Check(request.Currency, "Currency", 10)
            ?? Guards.Check(request.OwnerId, "OwnerId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("opps:write")) return (Envelope.FailBody("FORBIDDEN", "opps:write permission required."), 403);
        var (opportunity, code, message) = opportunities.Create(u, request);
        return code is null ? (Envelope.OkBody(opportunity!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/lines")]
    public IResult AddLine(string id, AddOpportunityLineRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.ItemSku, "ItemSku") ?? Guards.CheckPositive(request.Qty, "Qty")
            ?? (request.TargetPrice < 0 ? "TargetPrice must be >= 0." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("opps:write")) return (Envelope.FailBody("FORBIDDEN", "opps:write permission required."), 403);
        var (opportunity, code, message) = opportunities.AddLine(u, id, request);
        return code is null ? (Envelope.OkBody(opportunity!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/transitions")]
    public IResult Transition(string id, TransitionOpportunityRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("opps:write")) return Envelope.Fail("FORBIDDEN", "opps:write permission required.", 403);
        // 019 AC2: Lost without a loss reason never reaches the service (400 like case resolutions)
        if (request.Status == OpportunityStatus.Lost && string.IsNullOrWhiteSpace(request.LossReason))
            return Envelope.Fail("VALIDATION_ERROR", "LossReason is required when transitioning to Lost.", 400);
        var (opportunity, code, message) = opportunities.Transition(u, id, request);
        return code is null ? Envelope.Ok<object>(opportunity!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
