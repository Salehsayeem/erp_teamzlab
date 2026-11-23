namespace Erp.Api;

// Domain models — mock-mode in-memory store (04_Data_Model seed lists, trimmed to pilot slice).
// shortcut: no persistence, swaps to EF Core + PostgreSQL when DB is reachable (D-10)

public enum PoStatus { Draft, PendingApproval, Approved, Sent, Received, Rejected }
public enum SoStatus { Draft, PendingApproval, Confirmed, Shipped, Invoiced, Rejected }
public enum JournalStatus { Pending, Posted }
public enum TransferStatus { Posted }
public enum CountStatus { Open, Closed }

// ASSETS domain slice (ERP-ASSETS-001..024 trimmed pilot)
public enum ProjectStatus { Planned, Active, OnHold, Closed }
public enum ProjectTaskStatus { Open, Done }
public enum CasePriority { Low, Medium, High }
public enum CaseStatus { Open, InProgress, Resolved, Closed }
public enum AssetStatus { Draft, Active, Disposed }

public record Tenant(string Id, string Name);
public record LegalEntity(string Id, string TenantId, string Name, string Jurisdiction, string Currency, string DefaultLocale);

public record Role(string Id, string Name, string[] Permissions);
public record Membership(string UserId, string TenantId, string LegalEntityId, string RoleId);
public record User(string Id, string Email, string DisplayName, bool Active);

public record Supplier(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy);

public record Customer(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy);
public record SoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);
public record SalesOrder(string Id, string TenantId, string LegalEntityId, string CustomerId, SoStatus Status, IReadOnlyList<SoLine> Lines, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null);
public record Shipment(string Id, string TenantId, string LegalEntityId, string SalesOrderId, string ItemSku, decimal Qty, DateTimeOffset ShippedAt, string ShippedBy);

public record PoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);
public record PurchaseOrder(
    string Id, string TenantId, string LegalEntityId, string SupplierId, PoStatus Status,
    IReadOnlyList<PoLine> Lines, string CreatedBy, DateTimeOffset CreatedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null);

public record Receipt(string Id, string TenantId, string LegalEntityId, string PurchaseOrderId, string ItemSku, decimal Qty, DateTimeOffset ReceivedAt, string ReceivedBy);

public record Warehouse(string Id, string TenantId, string LegalEntityId, string Code, string Name);
public record StockLocation(string Id, string TenantId, string LegalEntityId, string WarehouseId, string Code, string Name);

// stock is held per location (ERP-INVENTORY-005); receipts land in the default receiving location
public record StockLevel(string TenantId, string LegalEntityId, string WarehouseId, string LocationId, string ItemSku, decimal QtyOnHand);

public record StockTransfer(string Id, string TenantId, string LegalEntityId, string FromLocationId, string ToLocationId, string ItemSku, decimal Qty, TransferStatus Status, DateTimeOffset CreatedAt, string CreatedBy);
public record CountLine(string ItemSku, decimal CountedQty, decimal SystemQty);
public record StockCount(string Id, string TenantId, string LegalEntityId, string LocationId, CountStatus Status, IReadOnlyList<CountLine> Items, string CreatedBy, DateTimeOffset CreatedAt, string? ClosedBy = null, DateTimeOffset? ClosedAt = null);
public record StockAdjustment(string Id, string TenantId, string LegalEntityId, string CountId, string ItemSku, string LocationId, decimal QtyVariance, decimal UnitCost, decimal Amount, string JournalId, DateTimeOffset CreatedAt, string CreatedBy);

public record JournalLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);
public record Journal(string Id, string TenantId, string LegalEntityId, JournalStatus Status, string Memo, IReadOnlyList<JournalLine> Lines, DateTimeOffset CreatedAt, string CreatedBy, string? ReversalOfId = null);

public record Project(string Id, string TenantId, string LegalEntityId, string Name, ProjectStatus Status, decimal Budget, string? CustomerId, string CreatedBy, DateTimeOffset CreatedAt);
public record ProjectTask(string Id, string TenantId, string LegalEntityId, string ProjectId, string Name, ProjectTaskStatus Status, decimal HoursEstimate, decimal HoursActual);
public record ServiceCase(string Id, string TenantId, string LegalEntityId, string Title, CasePriority Priority, CaseStatus Status, string? AssignedTo, string CreatedBy, DateTimeOffset CreatedAt);
public record FixedAsset(string Id, string TenantId, string LegalEntityId, string Name, string Category, decimal AcquisitionCost, DateTimeOffset AcquisitionDate, AssetStatus Status, string CreatedBy, DateTimeOffset CreatedAt);
public record DepreciationSchedule(string Id, string TenantId, string LegalEntityId, string AssetId, decimal MonthlyAmount, DateTimeOffset StartDate, int PostedCount);

public record AuditEvent(string Id, string TenantId, string ActorUserId, string Action, string EntityType, string EntityId, DateTimeOffset At, string Summary);

public record OutboxMessage(string Id, string TenantId, string Type, int Version, DateTimeOffset OccurredAt, string ActorUserId, string PayloadJson);

// Requests (validated before business logic — CLAUDE.md backend rules)
public record CreateSupplierReq(string Name, string TaxId, string Email);
public record CreatePoReq(string SupplierId, IReadOnlyList<PoLine> Lines);
public record ApprovePoReq(string? Reason);
public record CreateReceiptReq(string PurchaseOrderId, string ItemSku, decimal Qty);
public record CreateCustomerReq(string Name, string TaxId, string Email);
public record CreateSoReq(string CustomerId, IReadOnlyList<SoLine> Lines);
public record ApproveSoReq(string? Reason);
public record ShipReq(string ItemSku, decimal Qty);
public record TokenExchangeReq(string Email);
public record CreateWarehouseReq(string Code, string Name);
public record CreateLocationReq(string WarehouseId, string Code, string Name);
public record CreateTransferReq(string FromLocationId, string ToLocationId, string ItemSku, decimal Qty);
public record CountItemReq(string ItemSku, decimal CountedQty);
public record CreateCountReq(string LocationId, IReadOnlyList<CountItemReq> Items);
public record CreateProjectReq(string Name, decimal Budget, string? CustomerId);
public record CreateTaskReq(string Name, decimal HoursEstimate);
public record CompleteTaskReq(decimal HoursActual);
public record CreateCaseReq(string Title, CasePriority? Priority, string? AssignedTo);
public record TransitionCaseReq(CaseStatus Status);
public record CreateAssetReq(string Name, string Category, decimal AcquisitionCost, DateTimeOffset AcquisitionDate);
