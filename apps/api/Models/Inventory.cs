namespace Erp.Api;

public enum TransferStatus { Posted }
public enum CountStatus { Open, Closed }

public record Warehouse(string Id, string TenantId, string LegalEntityId, string Code, string Name);
public record StockLocation(string Id, string TenantId, string LegalEntityId, string WarehouseId, string Code, string Name);

// stock is held per location (ERP-INVENTORY-005); receipts land in the default receiving location
public record StockLevel(string TenantId, string LegalEntityId, string WarehouseId, string LocationId, string ItemSku, decimal QtyOnHand);

public record StockTransfer(string Id, string TenantId, string LegalEntityId, string FromLocationId, string ToLocationId, string ItemSku, decimal Qty, TransferStatus Status, DateTimeOffset CreatedAt, string CreatedBy);
public record CountLine(string ItemSku, decimal CountedQty, decimal SystemQty);
public record StockCount(string Id, string TenantId, string LegalEntityId, string LocationId, CountStatus Status, IReadOnlyList<CountLine> Items, string CreatedBy, DateTimeOffset CreatedAt, string? ClosedBy = null, DateTimeOffset? ClosedAt = null);
public record StockAdjustment(string Id, string TenantId, string LegalEntityId, string CountId, string ItemSku, string LocationId, decimal QtyVariance, decimal UnitCost, decimal Amount, string JournalId, DateTimeOffset CreatedAt, string CreatedBy);
