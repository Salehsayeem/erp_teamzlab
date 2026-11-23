namespace Erp.Api;

public enum PoStatus { Draft, PendingApproval, Approved, Sent, Received, Rejected }

public record Supplier(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy);

public record PoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);

// EF Core cannot bind collection navigations in a primary constructor (task-2 §2) — Lines lives in
// a settable body property that EF populates; the secondary constructor keeps every Task-1
// positional call site byte-identical. JSON keeps a single "lines" field.
public record PurchaseOrder(
    string Id, string TenantId, string LegalEntityId, string SupplierId, PoStatus Status,
    string CreatedBy, DateTimeOffset CreatedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null)
{
    public List<PoLine> Lines { get; set; } = [];
    public PurchaseOrder(string id, string tenantId, string legalEntityId, string supplierId, PoStatus status,
        IReadOnlyList<PoLine> lines, string createdBy, DateTimeOffset createdAt,
        string? approvedBy = null, DateTimeOffset? approvedAt = null, string? rejectionReason = null)
        : this(id, tenantId, legalEntityId, supplierId, status, createdBy, createdAt, approvedBy, approvedAt, rejectionReason)
    { Lines = [.. lines]; }
}

public record Receipt(string Id, string TenantId, string LegalEntityId, string PurchaseOrderId, string ItemSku, decimal Qty, DateTimeOffset ReceivedAt, string ReceivedBy);
