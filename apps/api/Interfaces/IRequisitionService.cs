using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// P1 requisitions (011–019). Decisions are fixed maker-checker (Q-P2) with versioned tokens (018).
public interface IRequisitionService
{
    object List(RequisitionStatus? status);
    (Requisition? requisition, string? code, string? message) Create(CurrentUser u, string? purpose, string? currency, IReadOnlyList<RequisitionLineInput>? lines);
    object? Detail(string id);
    (Requisition? requisition, string? code, string? message) Submit(CurrentUser u, string id, string? overrideReason);
    (Requisition? requisition, string? code, string? message) Decide(CurrentUser actor, string id, string? decision, int version, string? reason);
    (Requisition? requisition, string? code, string? message) FlagException(CurrentUser u, string id, string? reasonCode, string? narrative, string? evidenceRef);
    (ApprovalDelegation? delegation, string? code, string? message) CreateDelegation(CurrentUser u, string? delegateId, DateTimeOffset from, DateTimeOffset to, decimal? maxAmount, string? categoryId, string? reason);
}
