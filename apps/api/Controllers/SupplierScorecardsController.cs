using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P5 scorecards (062) — generation, supplier response, corrective actions with reviewer closure.
[ApiController]
[Route("api/v1/supplier-scorecards")]
public sealed class SupplierScorecardsController(ISupplierScorecardsService scorecards) : ControllerBase
{
    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var detail = scorecards.Detail(id);
        return detail is null ? Envelope.Fail("SCORECARD_NOT_FOUND", "Scorecard not found.", 404) : Envelope.Ok<object>(detail);
    }

    [HttpPost]
    public IResult Create(CreateScorecardRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (scorecard, code, error) = scorecards.Create(u, request.SupplierId, request.PeriodFrom, request.PeriodTo);
        return code is null ? (Envelope.OkBody(scorecard!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/supplier-responses")]
    public IResult AddSupplierResponse(string id, AddScorecardSupplierResponseRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return Envelope.Fail("FORBIDDEN", "suppliers:write permission required.", 403);
        var (scorecard, code, error) = scorecards.AddSupplierResponse(u, id, request.Response);
        return code is null ? Envelope.Ok<object>(scorecard!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }
}

// I5: the qualification page reads a supplier's scorecards by supplier id (ForSupplier) —
// same suppliers:read gate as GET {id}/gates on SuppliersController; repo query is tenant-scoped
[ApiController]
[Route("api/v1/suppliers")]
public sealed class SupplierScorecardListController(ISupplierScorecardsService scorecards) : ControllerBase
{
    [HttpGet("{id}/scorecards")]
    public IResult ForSupplier(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:read")) return Envelope.Fail("FORBIDDEN", "suppliers:read permission required.", 403);
        return Envelope.Ok<object>(scorecards.ForSupplier(id));
    }
}

[ApiController]
[Route("api/v1/scorecards/{scorecardId}/corrective-actions")]
public sealed class CorrectiveActionsController(ISupplierScorecardsService scorecards) : ControllerBase
{
    [HttpPost]
    public IResult Add(string scorecardId, AddCorrectiveActionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (action, code, error) = scorecards.AddAction(u, scorecardId, request.Finding, request.Action);
        return code is null ? (Envelope.OkBody(action!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/corrective-actions")]
public sealed class CorrectiveActionClosuresController(ISupplierScorecardsService scorecards) : ControllerBase
{
    [HttpPost("{id}/closures")]
    public IResult Close(string id, CloseCorrectiveActionRequest request)
    {
        var (action, code, error) = scorecards.CloseAction(CurrentUser.From(HttpContext), id, request.Outcome);
        return code is null ? Envelope.Ok<object>(action!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }
}
