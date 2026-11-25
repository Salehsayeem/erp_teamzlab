namespace Erp.Api.Interfaces;

// Warehouses / locations / stock levels / transfers / counts / adjustments — plus the location-
// scoped stock mutation helpers that used to be Common/Stock.cs (QtyAt, Apply, Draw)
public interface IInventoryRepository
{
    StockLocation? FindLocation(string locationId);
    Warehouse? FindWarehouse(string warehouseId);
    List<Warehouse> AllWarehouses();
    List<StockLocation> AllLocations();
    List<StockLevel> AllStock();
    StockLevel? FindStockRow(string tenantId, string legalEntityId, string itemSku);
    bool WarehouseCodeExists(string code);
    bool LocationCodeExists(string warehouseId, string code);
    void AddWarehouse(Warehouse warehouse);
    void AddLocation(StockLocation location);
    void AddTransfer(StockTransfer transfer);
    void AddCount(StockCount count);
    StockCount? FindCount(string countId);
    void MarkCountClosed(StockCount count, string closedBy, DateTimeOffset closedAt);
    void AddAdjustment(StockAdjustment adjustment);
    decimal QtyAt(string locationId, string itemSku);
    decimal QtyOf(string itemSku);
    // MF9 (059 AC2): open quality-hold quantity for the item — availability = QtyOf − HeldQtyFor
    decimal HeldQtyFor(string itemSku);
    void ApplyStock(string warehouseId, string locationId, string itemSku, decimal delta);
    void DrawStock(string itemSku, decimal qty);
    void SetStockQty(StockLevel row, decimal qty);
    StockLocation DefaultReceivingLocation();
    int Save();
}
