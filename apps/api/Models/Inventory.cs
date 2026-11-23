namespace Erp.Api;

public enum TransferStatus { Posted }
public enum CountStatus { Open, Closed }

public record Warehouse(string Id, string TenantId, string LegalEntityId, string Code, string Name);
public record StockLocation(string Id, string TenantId, string LegalEntityId, string WarehouseId, string Code, string Name);

// stock is held per location (ERP-INVENTORY-005); receipts land in the default receiving location
public record StockLevel(string TenantId, string LegalEntityId, string WarehouseId, string LocationId, string ItemSku, decimal QtyOnHand);

public record StockTransfer(string Id, string TenantId, string LegalEntityId, string FromLocationId, string ToLocationId, string ItemSku, decimal Qty, TransferStatus Status, DateTimeOffset CreatedAt, string CreatedBy);
public record CountLine(string ItemSku, decimal CountedQty, decimal SystemQty);

// Items as a settable body property for EF (see PurchaseOrder) — same JSON shape, same call sites
public record StockCount(string Id, string TenantId, string LegalEntityId, string LocationId, CountStatus Status, string CreatedBy, DateTimeOffset CreatedAt, string? ClosedBy = null, DateTimeOffset? ClosedAt = null)
{
    public List<CountLine> Items { get; set; } = [];
    public StockCount(string id, string tenantId, string legalEntityId, string locationId, CountStatus status,
        IReadOnlyList<CountLine> items, string createdBy, DateTimeOffset createdAt,
        string? closedBy = null, DateTimeOffset? closedAt = null)
        : this(id, tenantId, legalEntityId, locationId, status, createdBy, createdAt, closedBy, closedAt)
    { Items = [.. items]; }
}
public record StockAdjustment(string Id, string TenantId, string LegalEntityId, string CountId, string ItemSku, string LocationId, decimal QtyVariance, decimal UnitCost, decimal Amount, string JournalId, DateTimeOffset CreatedAt, string CreatedBy);
