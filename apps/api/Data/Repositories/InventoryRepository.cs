using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Warehouses / locations / stock / transfers / counts / adjustments — plus the location-scoped
// stock helpers that used to be Common/Stock.cs. Both legs of a transfer and the count-close
// adjustments commit inside the caller's single SaveChanges (task-2 §4).
public sealed class InventoryRepository(ErpDbContext db, EntityContext ec) : IInventoryRepository
{
    private IQueryable<Warehouse> WarehousesScoped() =>
        db.Warehouses.Where(w => w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId);

    private IQueryable<StockLocation> LocationsScoped() =>
        db.Locations.Where(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId);

    private IQueryable<StockLevel> StockScoped() =>
        db.Stock.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    public StockLocation? FindLocation(string locationId) =>
        LocationsScoped().FirstOrDefault(l => l.Id == locationId);

    public Warehouse? FindWarehouse(string warehouseId) =>
        WarehousesScoped().FirstOrDefault(w => w.Id == warehouseId);

    public List<Warehouse> AllWarehouses() => WarehousesScoped().OrderBy(w => w.Id).ToList();

    public List<StockLocation> AllLocations() => LocationsScoped().OrderBy(l => l.Id).ToList();

    public List<StockLevel> AllStock() =>
        StockScoped().OrderBy(s => s.WarehouseId).ThenBy(s => s.LocationId).ThenBy(s => s.ItemSku).ToList();

    public StockLevel? FindStockRow(string tenantId, string legalEntityId, string itemSku) =>
        db.Stock.Where(s => s.TenantId == tenantId && s.LegalEntityId == legalEntityId && s.ItemSku == itemSku)
            .OrderBy(s => s.WarehouseId).ThenBy(s => s.LocationId)
            .FirstOrDefault(); // deterministic stand-in for the in-memory FindIndex (first row)

    public bool WarehouseCodeExists(string code) => WarehousesScoped().Any(w => w.Code == code);

    public bool LocationCodeExists(string warehouseId, string code) =>
        LocationsScoped().Any(l => l.WarehouseId == warehouseId && l.Code == code);

    public void AddWarehouse(Warehouse warehouse) => db.Warehouses.Add(warehouse);

    public void AddLocation(StockLocation location) => db.Locations.Add(location);

    public void AddTransfer(StockTransfer transfer) => db.Transfers.Add(transfer);

    public void AddCount(StockCount count)
    {
        db.Counts.Add(count);
        foreach (var line in count.Items)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public StockCount? FindCount(string countId) =>
        ScopedCounts().Where(c => c.Id == countId).Include(c => c.Items).FirstOrDefault(); // count_lines is an owned table

    public void MarkCountClosed(StockCount count, string closedBy, DateTimeOffset closedAt)
    {
        Set(count, nameof(StockCount.Status), CountStatus.Closed);
        Set(count, nameof(StockCount.ClosedBy), closedBy);
        Set(count, nameof(StockCount.ClosedAt), closedAt);
    }

    public void AddAdjustment(StockAdjustment adjustment) => db.Adjustments.Add(adjustment);

    public decimal QtyAt(string locationId, string itemSku) =>
        StockScoped().Where(s => s.LocationId == locationId && s.ItemSku == itemSku).Sum(s => (decimal?)s.QtyOnHand) ?? 0m;

    public decimal QtyOf(string itemSku) =>
        StockScoped().Where(s => s.ItemSku == itemSku).Sum(s => (decimal?)s.QtyOnHand) ?? 0m;

    public void ApplyStock(string warehouseId, string locationId, string itemSku, decimal delta)
    {
        var row = StockScoped().FirstOrDefault(s => s.LocationId == locationId && s.ItemSku == itemSku);
        if (row is not null)
            Set(row, nameof(StockLevel.QtyOnHand), row.QtyOnHand + delta);
        else
            db.Stock.Add(new StockLevel(ec.TenantId, ec.EntityId, warehouseId, locationId, itemSku, delta));
    }

    public void DrawStock(string itemSku, decimal qty)
    {
        var remaining = qty;
        // deterministic drain order — the default receiving location (LOC-A1) sorts first, matching
        // the in-memory insertion order
        foreach (var row in StockScoped().Where(s => s.ItemSku == itemSku && s.QtyOnHand > 0)
            .OrderBy(s => s.WarehouseId).ThenBy(s => s.LocationId).ToList())
        {
            var take = Math.Min(row.QtyOnHand, remaining);
            ApplyStock(row.WarehouseId, row.LocationId, itemSku, -take);
            remaining -= take;
            if (remaining <= 0) break;
        }
    }

    public void SetStockQty(StockLevel row, decimal qty) =>
        Set(row, nameof(StockLevel.QtyOnHand), qty);

    public StockLocation DefaultReceivingLocation() =>
        LocationsScoped().FirstOrDefault(l => l.Id == "LOC-A1") // task-1 seed: the receiving dock
        ?? LocationsScoped().OrderBy(l => l.Id).First(); // LOC-A1 never drifted silently again (T3): created ids (LOC-00xx) used to sort first and steal receiving

    public int Save() => db.SaveChanges();

    private IQueryable<StockCount> ScopedCounts() =>
        db.Counts.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
