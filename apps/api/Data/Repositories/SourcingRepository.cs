using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// T6 sourcing tables — every query filters tenant+entity from the scoped EntityContext; state
// flips go through the Set helper so EF writes positional-record properties via backing fields.
public sealed class SourcingRepository(ErpDbContext db, EntityContext ec) : ISourcingRepository
{
    private IQueryable<SourcingEvent> EventsScoped() =>
        db.SourcingEvents.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);
    private IQueryable<EventLot> LotsScoped() =>
        db.EventLots.Where(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId);
    private IQueryable<EvalCriterion> CriteriaScoped() =>
        db.EvalCriteria.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);
    private IQueryable<EventInvitation> InvitationsScoped() =>
        db.EventInvitations.Where(i => i.TenantId == ec.TenantId && i.LegalEntityId == ec.EntityId);
    private IQueryable<EventQa> QaScoped() =>
        db.EventQas.Where(q => q.TenantId == ec.TenantId && q.LegalEntityId == ec.EntityId);
    private IQueryable<Bid> BidsScoped() =>
        db.Bids.Where(b => b.TenantId == ec.TenantId && b.LegalEntityId == ec.EntityId);
    private IQueryable<BidLine> LinesScoped() =>
        db.BidLines.Where(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId);
    private IQueryable<EvaluatorAssignment> AssignmentsScoped() =>
        db.EvaluatorAssignments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);
    private IQueryable<ScoreCard> ScoresScoped() =>
        db.ScoreCards.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
    private IQueryable<Award> AwardsScoped() =>
        db.Awards.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);

    public List<SourcingEvent> AllEvents(SourcingEventStatus? status, SourcingEventType? eventType) =>
        Filtered(EventsScoped(), status, eventType).OrderBy(e => e.Id).ToList();

    // 031 AC1 — evaluators see only events they are assigned to
    public List<SourcingEvent> EventsForEvaluator(string userId, SourcingEventStatus? status, SourcingEventType? eventType) =>
        Filtered(EventsScoped()
                .Where(e => AssignmentsScoped().Any(a => a.EventId == e.Id && a.UserId == userId)),
            status, eventType).OrderBy(e => e.Id).ToList();

    private static IQueryable<SourcingEvent> Filtered(
        IQueryable<SourcingEvent> query, SourcingEventStatus? status, SourcingEventType? eventType)
    {
        if (status is not null) query = query.Where(e => e.Status == status);
        if (eventType is not null) query = query.Where(e => e.EventType == eventType);
        return query;
    }

    public SourcingEvent? FindEvent(string eventId) => EventsScoped().FirstOrDefault(e => e.Id == eventId);
    public void AddEvent(SourcingEvent sourcingEvent) => db.SourcingEvents.Add(sourcingEvent);

    public List<EventLot> LotsForEvent(string eventId) =>
        LotsScoped().Where(l => l.EventId == eventId).OrderBy(l => l.Id).ToList();

    public EventLot? FindLot(string eventId, string lotId) =>
        LotsScoped().FirstOrDefault(l => l.EventId == eventId && l.Id == lotId);

    public void AddLot(EventLot lot) => db.EventLots.Add(lot);

    public List<EvalCriterion> CriteriaForEvent(string eventId) =>
        CriteriaScoped().Where(c => c.EventId == eventId).OrderBy(c => c.Code).ToList();

    public EvalCriterion? FindCriterion(string eventId, string criterionId) =>
        CriteriaScoped().FirstOrDefault(c => c.EventId == eventId && c.Id == criterionId);

    public void AddCriterion(EvalCriterion criterion) => db.EvalCriteria.Add(criterion);

    public List<EventInvitation> InvitationsForEvent(string eventId) =>
        InvitationsScoped().Where(i => i.EventId == eventId).OrderBy(i => i.Id).ToList();

    public EventInvitation? FindInvitation(string eventId, string supplierId) =>
        InvitationsScoped().FirstOrDefault(i => i.EventId == eventId && i.SupplierId == supplierId);

    public void AddInvitation(EventInvitation invitation) => db.EventInvitations.Add(invitation);

    public List<EventQa> QaForEvent(string eventId) =>
        QaScoped().Where(q => q.EventId == eventId).OrderBy(q => q.Id).ToList();

    public void AddQa(EventQa qa) => db.EventQas.Add(qa);

    public List<Bid> BidsForEvent(string eventId) =>
        BidsScoped().Where(b => b.EventId == eventId).OrderBy(b => b.Id).ToList();

    public Bid? FindBid(string eventId, string bidId) =>
        BidsScoped().FirstOrDefault(b => b.EventId == eventId && b.Id == bidId);

    public void AddBid(Bid bid) => db.Bids.Add(bid);

    public List<BidLine> LinesForBid(string bidId) =>
        LinesScoped().Where(l => l.BidId == bidId).OrderBy(l => l.Id).ToList();

    public BidLine? FindBidLine(string bidId, string lotId) =>
        LinesScoped().FirstOrDefault(l => l.BidId == bidId && l.LotId == lotId);

    public void AddBidLine(BidLine line) => db.BidLines.Add(line);

    public bool BidForSupplierExists(string eventId, string supplierId) =>
        BidsScoped().Any(b => b.EventId == eventId && b.SupplierId == supplierId);

    public List<EvaluatorAssignment> AssignmentsForEvent(string eventId) =>
        AssignmentsScoped().Where(a => a.EventId == eventId).OrderBy(a => a.Id).ToList();

    public EvaluatorAssignment? FindAssignment(string eventId, string userId) =>
        AssignmentsScoped().FirstOrDefault(a => a.EventId == eventId && a.UserId == userId);

    public void AddAssignment(EvaluatorAssignment assignment) => db.EvaluatorAssignments.Add(assignment);

    public List<ScoreCard> ScoresForEvent(string eventId) =>
        ScoresScoped().Where(s => s.EventId == eventId).OrderBy(s => s.Id).ToList();

    public ScoreCard? FindScore(string eventId, string bidId, string criterionId, string evaluatorUserId) =>
        ScoresScoped().FirstOrDefault(s => s.EventId == eventId && s.BidId == bidId
            && s.CriterionId == criterionId && s.EvaluatorUserId == evaluatorUserId);

    public void AddScore(ScoreCard score) => db.ScoreCards.Add(score);

    public void SetScore(ScoreCard score, decimal? rawScore, bool? passFail, string? comment, int criteriaVersion)
    {
        Set(score, nameof(ScoreCard.RawScore), rawScore);
        Set(score, nameof(ScoreCard.PassFail), passFail);
        Set(score, nameof(ScoreCard.Comment), comment);
        Set(score, nameof(ScoreCard.CriteriaVersion), criteriaVersion);
        Set(score, nameof(ScoreCard.SubmittedAt), DateTimeOffset.UtcNow);
    }

    public List<Award> AwardsForEvent(string eventId) =>
        AwardsScoped().Where(a => a.EventId == eventId).OrderBy(a => a.Id).ToList();

    public Award? FindAward(string awardId) => AwardsScoped().FirstOrDefault(a => a.Id == awardId);

    // 034 AC1 — per-lot ceiling counts APPROVED awards only (rejections and alternates free the qty)
    public decimal ApprovedQtyForLot(string lotId, string excludingAwardId) =>
        AwardsScoped().Where(a => a.LotId == lotId && a.Id != excludingAwardId && a.Status == AwardStatus.Approved)
            .Sum(a => (decimal?)a.Qty) ?? 0m;

    public void AddAward(Award award) => db.Awards.Add(award);

    public void SetAwardApproved(Award award, string approvedBy, DateTimeOffset approvedAt)
    {
        Set(award, nameof(Award.Status), AwardStatus.Approved);
        Set(award, nameof(Award.ApprovedBy), approvedBy);
        Set(award, nameof(Award.ApprovedAt), approvedAt);
    }

    public void SetAwardRejected(Award award, string rejectionReason, string approvedBy, DateTimeOffset approvedAt)
    {
        Set(award, nameof(Award.Status), AwardStatus.Rejected);
        Set(award, nameof(Award.RejectionReason), rejectionReason);
        Set(award, nameof(Award.ApprovedBy), approvedBy);
        Set(award, nameof(Award.ApprovedAt), approvedAt);
    }

    public void SetEventStatus(SourcingEvent sourcingEvent, SourcingEventStatus status) =>
        Set(sourcingEvent, nameof(SourcingEvent.Status), status);

    public void SetEventVersion(SourcingEvent sourcingEvent, int version) =>
        Set(sourcingEvent, nameof(SourcingEvent.Version), version);

    public void SetBidOpened(Bid bid, string openedBy, DateTimeOffset openedAt)
    {
        Set(bid, nameof(Bid.Status), BidStatus.Opened);
        Set(bid, nameof(Bid.OpenedBy), openedBy);
        Set(bid, nameof(Bid.OpenedAt), openedAt);
    }

    public void SetBidStatus(Bid bid, BidStatus status) => Set(bid, nameof(Bid.Status), status);

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
