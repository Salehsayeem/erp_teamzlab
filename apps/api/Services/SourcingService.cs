using System.Security.Cryptography;
using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// T6 sourcing spine (ERP-PROCUREMENT-022..034 + 044/047, rulings R-T6a..i). Bids are recorded by
// buyers (no portal), sealed until an approved opening stamps who/when; evaluation is independent
// (assigned evaluators, own scores only) with a deterministic normalized trace; awards are
// maker-checked with a NOW eligibility re-check and per-lot ceiling allocation. No journals are
// posted anywhere in this service (R-T6d/R-T6f — commitment records only).
public sealed class SourcingService(
    ISourcingRepository sourcing, ISupplierRepository suppliers, IIdentityRepository identities,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : ISourcingService
{
    private const string FormulaVersion = "1"; // 032 AC1 — bumped only when the normalization changes

    public object List(CurrentUser user, SourcingEventStatus? status, SourcingEventType? eventType) =>
        CanSeeEverything(user)
            ? sourcing.AllEvents(status, eventType).Select(ProjEvent).ToList()
            : sourcing.EventsForEvaluator(user.Id, status, eventType).Select(ProjEvent).ToList();

    // Composition read for the event screen (page reload): header + publication state + lots +
    // frozen criteria + invitations (decorated with the supplier's current gates) + Q&A + bids +
    // award summary. Visibility mirrors bids/score-summary; sealedness mirrors ListBids EXACTLY
    // (FIX 1): without sourcing:approve, bid prices AND contentDigest stay hidden until an
    // approved opening stamps who/when — the same rule strips the award unitPrice.
    public (object? data, string? code, string? message) GetEvent(CurrentUser user, string eventId)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (!CanSeeEverything(user) && sourcing.FindAssignment(eventId, user.Id) is null)
            return (null, "FORBIDDEN", "Only assigned evaluators can view this event.");

        var isApprover = user.Has("sourcing:approve");
        var bids = sourcing.BidsForEvent(eventId)
            .Select(b =>
            {
                var unsealed = isApprover || b.OpenedAt is not null;
                return ProjBid(b, sourcing.LinesForBid(b.Id).Select(l => ProjLine(l, unsealed)).ToList(), unsealed);
            })
            .ToList();
        var pricesUnsealed = isApprover || sourcing.BidsForEvent(eventId).Any(b => b.OpenedAt is not null);
        return (new
        {
            @event = ProjEvent(evt),
            publication = ProjPublication(evt),
            lots = sourcing.LotsForEvent(eventId).Select(ProjLot).ToList(),
            criteria = sourcing.CriteriaForEvent(eventId).Select(ProjCriterion).ToList(),
            invitations = sourcing.InvitationsForEvent(eventId).Select(ProjInvitationDecorated).ToList(),
            qa = sourcing.QaForEvent(eventId).Select(ProjQa).ToList(),
            bids,
            awards = sourcing.AwardsForEvent(eventId).Select(a => ProjAwardSummary(a, pricesUnsealed)).ToList(),
        }, null, null);
    }

    // GET /api/v1/awards?eventId= — the event's award summary; embedded prices follow the same
    // sealed-phase rule as the event detail (hidden until the approver view or an opening)
    public (object? data, string? code, string? message) AwardsFor(CurrentUser user, string eventId)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (!CanSeeEverything(user) && sourcing.FindAssignment(eventId, user.Id) is null)
            return (null, "FORBIDDEN", "Only assigned evaluators can view the awards of this event.");
        var reveal = user.Has("sourcing:approve") || sourcing.BidsForEvent(eventId).Any(b => b.OpenedAt is not null);
        return (sourcing.AwardsForEvent(eventId).Select(a => ProjAwardSummary(a, reveal)).ToList(), null, null);
    }

    public (object? data, string? code, string? message) Create(CurrentUser user, CreateEventRequest request)
    {
        var lot = new SourcingEvent(numbers.Next("EV"), ec.TenantId, ec.EntityId, request.Title,
            request.EventType, SourcingEventStatus.Draft, ToUtc(request.OpensAt), ToUtc(request.ClosesAt),
            request.TimeZoneName, request.Currency, Version: 1, user.Id, DateTimeOffset.UtcNow);
        sourcing.AddEvent(lot);
        audit.Log(user, "create", "sourcing_event", lot.Id,
            $"Sourcing event '{request.Title}' ({request.EventType}) opens {request.OpensAt:u} closes {request.ClosesAt:u} {request.TimeZoneName}");
        sourcing.Save();
        return (ProjEvent(lot), null, null);
    }

    public (object? data, string? code, string? message) AddLot(CurrentUser user, string eventId, AddLotRequest request)
    {
        var (evt, code, message) = LoadEditableEvent(eventId);
        if (code is not null) return (null, code, message);
        var lot = new EventLot(numbers.Next("LOT"), ec.TenantId, ec.EntityId, eventId, request.LotNo,
            request.Description, request.ItemSku, request.TargetQty, request.CeilingValue);
        sourcing.AddLot(lot);
        audit.Log(user, "add", "event_lot", lot.Id, $"Lot {request.LotNo} added to {eventId}");
        sourcing.Save();
        return (ProjLot(lot), null, null);
    }

    public (object? data, string? code, string? message) AddCriterion(CurrentUser user, string eventId, AddCriterionRequest request)
    {
        var (evt, code, message) = LoadEditableEvent(eventId);
        if (code is not null) return (null, code, message);
        var criterion = new EvalCriterion(numbers.Next("QA"), ec.TenantId, ec.EntityId, eventId,
            request.Code, request.Name, request.Kind, request.WeightPct, request.ScaleMax);
        sourcing.AddCriterion(criterion);
        audit.Log(user, "add", "eval_criterion", criterion.Id,
            $"Criterion {request.Code} ({request.Kind}, weight {request.WeightPct}%) added to {eventId}");
        sourcing.Save();
        return (ProjCriterion(criterion), null, null);
    }

    // 022 AC3 + 030 AC1: publication freezes lots and criteria — needs ≥1 invitation and
    // criteria whose weights sum to exactly 100
    public (object? data, string? code, string? message) Publish(CurrentUser user, string eventId)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (evt.Status is not SourcingEventStatus.Draft)
            return (null, "EVENT_STATE_ERROR", $"Only draft events can be published (current: {evt.Status}).");
        if (sourcing.InvitationsForEvent(eventId).Count == 0)
            return (null, "EVENT_PUBLISH_BLOCKED", "Publish needs at least one invited supplier.");
        var criteria = sourcing.CriteriaForEvent(eventId);
        if (criteria.Count == 0)
            return (null, "EVENT_PUBLISH_BLOCKED", "Publish needs at least one evaluation criterion.");
        var weight = criteria.Sum(c => c.WeightPct);
        if (weight != 100m)
            return (null, "EVENT_PUBLISH_BLOCKED", $"Criteria weights must sum to 100% (currently {weight:0.##}%).");

        sourcing.SetEventStatus(evt, SourcingEventStatus.Published);
        audit.Log(user, "publish", "sourcing_event", eventId,
            $"Event '{evt.Title}' published; {criteria.Count} criterion/criteria frozen (weights {weight:0.##}%), {sourcing.InvitationsForEvent(eventId).Count} invitation(s) sent");
        audit.Emit(user, "procurement.event.published", new { eventId, version = evt.Version, criteriaCount = criteria.Count });
        sourcing.Save();
        return (ProjEvent(evt), null, null);
    }

    public (object? data, string? code, string? message) Invite(CurrentUser user, string eventId, InviteSuppliersRequest request)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (evt.Status is not (SourcingEventStatus.Draft or SourcingEventStatus.Published))
            return (null, "EVENT_STATE_ERROR", $"Event {eventId} no longer accepts invitations (current: {evt.Status}).");

        // FIX 7: validate EVERY supplier before tracking anything — a mid-list failure must not
        // flush earlier items (the idempotency store shares this scoped context)
        var prepared = new List<(string SupplierId, string? Warnings)>();
        foreach (var supplierId in request.SupplierIds)
        {
            if (suppliers.Find(supplierId) is null)
                return (null, "SUPPLIER_NOT_FOUND", $"Supplier {supplierId} not found.");
            if (sourcing.FindInvitation(eventId, supplierId) is not null)
                return (null, "INVITATION_EXISTS", $"Supplier {supplierId} is already invited to {eventId}.");
            var (eligible, reason, warnings) = EvaluateEligibility(supplierId);
            if (!eligible)
                return (null, "SUPPLIER_INELIGIBLE", $"Supplier {supplierId} failed the eligibility gate: {reason}.");
            prepared.Add((supplierId, warnings));
        }

        var invited = new List<object>();
        foreach (var (supplierId, warnings) in prepared)
        {
            var invitation = new EventInvitation($"{eventId}:{supplierId}", ec.TenantId, ec.EntityId, eventId,
                supplierId, InvitationStatus.Sent, "manual", DateTimeOffset.UtcNow, null, warnings);
            sourcing.AddInvitation(invitation);
            audit.Log(user, "invite", "event_invitation", invitation.Id,
                $"Supplier {supplierId} invited to {eventId}{(warnings is null ? "" : $" (warning gates on record: {warnings})")}");
            invited.Add(ProjInvitation(invitation));
        }
        sourcing.Save();
        return (invited, null, null);
    }

    // 027: buyer logs Q&A; publishing an event-wide answer bumps the event version (AC3)
    public (object? data, string? code, string? message) AnswerQa(CurrentUser user, string eventId, AnswerQaRequest request)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (evt.Status is not (SourcingEventStatus.Draft or SourcingEventStatus.Published))
            return (null, "EVENT_STATE_ERROR", $"Event {eventId} no longer accepts Q&A (current: {evt.Status}).");
        if (request.SupplierId is { } sid && suppliers.Find(sid) is null)
            return (null, "SUPPLIER_NOT_FOUND", $"Supplier {sid} not found.");

        var qa = new EventQa(numbers.Next("QA"), ec.TenantId, ec.EntityId, eventId, request.SupplierId,
            request.Question, request.Answer, request.Scope, request.Publish,
            request.Publish ? DateTimeOffset.UtcNow : null, user.Id);
        sourcing.AddQa(qa);
        var bumped = false;
        if (request.Publish && request.Scope == QaScope.EventWide)
        {
            sourcing.SetEventVersion(evt, evt.Version + 1);
            bumped = true;
        }
        audit.Log(user, "answer", "event_qa", qa.Id,
            $"Q&A logged on {eventId} ({request.Scope}{(request.SupplierId is null ? "" : $", {request.SupplierId}")}), {(request.Publish ? "published" : "draft")}"
            + (bumped ? $"; event version bumped to {evt.Version}" : ""));
        sourcing.Save();
        return (ProjQa(qa), null, null);
    }

    // 028: buyer-recorded bid (R-T6a). Late bids rejected; the receipt carries the bid id (the
    // receipt id), event version snapshot, instant and a SHA-256 content digest. Same
    // Idempotency-Key replays the stored receipt without creating a second bid (controller Wrap).
    public (object? data, string? code, string? message) SubmitBid(CurrentUser user, string eventId, SubmitBidRequest request)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (evt.Status != SourcingEventStatus.Published)
            return (null, "EVENT_NOT_PUBLISHED", $"Bids are only accepted on published events (current: {evt.Status}).");
        if (DateTimeOffset.UtcNow > evt.ClosesAt)
            return (null, "BID_CLOSED", $"The bid window closed at {evt.ClosesAt:u} ({evt.TimeZoneName}).");
        if (suppliers.Find(request.SupplierId) is null)
            return (null, "SUPPLIER_NOT_FOUND", $"Supplier {request.SupplierId} not found.");

        var digest = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(request, Idem.JsonOptions))));
        // validate every line BEFORE touching the change tracker — the idempotency store shares
        // this DbContext, so an error return must leave nothing tracked to commit (see Reserve/Complete)
        var lots = new List<EventLot>();
        foreach (var line in request.Lines)
        {
            var lot = sourcing.FindLot(eventId, line.LotId);
            if (lot is null) return (null, "LOT_NOT_FOUND", $"Lot {line.LotId} does not belong to event {eventId}.");
            if (lots.Any(l => l.Id == lot.Id))
                return (null, "VALIDATION_ERROR", $"Duplicate line for lot {line.LotId} is not allowed in one bid.");
            lots.Add(lot);
        }
        var bid = new Bid(numbers.Next("BID"), ec.TenantId, ec.EntityId, eventId, request.SupplierId,
            evt.Version, BidStatus.Submitted, digest, DateTimeOffset.UtcNow, null, null);
        sourcing.AddBid(bid);
        foreach (var line in request.Lines)
            sourcing.AddBidLine(new BidLine($"{bid.Id}:{line.LotId}", ec.TenantId, ec.EntityId, bid.Id,
                line.LotId, line.Qty, line.UnitPrice, line.DiscountPct));
        audit.Log(user, "record", "bid", bid.Id,
            $"Bid {bid.Id} recorded for supplier {request.SupplierId} on {eventId} (event v{evt.Version})"); // no digest fragment: audit must not leak sealed content (FIX 1)
        sourcing.Save();
        return (new { bidId = bid.Id, eventVersion = evt.Version, submittedAt = bid.SubmittedAt, contentDigest = digest }, null, null);
    }

    // 025 trimmed (R-T6b): opening is a checker act — ClosesAt must be past, opener ≠ event
    // creator — stamping who/when on every submitted bid and unsealing prices for everyone
    public (object? data, string? code, string? message) OpenBids(CurrentUser user, string eventId)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (DateTimeOffset.UtcNow <= evt.ClosesAt)
            return (null, "BIDS_SEALED", $"Bids stay sealed until the window closes at {evt.ClosesAt:u} ({evt.TimeZoneName}).");
        if (evt.CreatedBy == user.Id)
            return (null, "BID_OPENING_APPROVAL_ERROR", "Maker-checker: the event creator cannot run the bid opening.");
        if (!user.Has("sourcing:approve"))
            return (null, "FORBIDDEN", "sourcing:approve permission required.");
        var submitted = sourcing.BidsForEvent(eventId).Where(b => b.Status == BidStatus.Submitted).ToList();
        if (submitted.Count == 0 && sourcing.BidsForEvent(eventId).Any(b => b.OpenedAt is not null))
            return (null, "BIDS_ALREADY_OPEN", "Bids for this event were already opened.");

        var openedAt = DateTimeOffset.UtcNow;
        foreach (var bid in submitted) sourcing.SetBidOpened(bid, user.Id, openedAt);
        audit.Log(user, "open", "bid", eventId,
            $"Bid opening recorded by {user.Email}: {submitted.Count} bid(s) unsealed at {openedAt:u}");
        audit.Emit(user, "procurement.bids.opened", new { eventId, openedBy = user.Id, openedAt, bidCount = submitted.Count });
        sourcing.Save();
        return (new { eventId, openedBy = user.Id, openedAt, bidCount = submitted.Count }, null, null);
    }

    // 025 AC1 trimmed: sealed prices are stripped for callers without sourcing:approve
    public (object? data, string? code, string? message) ListBids(CurrentUser user, string eventId)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (!CanSeeEverything(user) && sourcing.FindAssignment(eventId, user.Id) is null)
            return (null, "FORBIDDEN", "Only assigned evaluators can view bids of this event.");
        var isApprover = user.Has("sourcing:approve");
        var bids = sourcing.BidsForEvent(eventId)
            .Select(b =>
            {
                var unsealed = isApprover || b.OpenedAt is not null;
                // FIX 1: prices AND the content digest stay stripped while sealed — a SHA-256 of a
                // fixed-shape payload plus visible qty lets an evaluator grid-search price×discount
                return ProjBid(b, sourcing.LinesForBid(b.Id).Select(l => ProjLine(l, unsealed)).ToList(), unsealed);
            })
            .ToList();
        return (bids, null, null);
    }

    public (object? data, string? code, string? message) AssignEvaluators(CurrentUser user, string eventId, AssignEvaluatorsRequest request)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        // FIX 7: validate every user first — mid-list failures persist nothing
        foreach (var userId in request.UserIds)
        {
            if (identities.FindActiveUserById(userId) is null)
                return (null, "USER_NOT_FOUND", $"User {userId} not found or inactive.");
            if (userId == evt.CreatedBy)
                return (null, "EVALUATOR_CREATOR_CONFLICT", $"User {userId} created event {eventId} and cannot evaluate it (016 AC2).");
        }

        var assigned = new List<object>();
        foreach (var userId in request.UserIds)
        {
            var assignment = sourcing.FindAssignment(eventId, userId);
            if (assignment is null)
            {
                assignment = new EvaluatorAssignment($"{eventId}:{userId}", ec.TenantId, ec.EntityId, eventId, userId);
                sourcing.AddAssignment(assignment);
                audit.Log(user, "assign", "evaluator_assignment", assignment.Id, $"Evaluator {userId} assigned to {eventId}");
            }
            assigned.Add(new { assignment.Id, assignment.EventId, assignment.UserId });
        }
        sourcing.Save();
        return (assigned, null, null);
    }

    // 031 AC1: must be an assigned evaluator; scores stamped with the caller — own scores only
    public (object? data, string? code, string? message) SubmitScore(CurrentUser user, string eventId, SubmitScoreRequest request)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (!user.Has("sourcing:evaluate"))
            return (null, "FORBIDDEN", "sourcing:evaluate permission required.");
        if (sourcing.FindAssignment(eventId, user.Id) is null)
            return (null, "FORBIDDEN", $"Only assigned evaluators can score bids on {eventId}.");
        var bid = sourcing.FindBid(eventId, request.BidId);
        if (bid is null) return (null, "BID_NOT_FOUND", $"Bid {request.BidId} not found on {eventId}.");
        var criterion = sourcing.FindCriterion(eventId, request.CriterionId);
        if (criterion is null) return (null, "CRITERION_NOT_FOUND", $"Criterion {request.CriterionId} not found on {eventId}.");
        if (criterion.Kind == CriterionKind.Scored)
        {
            if (request.RawScore is null || request.PassFail is not null)
                return (null, "VALIDATION_ERROR", $"Criterion {criterion.Code} is scored: send rawScore within 0..{criterion.ScaleMax} and no passFail.");
            if (request.RawScore < 0 || request.RawScore > criterion.ScaleMax)
                return (null, "VALIDATION_ERROR", $"Raw score must be within 0..{criterion.ScaleMax} for {criterion.Code}.");
        }
        else
        {
            if (request.PassFail is null || request.RawScore is not null)
                return (null, "VALIDATION_ERROR", $"Criterion {criterion.Code} is pass/fail: send passFail and no rawScore.");
        }

        var score = sourcing.FindScore(eventId, request.BidId, request.CriterionId, user.Id);
        if (score is null)
        {
            score = new ScoreCard($"{request.BidId}:{request.CriterionId}:{user.Id}", ec.TenantId, ec.EntityId,
                eventId, request.BidId, request.CriterionId, user.Id, request.RawScore, request.PassFail,
                request.Comment, evt.Version, DateTimeOffset.UtcNow);
            sourcing.AddScore(score);
        }
        else sourcing.SetScore(score, request.RawScore, request.PassFail, request.Comment, evt.Version);
        audit.Log(user, "score", "score_card", score.Id,
            $"Evaluator {user.Id} scored {request.BidId} on {criterion.Code} (criteria v{evt.Version})");
        sourcing.Save();
        return (ProjScore(score, criterion.Code), null, null);
    }

    // 032: deterministic normalized trace per bid × criterion — raw, normalized, weight, rounded
    // contribution, total, rank; missing inputs flagged never coerced (AC3); a failed pass/fail
    // criterion flags the bid disqualified (same formula + version for identical inputs)
    public (object? data, string? code, string? message) ScoreSummary(CurrentUser user, string eventId)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (!CanSeeEverything(user) && sourcing.FindAssignment(eventId, user.Id) is null)
            return (null, "FORBIDDEN", "Only assigned evaluators can view the score summary of this event.");

        var criteria = sourcing.CriteriaForEvent(eventId);
        var scores = sourcing.ScoresForEvent(eventId);
        // FIX 2: evaluators see only their OWN trace rows before any release stage (031 AC1) —
        // other evaluators' ids, comments and aggregate totals/ranks leak their input. Buyers and
        // sourcing:approve callers get the full trace.
        var fullTrace = CanSeeEverything(user);
        var rows = sourcing.BidsForEvent(eventId).Select(bid =>
        {
            var trace = criteria.Select(c =>
            {
                var score = scores.FirstOrDefault(s => s.BidId == bid.Id && s.CriterionId == c.Id);
                var normalized = c.Kind == CriterionKind.Scored && score?.RawScore is { } raw && c.ScaleMax > 0
                    ? decimal.Round(raw / c.ScaleMax, 4, MidpointRounding.AwayFromZero)
                    : (decimal?)null;
                var contribution = normalized is { } n ? decimal.Round(n * c.WeightPct, 2, MidpointRounding.AwayFromZero) : 0m;
                return new
                {
                    criterionId = c.Id,
                    criterionCode = c.Code,
                    kind = c.Kind,
                    weightPct = c.WeightPct,
                    rawScore = score?.RawScore,
                    passFail = score?.PassFail,
                    normalized,
                    contribution,
                    missing = score is null,
                    score?.EvaluatorUserId,
                    score?.Comment,
                };
            }).ToList();
            if (!fullTrace)
                return new
                {
                    bidId = bid.Id,
                    supplierId = bid.SupplierId,
                    bidStatus = bid.Status,
                    criteriaVersion = evt.Version,
                    ownRowsOnly = true,
                    disqualified = (bool?)null,
                    total = (decimal?)null,
                    rows = (IReadOnlyList<object>)trace.Where(t => t.EvaluatorUserId == user.Id).ToList(),
                };
            var disqualified = trace.Any(t => t.passFail == false);
            return new
            {
                bidId = bid.Id,
                supplierId = bid.SupplierId,
                bidStatus = bid.Status,
                criteriaVersion = evt.Version,
                ownRowsOnly = false,
                disqualified = (bool?)disqualified,
                total = (decimal?)decimal.Round(trace.Sum(t => t.contribution), 2, MidpointRounding.AwayFromZero),
                rows = (IReadOnlyList<object>)trace,
            };
        }).ToList();

        if (!fullTrace)
            return (new
            {
                eventId,
                formulaVersion = FormulaVersion,
                criteria = criteria.Select(ProjCriterion).ToList(),
                bids = rows.Select(r => new
                {
                    r.bidId, r.supplierId, r.bidStatus, r.criteriaVersion, r.ownRowsOnly, rows = r.rows,
                }).ToList(),
            }, null, null);

        var ranked = rows.OrderBy(r => r.disqualified == true ? 1 : 0)
            .ThenByDescending(r => r.total)
            .ThenBy(r => r.bidId)
            .Select((r, i) => (row: r, rank: i + 1))
            .ToList();

        var data = new
        {
            eventId,
            formulaVersion = FormulaVersion,
            criteria = criteria.Select(ProjCriterion).ToList(),
            bids = ranked.Select(x => new
            {
                x.row.bidId, x.row.supplierId, x.row.bidStatus, x.row.criteriaVersion, x.row.ownRowsOnly,
                x.row.disqualified, x.row.total, rank = x.rank, rows = x.row.rows,
            }).ToList(),
        };
        return (data, null, null);
    }

    // 033 AC1 / 034: recommendations reference a closed event's lot + a bid-owning supplier;
    // multiple recommendations per lot are allowed pre-approval; RFI events carry no award intent
    public (object? data, string? code, string? message) RecommendAward(CurrentUser user, RecommendAwardRequest request)
    {
        var evt = sourcing.FindEvent(request.EventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (evt.EventType == SourcingEventType.Rfi)
            return (null, "EVENT_NOT_AWARDABLE", "RFI events carry no award intent (022 edge).");
        if (evt.Status is not (SourcingEventStatus.Published or SourcingEventStatus.Closed or SourcingEventStatus.Awarded))
            return (null, "EVENT_STATE_ERROR", $"Event {request.EventId} is not open for awards (current: {evt.Status}).");
        if (DateTimeOffset.UtcNow <= evt.ClosesAt)
            return (null, "EVENT_STILL_OPEN", "Awards wait until the bid window has closed.");
        if (sourcing.FindLot(request.EventId, request.LotId) is null)
            return (null, "LOT_NOT_FOUND", $"Lot {request.LotId} not found on {request.EventId}.");
        if (suppliers.Find(request.SupplierId) is null)
            return (null, "SUPPLIER_NOT_FOUND", $"Supplier {request.SupplierId} not found.");
        var bid = sourcing.BidsForEvent(request.EventId).FirstOrDefault(b => b.SupplierId == request.SupplierId);
        if (bid is null)
            return (null, "BID_NOT_FOUND", $"Supplier {request.SupplierId} has no bid on {request.EventId}.");
        if (BidDisqualified(bid))
        {
            sourcing.SetBidStatus(bid, BidStatus.Disqualified);
            audit.Log(user, "disqualify", "bid", bid.Id, "Bid marked disqualified: a pass/fail criterion failed");
            sourcing.Save();
            return (null, "BID_DISQUALIFIED", $"Supplier {request.SupplierId}'s bid failed a pass/fail criterion and is disqualified.");
        }

        var award = new Award(numbers.Next("AWD"), ec.TenantId, ec.EntityId, request.EventId, request.LotId,
            request.SupplierId, request.Qty, request.UnitPrice, AwardStatus.Recommended, evt.Version,
            request.Recommendation, null, null, null, user.Id, DateTimeOffset.UtcNow);
        sourcing.AddAward(award);
        audit.Log(user, "recommend", "award", award.Id,
            $"Award recommended: {request.Qty:0.##} @ {request.UnitPrice:0.00} to {request.SupplierId} on lot {request.LotId} ({request.EventId})");
        sourcing.Save();
        return (ProjAward(award), null, null);
    }

    // 033 AC2/AC3 + 023 edge: maker-checker (approver ≠ recommender), eligibility re-checked NOW,
    // per-lot ceiling on approved quantities; Reason present = reject → event back to Closed
    // (NoAward when nothing remains awardable); outbox on approval
    public (object? data, string? code, string? message) ApproveAward(CurrentUser approver, string awardId, ApproveAwardRequest request)
    {
        var award = sourcing.FindAward(awardId);
        if (award is null) return (null, "AWARD_NOT_FOUND", "Award not found.");
        var evt = sourcing.FindEvent(award.EventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        if (evt.Status is SourcingEventStatus.NoAward or SourcingEventStatus.Cancelled)
            return (null, "EVENT_STATE_ERROR", $"Event {evt.Id} is {evt.Status}; its awards can no longer be decided.");
        if (award.Status is not AwardStatus.Recommended)
            return (null, "AWARD_STATE_ERROR", $"Only recommended awards can be decided (current: {award.Status}).");
        if (award.CreatedBy == approver.Id)
            return (null, "AWARD_APPROVAL_ERROR", "Maker-checker: the recommender cannot approve their own award.");
        if (!approver.Has("sourcing:approve"))
            return (null, "FORBIDDEN", "sourcing:approve permission required.");

        // FIX 4: serialize per sourcing event — the ceiling re-check and the commit happen inside
        // one transaction holding the event row (SELECT … FOR UPDATE), so two concurrent same-lot
        // approvals cannot both read stale approved-qty and over-award. Error returns before
        // commit dispose the transaction and roll back (nothing is written on a rejection path).
        using var tx = db.Database.BeginTransaction();
        LockSourcingEventRow(award.EventId);

        var at = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            // FIX 3: rejection IS the disposition for an ineligible supplier — the re-check runs
            // only on the approve disposition, so a checker rejecting "supplier suspended" lands
            sourcing.SetAwardRejected(award, request.Reason, approver.Id, at);
            sourcing.SetEventStatus(evt, RemainingAwardables(award.EventId) ? SourcingEventStatus.Closed : SourcingEventStatus.NoAward);
            audit.Log(approver, "reject", "award", awardId, $"Award {awardId} rejected by {approver.Email}: {request.Reason}; event {evt.Id} → {evt.Status}");
            sourcing.Save();
            tx.Commit();
            return (new { award = ProjAward(award), @event = ProjEvent(evt) }, null, null);
        }

        var (eligible, reason, _) = EvaluateEligibility(award.SupplierId);
        if (!eligible)
            return (null, "SUPPLIER_INELIGIBLE", $"Supplier {award.SupplierId} failed the eligibility re-check: {reason}.");

        var lot = sourcing.FindLot(award.EventId, award.LotId);
        var target = lot?.TargetQty;
        var approved = sourcing.ApprovedQtyForLot(award.LotId, award.Id);
        if (target is { } cap && approved + award.Qty > cap)
            return (null, "AWARD_OVER_CEILING",
                $"Lot {award.LotId} ceiling exceeded: {approved:0.##} already approved + {award.Qty:0.##} > {cap:0.##} (034 AC1).");

        sourcing.SetAwardApproved(award, approver.Id, at);
        sourcing.SetEventStatus(evt, SourcingEventStatus.Awarded);
        audit.Log(approver, "approve", "award", awardId,
            $"Award {awardId} approved by {approver.Email}: {award.Qty:0.##} @ {award.UnitPrice:0.00} to {award.SupplierId}; event {evt.Id} → Awarded");
        audit.Emit(approver, "procurement.award.approved", new
        {
            awardId, eventId = award.EventId, lotId = award.LotId, supplierId = award.SupplierId,
            qty = award.Qty, unitPrice = award.UnitPrice, approvedBy = approver.Id,
        });
        sourcing.Save();
        tx.Commit();
        return (new
        {
            award = ProjAward(award),
            lot = new
            {
                lotId = award.LotId,
                targetQty = target,
                awardedQty = approved + award.Qty,
                remainingTargetQty = target is { } c ? c - (approved + award.Qty) : (decimal?)null,
            },
        }, null, null);
    }

    // ==================== helpers ====================

    private static bool CanSeeEverything(CurrentUser user) => user.Has("sourcing:write") || user.Has("sourcing:approve");

    // date-only or local-offset client instants deserialize with a non-zero offset — Npgsql
    // refuses to write those to timestamptz, so every client-supplied instant normalizes to UTC
    private static DateTimeOffset ToUtc(DateTimeOffset v) => new(v.UtcDateTime);

    private (SourcingEvent? evt, string? code, string? message) LoadEditableEvent(string eventId)
    {
        var evt = sourcing.FindEvent(eventId);
        if (evt is null) return (null, "EVENT_NOT_FOUND", "Sourcing event not found.");
        return evt.Status != SourcingEventStatus.Draft
            ? ((SourcingEvent?)null, "EVENT_PUBLISHED", $"Event {eventId} is {evt.Status}; lots and criteria are frozen at publication (022).")
            : (evt, null, null);
    }

    // 023 AC1/AC2 + 044 + 047 AC2: suspended/blocked status or an in-force Hard gate fails
    // eligibility; a running time-bounded exception lifts the gate (023 AC3, R-T6e). Warning
    // gates and excepted hard gates are returned for the invitation record.
    private (bool Eligible, string? Reason, string? WarningGates) EvaluateEligibility(string supplierId)
    {
        var supplier = suppliers.Find(supplierId)!;
        if (supplier.Status is SupplierLifecycleStatus.Suspended or SupplierLifecycleStatus.Blocked)
            return (false, $"supplier status is {supplier.Status}", null);

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var gates = suppliers.GatesFor(supplierId).ToList();
        var hard = gates.FirstOrDefault(g => g.Severity == GateSeverity.Hard && g.EffectiveFrom <= today);
        if (hard is { } failing && !(failing.ExceptionUntil is { } until && until > now))
            return (false, $"hard gate '{failing.GateCode}' is in force (effective {failing.EffectiveFrom:yyyy-MM-dd})", null);

        var warnings = gates.Where(g => g.Severity == GateSeverity.Warning && g.EffectiveFrom <= today
                && (g.EffectiveTo is null || g.EffectiveTo >= today)) // minor (a): expired gates stop decorating
            .Select(g => g.GateCode)
            .Concat(gates.Where(g => g.Severity == GateSeverity.Hard && g.ExceptionUntil is { } u && u > now)
                .Select(g => $"{g.GateCode}(excepted)"))
            .Distinct().ToList();
        return (true, null, warnings.Count == 0 ? null : string.Join(",", warnings));
    }

    // FIX 4: row lock serializing award decisions per sourcing event — the table name is fixed in
    // the SQL literal (identifiers cannot be parameters) while the id stays parameterized
    private void LockSourcingEventRow(string eventId) =>
        db.Database.SqlQuery<string>($"SELECT id AS \"Value\" FROM sourcing_events WHERE id = {eventId} FOR UPDATE").Single();

    private bool BidDisqualified(Bid bid) =>
        sourcing.ScoresForEvent(bid.EventId).Any(s => s.BidId == bid.Id && s.PassFail == false);

    private bool RemainingAwardables(string eventId) =>
        sourcing.AwardsForEvent(eventId).Any(a => a.Status is AwardStatus.Recommended or AwardStatus.Approved);

    private static object ProjEvent(SourcingEvent e) => new
    {
        e.Id, e.Title, e.EventType, e.Status, e.OpensAt, e.ClosesAt, e.TimeZoneName, e.Currency,
        e.Version, e.CreatedBy, e.CreatedAt,
    };

    private static object ProjLot(EventLot l) => new
    {
        l.Id, l.EventId, l.LotNo, l.Description, l.ItemSku, l.TargetQty, l.CeilingValue,
    };

    private static object ProjCriterion(EvalCriterion c) => new
    {
        c.Id, c.EventId, c.Code, c.Name, c.Kind, c.WeightPct, c.ScaleMax,
    };

    private static object ProjInvitation(EventInvitation i) => new
    {
        i.Id, i.EventId, i.SupplierId, i.Status, i.Channel, i.SentAt, i.FailureReason, i.WarningGates,
    };

    // event-detail decoration: the recorded warning gates PLUS the supplier's current gate rows
    // (status/exception windows) so the screen can render live eligibility, not just invite-time
    private object ProjInvitationDecorated(EventInvitation i) => new
    {
        i.Id, i.EventId, i.SupplierId, i.Status, i.Channel, i.SentAt, i.FailureReason, i.WarningGates,
        supplierStatus = suppliers.Find(i.SupplierId)?.Status,
        gates = suppliers.GatesFor(i.SupplierId).Select(ProjGateRow).ToList(),
    };

    private static object ProjGateRow(SupplierGate g) => new
    {
        g.Id, g.SupplierId, g.GateCode, g.Severity, g.EffectiveFrom, g.EffectiveTo, g.EvidenceRef,
        g.ExceptionBy, g.ExceptionUntil,
    };

    // publication state lives on the event row: Draft = unpublished; anything else is published
    // with lots/criteria frozen at Version (bumped by published event-wide Q&A, 027 AC3)
    private static object ProjPublication(SourcingEvent e) => new
    {
        published = e.Status is not SourcingEventStatus.Draft,
        criteriaVersion = e.Version,
    };

    private static object ProjQa(EventQa q) => new
    {
        q.Id, q.EventId, q.SupplierId, q.Question, q.Answer, q.Scope, q.Published, q.PublishedAt, q.AnsweredBy,
    };

    private static object ProjBid(Bid b, List<object> lines, bool unsealed) => unsealed
        ? new { bidId = b.Id, b.EventId, b.SupplierId, b.EventVersion, b.Status, b.ContentDigest, b.SubmittedAt, b.OpenedBy, b.OpenedAt, lines }
        : (object)new { bidId = b.Id, b.EventId, b.SupplierId, b.EventVersion, b.Status, b.SubmittedAt, b.OpenedBy, b.OpenedAt, lines };

    private static object ProjLine(BidLine l, bool reveal) => reveal
        ? new { l.LotId, l.Qty, l.UnitPrice, l.DiscountPct, lineTotal = l.LineTotal } as object
        : new { l.LotId, l.Qty };

    private static object ProjScore(ScoreCard s, string criterionCode) => new
    {
        s.Id, s.EventId, s.BidId, criterionCode, s.EvaluatorUserId, s.RawScore, s.PassFail,
        s.Comment, s.CriteriaVersion, s.SubmittedAt,
    };

    private static object ProjAward(Award a) => new
    {
        a.Id, a.EventId, a.LotId, a.SupplierId, a.Qty, a.UnitPrice, a.Status, a.BasisEventVersion,
        a.Recommendation, a.CreatedBy, a.ApprovedBy, a.ApprovedAt, a.RejectionReason, a.CreatedAt,
    };

    // read summary (event detail + awards list): while sealed (no sourcing:approve, no approved
    // opening) the unitPrice is stripped exactly like the bid lines (FIX 1)
    private static object ProjAwardSummary(Award a, bool reveal) => reveal
        ? new
        {
            a.Id, a.EventId, a.LotId, a.SupplierId, a.Qty, a.UnitPrice, a.Status, a.BasisEventVersion,
            a.CreatedBy, a.ApprovedBy, a.ApprovedAt, a.RejectionReason,
        }
        : (object)new
        {
            a.Id, a.EventId, a.LotId, a.SupplierId, a.Qty, a.Status, a.BasisEventVersion, a.CreatedBy,
        };
}
