using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IRequisitionRepository
{
    Requisition? Find(string id);
    List<Requisition> All(RequisitionStatus? status);
    void Add(Requisition requisition);
    ApprovalStep? FindStep(string requisitionId);
    void AddStep(ApprovalStep step);
    List<RequisitionDecision> DecisionsFor(string requisitionId);
    void AddDecision(RequisitionDecision decision);
    List<ApprovalDelegation> ActiveDelegationsFor(string delegateId, DateTimeOffset now);
    void AddDelegation(ApprovalDelegation delegation);
    ApprovalRule? ActiveRule();
    bool ReservationExists(string txnId);
    void AddReservation(BudgetReservation reservation);
    BudgetReservation? FindReservation(string txnId);
    // records carry init-only properties — mutations go through the EF change tracker (house pattern)
    void SetField(object entity, string name, object value);
    int Save();
}
