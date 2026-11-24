using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Inventory deep slice (ERP-INVENTORY-005 locations, -014 atomic movement, -020 negative-stock block)
public sealed class InventoryService(
    IInventoryRepository inventory, IPurchaseOrderRepository costing, IJournalRepository journals,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : IInventoryService
{
    // stock grouped by warehouse/location (ERP-INVENTORY-005) — flat rows keyed by location
    public object ListStock() => inventory.AllStock().Select(s =>
    {
        var wh = inventory.FindWarehouse(s.WarehouseId);
        var loc = inventory.FindLocation(s.LocationId);
        return new { warehouseId = wh!.Id, warehouseCode = wh.Code, locationId = loc!.Id, locationCode = loc.Code, s.ItemSku, s.QtyOnHand };
    }).ToList();

    public object ListWarehouses() => inventory.AllWarehouses();

    public object ListLocations() => inventory.AllLocations();

    public (Warehouse? warehouse, string? code, string? message) CreateWarehouse(CurrentUser user, string code, string name)
    {
        if (inventory.WarehouseCodeExists(code))
            return (null, "WAREHOUSE_EXISTS", $"Warehouse code {code} already exists.");
        var wh = new Warehouse(numbers.Next("WH"), ec.TenantId, ec.EntityId, code, name);
        inventory.AddWarehouse(wh);
        audit.Log(user, "create", "warehouse", wh.Id, $"Warehouse '{code}' created");
        inventory.Save();
        return (wh, null, null);
    }

    public (StockLocation? location, string? code, string? message) CreateLocation(CurrentUser user, string warehouseId, string code, string name)
    {
        var wh = inventory.FindWarehouse(warehouseId);
        if (wh is null) return (null, "WAREHOUSE_NOT_FOUND", "Warehouse not found.");
        if (inventory.LocationCodeExists(warehouseId, code))
            return (null, "LOCATION_EXISTS", $"Location code {code} already exists in warehouse {wh.Code}.");
        var loc = new StockLocation(numbers.Next("LOC"), ec.TenantId, ec.EntityId, warehouseId, code, name);
        inventory.AddLocation(loc);
        audit.Log(user, "create", "stock_location", loc.Id, $"Location '{code}' created in warehouse {wh.Code}");
        inventory.Save();
        return (loc, null, null);
    }

    public (StockTransfer? transfer, string? code, string? message) Transfer(
        CurrentUser user, string fromLocationId, string toLocationId, string itemSku, decimal qty)
    {
        if (fromLocationId == toLocationId) return (null, "VALIDATION_ERROR", "Source and destination locations must differ.");
        var from = inventory.FindLocation(fromLocationId);
        if (from is null) return (null, "LOCATION_NOT_FOUND", "Source location not found.");
        var to = inventory.FindLocation(toLocationId);
        if (to is null) return (null, "LOCATION_NOT_FOUND", "Destination location not found.");

        // ERP-INVENTORY-020: blocking policy — checked before either leg moves
        var onHand = inventory.QtyAt(from.Id, itemSku);
        if (onHand < qty)
            return (null, "INSUFFICIENT_STOCK", $"Insufficient stock for {itemSku} at {from.Code}: available {onHand}, requested {qty}.");

        // ERP-INVENTORY-014: both legs commit in ONE SaveChanges — all or nothing (task-2 §4)
        inventory.ApplyStock(from.WarehouseId, from.Id, itemSku, -qty);
        inventory.ApplyStock(to.WarehouseId, to.Id, itemSku, qty);

        var transfer = new StockTransfer(numbers.Next("TRF"), ec.TenantId, ec.EntityId, from.Id, to.Id, itemSku, qty, TransferStatus.Posted, DateTimeOffset.UtcNow, user.Id);
        inventory.AddTransfer(transfer);
        audit.Log(user, "transfer", "stock_transfer", transfer.Id, $"Transferred {qty} x {itemSku} from {from.Code} to {to.Code}");
        audit.Emit(user, "inventory.transfer.posted", new { transferId = transfer.Id, fromLocationId = from.Id, toLocationId = to.Id, itemSku, qty });
        inventory.Save();
        return (transfer, null, null);
    }

    public (StockCount? count, string? code, string? message) OpenCount(CurrentUser user, string locationId, IReadOnlyList<CountItemRequest> items)
    {
        var loc = inventory.FindLocation(locationId);
        if (loc is null) return (null, "LOCATION_NOT_FOUND", "Location not found.");

        // snapshot system qty at open — variance at close is measured against this frozen basis
        var count = new StockCount(numbers.Next("CNT"), ec.TenantId, ec.EntityId, loc.Id, CountStatus.Open,
            items.Select(i => new CountLine(i.ItemSku, i.CountedQty, inventory.QtyAt(loc.Id, i.ItemSku))).ToList(),
            user.Id, DateTimeOffset.UtcNow);
        inventory.AddCount(count);
        audit.Log(user, "open", "stock_count", count.Id, $"Count opened at {loc.Code} for {items.Count} item(s)");
        inventory.Save();
        return (count, null, null);
    }

    public (StockCount? count, IReadOnlyList<StockAdjustment>? adjustments, string? code, string? message) Close(CurrentUser closer, string countId)
    {
        var count = inventory.FindCount(countId);
        if (count is null) return (null, null, "COUNT_NOT_FOUND", "Stock count not found.");
        if (count.Status != CountStatus.Open) return (null, null, "COUNT_CLOSE_ERROR", $"Only open counts can be closed (current: {count.Status}).");
        if (count.CreatedBy == closer.Id) return (null, null, "COUNT_CLOSE_ERROR", "Maker-checker: the counter cannot close their own stock count.");
        if (!closer.Has("inventory:write")) return (null, null, "FORBIDDEN", "inventory:write permission required.");

        // task-5: count adjustments post journals at today — the centralized period lock applies
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, null, guardCode, guardMsg);

        var loc = inventory.FindLocation(count.LocationId)!;
        var adjustments = new List<StockAdjustment>();
        foreach (var line in count.Items.Where(l => l.CountedQty != l.SystemQty))
        {
            var variance = line.CountedQty - line.SystemQty;
            var unitCost = costing.AverageUnitCost(line.ItemSku);
            var amount = decimal.Round(Math.Abs(variance) * unitCost, 2, MidpointRounding.AwayFromZero);
            inventory.ApplyStock(loc.WarehouseId, loc.Id, line.ItemSku, variance);

            // balanced adjustment journal: increase Dr Inventory / Cr Count Adjustment, decrease reversed
            JournalLine[] lines = variance > 0
                ? [new JournalLine("1400", "Inventory", amount, 0), new JournalLine("3999", "Count Adjustment", 0, amount)]
                : [new JournalLine("3999", "Count Adjustment", amount, 0), new JournalLine("1400", "Inventory", 0, amount)];
            var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
                $"Stock count {count.Id} adjustment for {line.ItemSku}", lines, DateTimeOffset.UtcNow, closer.Id);
            journals.Add(journal);

            var adj = new StockAdjustment(numbers.Next("ADJ"), ec.TenantId, ec.EntityId, count.Id, line.ItemSku, loc.Id,
                variance, unitCost, amount, journal.Id, DateTimeOffset.UtcNow, closer.Id);
            inventory.AddAdjustment(adj);
            adjustments.Add(adj);
            audit.Emit(closer, "inventory.adjustment.posted", new { adjustmentId = adj.Id, countId = count.Id, itemSku = line.ItemSku, qtyVariance = variance, amount, journalId = journal.Id });
        }

        inventory.MarkCountClosed(count, closer.Id, DateTimeOffset.UtcNow);
        audit.Log(closer, "close", "stock_count", count.Id, $"Count closed by {closer.Email}; {adjustments.Count} adjustment(s) posted");
        // adjustments + journals + stock + count flip commit in ONE SaveChanges (task-2 §4)
        inventory.Save();
        return (count, adjustments, null, null);
    }

    // per-item weighted-average valuation: qty on hand across locations × average receipt unit cost
    public object Valuation() =>
        inventory.AllStock()
            .GroupBy(s => s.ItemSku)
            .Select(g =>
            {
                var qty = g.Sum(s => s.QtyOnHand);
                var unitCost = costing.AverageUnitCost(g.Key);
                return new { itemSku = g.Key, qtyOnHand = qty, unitCost, value = decimal.Round(qty * unitCost, 2, MidpointRounding.AwayFromZero) };
            })
            .ToList();
}
