namespace Erp.Api;

public enum SoStatus { Draft, PendingApproval, Confirmed, Shipped, Invoiced, Rejected }

public record Customer(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy);

public record SoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);

// Lines as a settable body property for EF (see PurchaseOrder) — same JSON shape, same call sites
public record SalesOrder(string Id, string TenantId, string LegalEntityId, string CustomerId, SoStatus Status, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null)
{
    public List<SoLine> Lines { get; set; } = [];
    public SalesOrder(string id, string tenantId, string legalEntityId, string customerId, SoStatus status,
        IReadOnlyList<SoLine> lines, string createdBy, DateTimeOffset createdAt,
        string? approvedBy = null, DateTimeOffset? approvedAt = null, string? rejectionReason = null)
        : this(id, tenantId, legalEntityId, customerId, status, createdBy, createdAt, approvedBy, approvedAt, rejectionReason)
    { Lines = [.. lines]; }
}

public record Shipment(string Id, string TenantId, string LegalEntityId, string SalesOrderId, string ItemSku, decimal Qty, DateTimeOffset ShippedAt, string ShippedBy);
