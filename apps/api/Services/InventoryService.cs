using Erp.Api.Interfaces;
using Erp.Api.Data;
using Erp.Api.Dtos;

namespace Erp.Api.Services;

// Inventory deep slice (ERP-INVENTORY-005 locations, -014 atomic movement, -020 negative-stock block)
public sealed class InventoryService(Db db) : IInventoryService
{
    private StockLocation? Find(string locationId) =>
        db.Locations.FirstOrDefault(l => l.TenantId == db.Tenant.Id && l.LegalEntityId == db.Entity.Id && l.Id == locationId);

    // stock grouped by warehouse/location (ERP-INVENTORY-005) — flat rows keyed by location
    public object ListStock() => db.WithLock(() => db.Stock.Select(s =>
    {
        var wh = db.Warehouses.First(w => w.Id == s.WarehouseId);
        var loc = db.Locations.First(l => l.Id == s.LocationId);
        return new { warehouseId = wh.Id, warehouseCode = wh.Code, locationId = loc.Id, locationCode = loc.Code, s.ItemSku, s.QtyOnHand };
    }).ToList());

    public object ListWarehouses() => db.WithLock(() => db.Warehouses);

    public object ListLocations() => db.WithLock(() => db.Locations);

    public (Warehouse? warehouse, string? code, string? message) CreateWarehouse(CurrentUser user, string code, string name)
    {
        if (db.Warehouses.Any(w => w.TenantId == db.Tenant.Id && w.LegalEntityId == db.Entity.Id && w.Code == code))
            return (null, "WAREHOUSE_EXISTS", $"Warehouse code {code} already exists.");
        var wh = new Warehouse(db.NextDoc("WH"), db.Tenant.Id, db.Entity.Id, code, name);
        db.Warehouses.Add(wh);
        Audit.Log(db, user, "create", "warehouse", wh.Id, $"Warehouse '{code}' created");
        return (wh, null, null);
    }

    public (StockLocation? location, string? code, string? message) CreateLocation(CurrentUser user, string warehouseId, string code, string name)
    {
        var wh = db.Warehouses.FirstOrDefault(w => w.TenantId == db.Tenant.Id && w.LegalEntityId == db.Entity.Id && w.Id == warehouseId);
        if (wh is null) return (null, "WAREHOUSE_NOT_FOUND", "Warehouse not found.");
        if (db.Locations.Any(l => l.TenantId == db.Tenant.Id && l.LegalEntityId == db.Entity.Id && l.WarehouseId == warehouseId && l.Code == code))
            return (null, "LOCATION_EXISTS", $"Location code {code} already exists in warehouse {wh.Code}.");
        var loc = new StockLocation(db.NextDoc("LOC"), db.Tenant.Id, db.Entity.Id, warehouseId, code, name);
        db.Locations.Add(loc);
        Audit.Log(db, user, "create", "stock_location", loc.Id, $"Location '{code}' created in warehouse {wh.Code}");
        return (loc, null, null);
    }

    public (StockTransfer? transfer, string? code, string? message) Transfer(
        CurrentUser user, string fromLocationId, string toLocationId, string itemSku, decimal qty)
    {
        if (fromLocationId == toLocationId) return (null, "VALIDATION_ERROR", "Source and destination locations must differ.");
        var from = Find(fromLocationId);
        if (from is null) return (null, "LOCATION_NOT_FOUND", "Source location not found.");
        var to = Find(toLocationId);
        if (to is null) return (null, "LOCATION_NOT_FOUND", "Destination location not found.");

        // ERP-INVENTORY-020: blocking policy — check inside the caller's WithLock so no oversell slips through
        var onHand = Stock.QtyAt(db, from.Id, itemSku);
        if (onHand < qty)
            return (null, "INSUFFICIENT_STOCK", $"Insufficient stock for {itemSku} at {from.Code}: available {onHand}, requested {qty}.");

        // ERP-INVENTORY-014: both legs commit inside one lock — all or nothing
        Stock.Apply(db, from.WarehouseId, from.Id, itemSku, -qty);
        Stock.Apply(db, to.WarehouseId, to.Id, itemSku, qty);

        var transfer = new StockTransfer(db.NextDoc("TRF"), db.Tenant.Id, db.Entity.Id, from.Id, to.Id, itemSku, qty, TransferStatus.Posted, DateTimeOffset.UtcNow, user.Id);
        db.Transfers.Add(transfer);
        Audit.Log(db, user, "transfer", "stock_transfer", transfer.Id, $"Transferred {qty} x {itemSku} from {from.Code} to {to.Code}");
        Audit.Emit(db, user, "inventory.transfer.posted", new { transferId = transfer.Id, fromLocationId = from.Id, toLocationId = to.Id, itemSku, qty });
        return (transfer, null, null);
    }

