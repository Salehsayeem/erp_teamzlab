using Erp.Api.Data;

namespace Erp.Api;

// Location-scoped stock balance helpers — shared by Receipt/Sales/Inventory services;
// every mutation runs inside Db.WithLock at the call site
public static class Stock
{
    public static decimal QtyAt(Db db, string locationId, string itemSku) =>
        db.Stock.Where(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id
            && s.LocationId == locationId && s.ItemSku == itemSku).Sum(s => s.QtyOnHand);

    public static decimal QtyOf(Db db, string itemSku) =>
        db.Stock.Where(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.ItemSku == itemSku).Sum(s => s.QtyOnHand);

    public static void Apply(Db db, string warehouseId, string locationId, string itemSku, decimal delta)
    {
        var idx = db.Stock.FindIndex(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id
            && s.LocationId == locationId && s.ItemSku == itemSku);
        if (idx >= 0) db.Stock[idx] = db.Stock[idx] with { QtyOnHand = db.Stock[idx].QtyOnHand + delta };
        else db.Stock.Add(new StockLevel(db.Tenant.Id, db.Entity.Id, warehouseId, locationId, itemSku, delta));
    }

    // draws qty from the first rows that hold it (default receiving location drains first)
    public static void Draw(Db db, string itemSku, decimal qty)
    {
        var remaining = qty;
        foreach (var row in db.Stock.Where(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.ItemSku == itemSku && s.QtyOnHand > 0).ToList())
        {
            var take = Math.Min(row.QtyOnHand, remaining);
            Apply(db, row.WarehouseId, row.LocationId, itemSku, -take);
            remaining -= take;
            if (remaining <= 0) break;
        }
    }
}
