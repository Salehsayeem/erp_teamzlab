using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// T6 sourcing spine: events → lots/criteria → invitations/Q&A → buyer-recorded bids → approved
// opening → evaluator scoring → award recommendation + maker-checked approval (R-T6a..i)
public interface ISourcingService
{
    object List(CurrentUser user, SourcingEventStatus? status, SourcingEventType? eventType);
    // composition reads for the event/award screens (page reloads) — sealedness decided here
    (object? data, string? code, string? message) GetEvent(CurrentUser user, string eventId);
    (object? data, string? code, string? message) AwardsFor(CurrentUser user, string eventId);
    (object? data, string? code, string? message) Create(CurrentUser user, CreateEventRequest request);
    (object? data, string? code, string? message) AddLot(CurrentUser user, string eventId, AddLotRequest request);
    (object? data, string? code, string? message) AddCriterion(CurrentUser user, string eventId, AddCriterionRequest request);
    (object? data, string? code, string? message) Publish(CurrentUser user, string eventId);
    (object? data, string? code, string? message) Invite(CurrentUser user, string eventId, InviteSuppliersRequest request);
    (object? data, string? code, string? message) AnswerQa(CurrentUser user, string eventId, AnswerQaRequest request);
    (object? data, string? code, string? message) SubmitBid(CurrentUser user, string eventId, SubmitBidRequest request);
    (object? data, string? code, string? message) OpenBids(CurrentUser user, string eventId);
    (object? data, string? code, string? message) ListBids(CurrentUser user, string eventId);
    (object? data, string? code, string? message) AssignEvaluators(CurrentUser user, string eventId, AssignEvaluatorsRequest request);
    (object? data, string? code, string? message) SubmitScore(CurrentUser user, string eventId, SubmitScoreRequest request);
    (object? data, string? code, string? message) ScoreSummary(CurrentUser user, string eventId);
    (object? data, string? code, string? message) RecommendAward(CurrentUser user, RecommendAwardRequest request);
    (object? data, string? code, string? message) ApproveAward(CurrentUser approver, string awardId, ApproveAwardRequest request);
}
