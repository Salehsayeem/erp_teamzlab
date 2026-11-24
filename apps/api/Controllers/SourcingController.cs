using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// T6 sourcing events (ERP-PROCUREMENT-022..032). DTOs are validated Guards-style BEFORE the
// service runs; creates are Idem.Wrap'd per the brief; approval-like acts (bid opening, award
// approval) do their maker-checker + permission checks in the service, PO-style.
[ApiController]
[Route("api/v1/sourcing-events")]
public sealed class SourcingEventsController(ISourcingService sourcing) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status, [FromQuery] string? eventType)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:read")) return Envelope.Fail("FORBIDDEN", "sourcing:read permission required.", 403);
        var (okStatus, statusValue) = Query.Enum<SourcingEventStatus>(status);
        var (okType, typeValue) = Query.Enum<SourcingEventType>(eventType);
        return okStatus && okType
            ? Envelope.Ok<object>(sourcing.List(u, statusValue, typeValue))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown event filter '{status ?? eventType}'.", 400);
    }

    // composition read for the event screen — sealedness (FIX 1) is decided in the service
    [HttpGet("{id}")]
    public IResult Get(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:read")) return Envelope.Fail("FORBIDDEN", "sourcing:read permission required.", 403);
        var (data, code, message) = sourcing.GetEvent(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Create(CreateEventRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Title, "Title") ?? Guards.Check(request.TimeZoneName, "TimeZoneName")
            ?? Guards.Check(request.Currency, "Currency")
            ?? (request.OpensAt >= request.ClosesAt ? "OpensAt must be earlier than ClosesAt." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return (Envelope.FailBody("FORBIDDEN", "sourcing:write permission required."), 403);
        var (data, code, message) = sourcing.Create(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/lots")]
    public IResult AddLot(string id, AddLotRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.LotNo, "LotNo") ?? Guards.Check(request.Description, "Description")
            ?? (request.TargetQty is { } qty && qty <= 0 ? "TargetQty must be greater than 0 when present." : null)
            ?? (request.CeilingValue is { } ceiling && ceiling < 0 ? "CeilingValue must be 0 or greater when present." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return (Envelope.FailBody("FORBIDDEN", "sourcing:write permission required."), 403);
        var (data, code, message) = sourcing.AddLot(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/criteria")]
    public IResult AddCriterion(string id, AddCriterionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code") ?? Guards.Check(request.Name, "Name")
            ?? (request.WeightPct <= 0 ? "WeightPct must be greater than 0." : null)
            ?? (request.Kind == CriterionKind.Scored && request.ScaleMax <= 0 ? "Scored criteria need a ScaleMax greater than 0." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return (Envelope.FailBody("FORBIDDEN", "sourcing:write permission required."), 403);
        var (data, code, message) = sourcing.AddCriterion(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/publications")]
    public IResult Publish(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return Envelope.Fail("FORBIDDEN", "sourcing:write permission required.", 403);
        var (data, code, message) = sourcing.Publish(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/invitations")]
    public IResult Invite(string id, InviteSuppliersRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = request.SupplierIds is null || request.SupplierIds.Count == 0 ? "At least one supplier id is required."
            : request.SupplierIds.Any(s => string.IsNullOrWhiteSpace(s)) ? "Every supplier id must be non-empty." : null;
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return (Envelope.FailBody("FORBIDDEN", "sourcing:write permission required."), 403);
        var (data, code, message) = sourcing.Invite(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/questions")]
    public IResult AnswerQa(string id, AnswerQaRequest request)
    {
        var err = Guards.Check(request.Question, "Question") ?? Guards.Check(request.Answer, "Answer")
            ?? (request.Scope == QaScope.SupplierSpecific && string.IsNullOrWhiteSpace(request.SupplierId)
                ? "Supplier-specific answers need a SupplierId." : null);
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return Envelope.Fail("FORBIDDEN", "sourcing:write permission required.", 403);
        var (data, code, message) = sourcing.AnswerQa(u, id, request);
        return code is null ? Envelope.Ok<object>(data!, 201) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/bids")]
    public IResult SubmitBid(string id, SubmitBidRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.SupplierId, "SupplierId");
        if (err is null && (request.Lines is null || request.Lines.Count == 0))
            err = "At least one bid line is required.";
        if (err is null && request.Lines!.Any(l => l.Qty <= 0))
            err = "Line qty must be greater than 0.";
        if (err is null && request.Lines!.Any(l => l.UnitPrice < 0))
            err = "Line unit price must be 0 or greater.";
        if (err is null && request.Lines!.Any(l => l.DiscountPct < 0 || l.DiscountPct > 100))
            err = "Line discount must be between 0 and 100.";
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return (Envelope.FailBody("FORBIDDEN", "sourcing:write permission required."), 403);
        var (data, code, message) = sourcing.SubmitBid(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/bids")]
    public IResult ListBids(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:read")) return Envelope.Fail("FORBIDDEN", "sourcing:read permission required.", 403);
        var (data, code, message) = sourcing.ListBids(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/bid-openings")]
    public IResult OpenBids(string id)
    {
        var (data, code, message) = sourcing.OpenBids(CurrentUser.From(HttpContext), id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/evaluator-assignments")]
    public IResult AssignEvaluators(string id, AssignEvaluatorsRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = request.UserIds is null || request.UserIds.Count == 0 ? "At least one evaluator user id is required."
            : request.UserIds.Any(x => string.IsNullOrWhiteSpace(x)) ? "Every evaluator user id must be non-empty." : null;
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return (Envelope.FailBody("FORBIDDEN", "sourcing:write permission required."), 403);
        var (data, code, message) = sourcing.AssignEvaluators(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/scores")]
    public IResult SubmitScore(string id, SubmitScoreRequest request)
    {
        var err = Guards.Check(request.BidId, "BidId") ?? Guards.Check(request.CriterionId, "CriterionId");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = sourcing.SubmitScore(u, id, request);
        return code is null ? Envelope.Ok<object>(data!, 201) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpGet("{id}/score-summary")]
    public IResult ScoreSummary(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:read")) return Envelope.Fail("FORBIDDEN", "sourcing:read permission required.", 403);
        var (data, code, message) = sourcing.ScoreSummary(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}

// T6 awards (ERP-PROCUREMENT-033/034): recommendation + maker-checked approval
[ApiController]
[Route("api/v1/awards")]
public sealed class AwardsController(ISourcingService sourcing) : ControllerBase
{
    // award summaries for one event — embedded prices follow the sealed-phase rule (service-side)
    [HttpGet]
    public IResult List([FromQuery] string? eventId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:read")) return Envelope.Fail("FORBIDDEN", "sourcing:read permission required.", 403);
        if (string.IsNullOrWhiteSpace(eventId)) return Envelope.Fail("VALIDATION_ERROR", "eventId query parameter is required.", 400);
        var (data, code, message) = sourcing.AwardsFor(u, eventId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Recommend(RecommendAwardRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EventId, "EventId") ?? Guards.Check(request.LotId, "LotId")
            ?? Guards.Check(request.SupplierId, "SupplierId") ?? Guards.Check(request.Recommendation, "Recommendation")
            ?? (request.Qty <= 0 ? "Qty must be greater than 0." : null)
            ?? (request.UnitPrice < 0 ? "UnitPrice must be 0 or greater." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("sourcing:write")) return (Envelope.FailBody("FORBIDDEN", "sourcing:write permission required."), 403);
        var (data, code, message) = sourcing.RecommendAward(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, ApproveAwardRequest request)
    {
        var (data, code, message) = sourcing.ApproveAward(CurrentUser.From(HttpContext), id, request);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
