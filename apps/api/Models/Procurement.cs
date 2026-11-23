namespace Erp.Api;

public enum PoStatus { Draft, PendingApproval, Approved, Sent, Received, Rejected }

public record Supplier(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy);

public record PoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);
public record PurchaseOrder(
    string Id, string TenantId, string LegalEntityId, string SupplierId, PoStatus Status,
    IReadOnlyList<PoLine> Lines, string CreatedBy, DateTimeOffset CreatedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null);

public record Receipt(string Id, string TenantId, string LegalEntityId, string PurchaseOrderId, string ItemSku, decimal Qty, DateTimeOffset ReceivedAt, string ReceivedBy);
