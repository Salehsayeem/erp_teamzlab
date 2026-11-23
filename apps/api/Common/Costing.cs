using Erp.Api.Data;

namespace Erp.Api;

// Shared by Sales (COGS) and Inventory (valuation) — single source for weighted-average unit cost
public static class Costing
{
    // weighted-average receipt cost per unit — single source for COGS and valuation (0 when never received)
    public static decimal AverageUnitCost(Db db, string itemSku)
    {
        var receipts = db.Receipts.Where(r => r.TenantId == db.Tenant.Id && r.LegalEntityId == db.Entity.Id && r.ItemSku == itemSku).ToList();
        var totalQty = receipts.Sum(r => r.Qty);
        return totalQty > 0
            ? decimal.Round(receipts.Sum(r => r.Qty * ReceiptUnitCost(db, r.PurchaseOrderId, itemSku)) / totalQty, 4, MidpointRounding.AwayFromZero)
            : 0m;
    }

    private static decimal ReceiptUnitCost(Db db, string poId, string itemSku) =>
        db.PurchaseOrders.First(p => p.TenantId == db.Tenant.Id && p.LegalEntityId == db.Entity.Id && p.Id == poId)
            .Lines.First(l => l.ItemSku == itemSku).UnitPrice;
}
