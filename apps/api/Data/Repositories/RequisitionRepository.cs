using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// P1 requisition aggregate reads/writes — tenant+entity scoped on every query (R14).
public sealed class RequisitionRepository(ErpDbContext db, EntityContext ec) : IRequisitionRepository
{
    private IQueryable<Requisition> Scoped() =>
        db.Requisitions.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    private IQueryable<ApprovalStep> StepsScoped() =>
        db.ApprovalSteps.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    private IQueryable<RequisitionDecision> DecisionsScoped() =>
        db.RequisitionDecisions.Where(d => d.TenantId == ec.TenantId && d.LegalEntityId == ec.EntityId);

    private IQueryable<ApprovalDelegation> DelegationsScoped() =>
        db.ApprovalDelegations.Where(d => d.TenantId == ec.TenantId && d.LegalEntityId == ec.EntityId);

    private IQueryable<ApprovalRule> RulesScoped() =>
        db.ApprovalRules.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    public Requisition? Find(string id) =>
        Scoped().Include(r => r.Lines).FirstOrDefault(r => r.Id == id);

    public List<Requisition> All(RequisitionStatus? status)
    {
        var query = Scoped().Include(r => r.Lines).AsQueryable();
        if (status is { } s) query = query.Where(r => r.Status == s);
        return query.OrderBy(r => r.Id).ToList();
    }

    public void Add(Requisition requisition)
    {
        db.Requisitions.Add(requisition);
        foreach (var line in requisition.Lines) SetLineTenant(line);
    }

    public ApprovalStep? FindStep(string requisitionId) =>
        StepsScoped().Where(s => s.RequisitionId == requisitionId).OrderBy(s => s.StepNo).FirstOrDefault();

    // fix I6: the pending decision token per requisition, one query — the decide endpoint
    // validates against the step's version, not the requisition's record version
    public Dictionary<string, int> PendingStepVersions(IReadOnlyCollection<string> requisitionIds)
    {
        if (requisitionIds.Count == 0) return [];
        return StepsScoped().Where(s => s.Decision == null && requisitionIds.Contains(s.RequisitionId))
            .OrderBy(s => s.RequisitionId).ThenBy(s => s.StepNo).ToList()
            .GroupBy(s => s.RequisitionId)
            .ToDictionary(g => g.Key, g => g.First().Version);
    }

    public void AddStep(ApprovalStep step) => db.ApprovalSteps.Add(step);

    // append-only history view (017 AC3) — insertion order preserved for equal instants
    public List<RequisitionDecision> DecisionsFor(string requisitionId) =>
        DecisionsScoped().Where(d => d.RequisitionId == requisitionId).OrderBy(d => d.At).ThenBy(d => d.Id).ToList();

    public void AddDecision(RequisitionDecision decision) => db.RequisitionDecisions.Add(decision);

    public List<ApprovalDelegation> ActiveDelegationsFor(string delegateId, DateTimeOffset now) =>
        DelegationsScoped().Where(d => d.DelegateId == delegateId && d.From <= now && d.To >= now)
            .OrderBy(d => d.CreatedAt).ToList();

    public void AddDelegation(ApprovalDelegation delegation) => db.ApprovalDelegations.Add(delegation);

    public ApprovalRule? ActiveRule() =>
        RulesScoped().Where(r => r.IsActive).OrderBy(r => r.Id).FirstOrDefault();

    public bool ReservationExists(string txnId) =>
        db.BudgetReservations.Any(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.TxnId == txnId);

    public void AddReservation(BudgetReservation reservation) => db.BudgetReservations.Add(reservation);

    public BudgetReservation? FindReservation(string txnId) =>
        db.BudgetReservations.FirstOrDefault(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.TxnId == txnId);

    // records carry init-only properties — mutations go through the EF change tracker (house pattern)
    public void SetField(object entity, string name, object value) =>
        db.Entry(entity).Property(name).CurrentValue = value;

    public int Save() => db.SaveChanges();

    private void SetLineTenant(object line)
    {
        db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
        db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
    }
}
