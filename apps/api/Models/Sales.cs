namespace Erp.Api;

public enum SoStatus { Draft, PendingApproval, Confirmed, Shipped, Invoiced, Rejected }

public record Customer(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy);

public record SoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);
public record SalesOrder(string Id, string TenantId, string LegalEntityId, string CustomerId, SoStatus Status, IReadOnlyList<SoLine> Lines, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null);

public record Shipment(string Id, string TenantId, string LegalEntityId, string SalesOrderId, string ItemSku, decimal Qty, DateTimeOffset ShippedAt, string ShippedBy);
