using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/requisitions")]
public sealed class RequisitionsController(IRequisitionService requisitions) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status) =>
        Envelope.Ok<object>(requisitions.List(ParseStatus(status)));

    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var detail = requisitions.Detail(id);
        return detail is null ? Envelope.Fail("REQUISITION_NOT_FOUND", "Requisition not found.", 404) : Envelope.Ok<object>(detail);
    }

    [HttpPost]
    public IResult Create(CreateRequisitionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("requisitions:write")) return (Envelope.FailBody("FORBIDDEN", "requisitions:write permission required."), 403);
        var (requisition, code, error) = requisitions.Create(u, request.Purpose, request.Currency, request.Lines);
        return code is null ? (Envelope.OkBody(Proj(requisition!)), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/submission")]
    public IResult Submit(string id, SubmitRequisitionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("requisitions:write")) return (Envelope.FailBody("FORBIDDEN", "requisitions:write permission required."), 403);
        var (requisition, code, error) = requisitions.Submit(u, id, request.OverrideReason);
        return code is null ? (Envelope.OkBody(Proj(requisition!)), 200) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/decisions")]
    public IResult Decide(string id, RequisitionDecisionRequest request)
    {
        // no controller-level approve gate: delegated deciders (015) arrive without
        // requisitions:approve — the service resolves authority, delegation scope and maker-checker
        var u = CurrentUser.From(HttpContext);
        var (requisition, code, error) = requisitions.Decide(u, id, request.Decision, request.Version, request.Reason);
        return code is null ? Envelope.Ok<object>(Proj(requisition!)) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/exceptions")]
    public IResult FlagException(string id, FlagExceptionRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("requisitions:write")) return Envelope.Fail("FORBIDDEN", "requisitions:write permission required.", 403);
        var (requisition, code, error) = requisitions.FlagException(u, id, request.ReasonCode, request.Narrative, request.EvidenceRef);
        return code is null ? Envelope.Ok<object>(Proj(requisition!)) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }

    private static RequisitionStatus? ParseStatus(string? status) =>
        Enum.TryParse<RequisitionStatus>(status, ignoreCase: true, out var s) ? s : null;

    private static object Proj(Requisition r) => new
    {
        r.Id, r.Purpose, r.Status, r.Version, r.Currency, r.CreatedBy, r.CreatedAt,
        r.SubmittedBy, r.SubmittedAt, r.RuleId, r.RuleVersion, r.ApproverRole,
        r.ExceptionCode, r.ExceptionNarrative, r.ExceptionEvidenceRef, r.ExceptionFlaggedAt,
        lines = r.Lines.Select(l => new
        {
            l.Id, l.CategoryId, l.Description, l.Qty, l.UnitPrice, l.Unit, l.NeedBy, l.BaseQty, l.BaseUnit,
        }).ToList(),
        total = r.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
    };
}

[ApiController]
[Route("api/v1/approval-delegations")]
public sealed class ApprovalDelegationsController(IRequisitionService requisitions) : ControllerBase
{
    [HttpPost]
    public IResult Create(CreateDelegationRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("requisitions:approve")) return Envelope.Fail("FORBIDDEN", "requisitions:approve permission required to delegate it.", 403);
        var (delegation, code, error) = requisitions.CreateDelegation(u, request.DelegateId, request.From, request.To, request.MaxAmount, request.CategoryId, request.Reason);
        return code is null
            ? Envelope.Ok<object>(delegation!, 201)
            : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }
}