    public (StockCount? count, string? code, string? message) OpenCount(CurrentUser user, string locationId, IReadOnlyList<CountItemRequest> items)
    {
        var loc = Find(locationId);
        if (loc is null) return (null, "LOCATION_NOT_FOUND", "Location not found.");

        // snapshot system qty at open — variance at close is measured against this frozen basis
        var count = new StockCount(db.NextDoc("CNT"), db.Tenant.Id, db.Entity.Id, loc.Id, CountStatus.Open,
            items.Select(i => new CountLine(i.ItemSku, i.CountedQty, Stock.QtyAt(db, loc.Id, i.ItemSku))).ToList(),
            user.Id, DateTimeOffset.UtcNow);
        db.Counts.Add(count);
        Audit.Log(db, user, "open", "stock_count", count.Id, $"Count opened at {loc.Code} for {items.Count} item(s)");
        return (count, null, null);
    }

    public (StockCount? count, IReadOnlyList<StockAdjustment>? adjustments, string? code, string? message) Close(CurrentUser closer, string countId)
    {
        var count = db.Counts.FirstOrDefault(c => c.TenantId == db.Tenant.Id && c.LegalEntityId == db.Entity.Id && c.Id == countId);
        if (count is null) return (null, null, "COUNT_NOT_FOUND", "Stock count not found.");
        if (count.Status != CountStatus.Open) return (null, null, "COUNT_CLOSE_ERROR", $"Only open counts can be closed (current: {count.Status}).");
        if (count.CreatedBy == closer.Id) return (null, null, "COUNT_CLOSE_ERROR", "Maker-checker: the counter cannot close their own stock count.");
        if (!closer.Has("inventory:write")) return (null, null, "FORBIDDEN", "inventory:write permission required.");

        var loc = db.Locations.First(l => l.Id == count.LocationId);
        var adjustments = new List<StockAdjustment>();
        foreach (var line in count.Items.Where(l => l.CountedQty != l.SystemQty))
        {
            var variance = line.CountedQty - line.SystemQty;
            var unitCost = Costing.AverageUnitCost(db, line.ItemSku);
            var amount = decimal.Round(Math.Abs(variance) * unitCost, 2, MidpointRounding.AwayFromZero);
            Stock.Apply(db, loc.WarehouseId, loc.Id, line.ItemSku, variance);

            // balanced adjustment journal: increase Dr Inventory / Cr Count Adjustment, decrease reversed
            JournalLine[] lines = variance > 0
                ? [new JournalLine("1400", "Inventory", amount, 0), new JournalLine("3999", "Count Adjustment", 0, amount)]
                : [new JournalLine("3999", "Count Adjustment", amount, 0), new JournalLine("1400", "Inventory", 0, amount)];
            var journal = new Journal(db.NextDoc("J"), db.Tenant.Id, db.Entity.Id, JournalStatus.Posted,
                $"Stock count {count.Id} adjustment for {line.ItemSku}", lines, DateTimeOffset.UtcNow, closer.Id);
            db.Journals.Add(journal);

            var adj = new StockAdjustment(db.NextDoc("ADJ"), db.Tenant.Id, db.Entity.Id, count.Id, line.ItemSku, loc.Id,
                variance, unitCost, amount, journal.Id, DateTimeOffset.UtcNow, closer.Id);
            db.Adjustments.Add(adj);
            adjustments.Add(adj);
            Audit.Emit(db, closer, "inventory.adjustment.posted", new { adjustmentId = adj.Id, countId = count.Id, itemSku = line.ItemSku, qtyVariance = variance, amount, journalId = journal.Id });
        }

        var closed = count with { Status = CountStatus.Closed, ClosedBy = closer.Id, ClosedAt = DateTimeOffset.UtcNow };
        db.Counts[db.Counts.IndexOf(count)] = closed;
        Audit.Log(db, closer, "close", "stock_count", count.Id, $"Count closed by {closer.Email}; {adjustments.Count} adjustment(s) posted");
        return (closed, adjustments, null, null);
    }

    // per-item weighted-average valuation: qty on hand across locations × average receipt unit cost
    public object Valuation() =>
        db.WithLock(() => db.Stock.Where(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id)
            .GroupBy(s => s.ItemSku)
            .Select(g =>
            {
                var qty = g.Sum(s => s.QtyOnHand);
                var unitCost = Costing.AverageUnitCost(db, g.Key);
                return new { itemSku = g.Key, qtyOnHand = qty, unitCost, value = decimal.Round(qty * unitCost, 2, MidpointRounding.AwayFromZero) };
            })
            .ToList());
}
