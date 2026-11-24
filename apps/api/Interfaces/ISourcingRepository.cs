namespace Erp.Api.Interfaces;

// T6 sourcing aggregate — events, lots, frozen criteria, invitations, Q&A, bids + lines,
// evaluator assignments, score cards and awards. Every query filters tenant+entity from the
// scoped EntityContext (R14 defense-in-depth); state flips go through the Set helper.
public interface ISourcingRepository
{
    List<SourcingEvent> AllEvents(SourcingEventStatus? status, SourcingEventType? eventType);
    List<SourcingEvent> EventsForEvaluator(string userId, SourcingEventStatus? status, SourcingEventType? eventType);
    SourcingEvent? FindEvent(string eventId);
    void AddEvent(SourcingEvent sourcingEvent);

    List<EventLot> LotsForEvent(string eventId);
    EventLot? FindLot(string eventId, string lotId);
    void AddLot(EventLot lot);

    List<EvalCriterion> CriteriaForEvent(string eventId);
    EvalCriterion? FindCriterion(string eventId, string criterionId);
    void AddCriterion(EvalCriterion criterion);

    List<EventInvitation> InvitationsForEvent(string eventId);
    EventInvitation? FindInvitation(string eventId, string supplierId);
    void AddInvitation(EventInvitation invitation);

    List<EventQa> QaForEvent(string eventId);
    void AddQa(EventQa qa);

    List<Bid> BidsForEvent(string eventId);
    Bid? FindBid(string eventId, string bidId);
    void AddBid(Bid bid);
    List<BidLine> LinesForBid(string bidId);
    BidLine? FindBidLine(string bidId, string lotId);
    void AddBidLine(BidLine line);
    bool BidForSupplierExists(string eventId, string supplierId);

    List<EvaluatorAssignment> AssignmentsForEvent(string eventId);
    EvaluatorAssignment? FindAssignment(string eventId, string userId);
    void AddAssignment(EvaluatorAssignment assignment);

    List<ScoreCard> ScoresForEvent(string eventId);
    ScoreCard? FindScore(string eventId, string bidId, string criterionId, string evaluatorUserId);
    void AddScore(ScoreCard score);
    void SetScore(ScoreCard score, decimal? rawScore, bool? passFail, string? comment, int criteriaVersion);

    List<Award> AwardsForEvent(string eventId);
    Award? FindAward(string awardId);
    decimal ApprovedQtyForLot(string lotId, string excludingAwardId);
    void AddAward(Award award);
    void SetAwardApproved(Award award, string approvedBy, DateTimeOffset approvedAt);
    void SetAwardRejected(Award award, string rejectionReason, string approvedBy, DateTimeOffset approvedAt);
    void SetEventStatus(SourcingEvent sourcingEvent, SourcingEventStatus status);
    void SetEventVersion(SourcingEvent sourcingEvent, int version);
    void SetBidOpened(Bid bid, string openedBy, DateTimeOffset openedAt);
    void SetBidStatus(Bid bid, BidStatus status);

    int Save();
}
