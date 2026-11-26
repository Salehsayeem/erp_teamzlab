using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// P4 receipt workspace (055–059): dispatch evidence, ASN intake with quarantine, quality
// inspections separating accepted from invoice-matchable, linked returns/claims, and the
// read-only balances handoff for FINANCE AP (060/Q-P6).
public interface IReceiptInspectionService
{
    (Dispatch? Dispatch, string? Code, string? Message) SendDispatch(CurrentUser u, string poId, int orderVersion, DispatchState state, string? detail);
    (AdvanceShipNotice? Asn, string? Code, string? Message) IntakeAsn(CurrentUser u, string? shipmentRef, string poId, int orderVersion, IReadOnlyList<AsnLineInput>? lines);
    object Asns(AsnStatus? status);
    (ReceiptInspection? Inspection, string? Code, string? Message) CreateInspection(CurrentUser u, string receiptId, string? itemSku, decimal qtyReceived);
    (ReceiptInspection? Inspection, string? Code, string? Message) DisposeInspection(CurrentUser actor, string receiptId, string inspectionId, InspectionStatus status, decimal qtyAccepted, decimal qtyRejected, string? defectCode, string? evidenceRef);
    (SupplierReturn? Return, string? Code, string? Message) CreateReturn(CurrentUser u, string? receiptId, string? poId, string? itemSku, decimal qty, string? reason, ClaimKind kind);
    (SupplierClaim? Claim, string? Code, string? Message) CreateClaim(CurrentUser u, string? receiptId, string? poId, ClaimKind kind, decimal? amount);
    (SupplierClaim? Claim, string? Code, string? Message) CloseClaim(CurrentUser actor, string claimId, string? outcomeCode, string? reconciliationStatus);
    object? Balances(string orderId);
    object? ReceiptDetail(string receiptId);
}
