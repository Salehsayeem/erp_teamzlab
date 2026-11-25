using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// P3 PO change control (052) + supplier acknowledgements (053). Fixed maker-checker on change
// approval (Q-P2); dispatch is blocked while a change is pending (052 AC2).
public interface IPurchaseOrderChangeService
{
    (PurchaseOrderChange? Change, string? Code, string? Message) Create(CurrentUser u, string poId, string? rationale, string? supplierId, IReadOnlyList<PoChangeLineInput>? lines);
    (PurchaseOrderChange? Change, string? Code, string? Message) Decide(CurrentUser actor, string poId, string changeId, bool approve);
    object Changes(string poId);
    (PurchaseOrderAcknowledgement? Ack, string? Code, string? Message) Acknowledge(CurrentUser u, string poId, string? supplierId, int orderVersion, IReadOnlyList<PoAckLineInput>? lines);
    bool HasPendingChange(string poId);
    object? PoDetail(string poId);
}
