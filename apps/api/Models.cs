namespace Erp.Api;

// Domain models — mock-mode in-memory store (04_Data_Model seed lists, trimmed to pilot slice).
// shortcut: no persistence, swaps to EF Core + PostgreSQL when DB is reachable (D-10)

public enum PoStatus { Draft, PendingApproval, Approved, Sent, Received, Rejected }
public enum JournalStatus { Pending, Posted }

public record Tenant(string Id, string Name);
public record LegalEntity(string Id, string TenantId, string Name, string Jurisdiction, string Currency, string DefaultLocale);

public record Role(string Id, string Name, string[] Permissions);
public record Membership(string UserId, string TenantId, string LegalEntityId, string RoleId);
public record User(string Id, string Email, string DisplayName, bool Active);

public record Supplier(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy);

public record PoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);
public record PurchaseOrder(
    string Id, string TenantId, string LegalEntityId, string SupplierId, PoStatus Status,
    IReadOnlyList<PoLine> Lines, string CreatedBy, DateTimeOffset CreatedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null);

public record Receipt(string Id, string TenantId, string LegalEntityId, string PurchaseOrderId, string ItemSku, decimal Qty, DateTimeOffset ReceivedAt, string ReceivedBy);

public record StockLevel(string TenantId, string LegalEntityId, string ItemSku, decimal QtyOnHand);

public record JournalLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);
public record Journal(string Id, string TenantId, string LegalEntityId, JournalStatus Status, string Memo, IReadOnlyList<JournalLine> Lines, DateTimeOffset CreatedAt, string CreatedBy, string? ReversalOfId = null);

public record AuditEvent(string Id, string TenantId, string ActorUserId, string Action, string EntityType, string EntityId, DateTimeOffset At, string Summary);

public record OutboxMessage(string Id, string TenantId, string Type, int Version, DateTimeOffset OccurredAt, string ActorUserId, string PayloadJson);

// Requests (validated before business logic — CLAUDE.md backend rules)
public record CreateSupplierReq(string Name, string TaxId, string Email);
public record CreatePoReq(string SupplierId, IReadOnlyList<PoLine> Lines);
public record ApprovePoReq(string? Reason);
public record CreateReceiptReq(string PurchaseOrderId, string ItemSku, decimal Qty);
public record TokenExchangeReq(string Email);
