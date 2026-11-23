using Erp.Api.Data;

namespace Erp.Api;

// Pilot-slice business logic (02_AppFlow §3.1 procure-to-receive; 04_Data_Model posted-fact rules)

public static class Audit
{
    public static void Log(Db db, CurrentUser user, string action, string entityType, string entityId, string summary) =>
        db.Audit.Add(new AuditEvent(db.NextDoc("A"), db.Tenant.Id, user.Id, action, entityType, entityId, DateTimeOffset.UtcNow, summary));

    public static void Emit(Db db, CurrentUser user, string type, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        db.Outbox.Add(new OutboxMessage(db.NextDoc("E"), db.Tenant.Id, type, 1, DateTimeOffset.UtcNow, user.Id, json));
    }
}

public static class Guards
{
    public static string? Check(string? value, string field, int max = 200) =>
        string.IsNullOrWhiteSpace(value) ? $"{field} is required." :
        value.Length > max ? $"{field} must be at most {max} characters." : null;

    public static string? CheckEmail(string? value) =>
        Check(value, "Email") is not null and var e ? e :
        value!.Contains('@', StringComparison.Ordinal) ? null : "Email must be valid.";

    public static string? CheckLines(IReadOnlyList<PoLine>? lines)
    {
        if (lines is null || lines.Count == 0) return "At least one order line is required.";
        return lines.Any(l => l.Qty <= 0 || l.UnitPrice < 0) ? "Line qty must be > 0 and unit price must be >= 0." : null;
    }

    public static string? CheckLines(IReadOnlyList<SoLine>? lines)
    {
        if (lines is null || lines.Count == 0) return "At least one order line is required.";
        return lines.Any(l => l.Qty <= 0 || l.UnitPrice < 0) ? "Line qty must be > 0 and unit price must be >= 0." : null;
    }

    public static string? CheckCountItems(IReadOnlyList<CountItemReq>? items)
    {
        if (items is null || items.Count == 0) return "At least one count line is required.";
        if (items.Any(i => string.IsNullOrWhiteSpace(i.ItemSku))) return "Each count line needs an item SKU.";
        if (items.Any(i => i.CountedQty < 0)) return "Counted qty must be >= 0.";
        return items.GroupBy(i => i.ItemSku).Any(g => g.Count() > 1) ? "Duplicate item lines are not allowed in one count." : null;
    }
}

// Location-scoped stock balance helpers — every mutation runs inside Db.WithLock at the call site
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

public static class PoService
{
    public static PurchaseOrder Create(Db db, CurrentUser user, string supplierId, IReadOnlyList<PoLine> lines)
    {
        var po = new PurchaseOrder(db.NextDoc("PO"), db.Tenant.Id, db.Entity.Id, supplierId, PoStatus.Draft, lines, user.Id, DateTimeOffset.UtcNow);
        db.PurchaseOrders.Add(po);
        Audit.Log(db, user, "create", "purchase_order", po.Id, $"PO created for supplier {supplierId} with {lines.Count} line(s)");
        return po;
    }

    public static (PurchaseOrder? po, string? code, string? error) Approve(Db db, CurrentUser approver, string poId, string? reason)
    {
        var po = db.PurchaseOrders.FirstOrDefault(p => p.TenantId == db.Tenant.Id && p.LegalEntityId == db.Entity.Id && p.Id == poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (po.Status != PoStatus.PendingApproval) return (null, "PO_APPROVAL_ERROR", $"Only pending POs can be approved (current: {po.Status}).");
        if (po.CreatedBy == approver.Id) return (null, "PO_APPROVAL_ERROR", "Maker-checker: the creator cannot approve their own purchase order.");
        if (!approver.Has("po:approve")) return (null, "FORBIDDEN", "po:approve permission required.");

        var approved = reason is null
            ? po with { Status = PoStatus.Approved, ApprovedBy = approver.Id, ApprovedAt = DateTimeOffset.UtcNow }
            : po with { Status = PoStatus.Rejected, ApprovedBy = approver.Id, ApprovedAt = DateTimeOffset.UtcNow, RejectionReason = reason };
        db.PurchaseOrders[db.PurchaseOrders.IndexOf(po)] = approved;
        Audit.Log(db, approver, reason is null ? "approve" : "reject", "purchase_order", po.Id,
            reason is null ? $"PO approved by {approver.Email}" : $"PO rejected: {reason}");
        if (reason is null) Audit.Emit(db, approver, "procurement.po.approved", new { poId = po.Id, approver = approver.Id });
        return (approved, null, null);
    }
}

public static class ReceiptService
{
    public static (Receipt? receipt, Journal? journal, string? code, string? error) Receive(
        Db db, CurrentUser user, string poId, string itemSku, decimal qty)
    {
        var po = db.PurchaseOrders.FirstOrDefault(p => p.TenantId == db.Tenant.Id && p.LegalEntityId == db.Entity.Id && p.Id == poId);
        if (po is null) return (null, null, "PO_NOT_FOUND", "Purchase order not found.");
        if (po.Status is not (PoStatus.Approved or PoStatus.Sent)) return (null, null, "RECEIPT_ERROR", $"PO must be approved or sent before receiving (current: {po.Status}).");
        if (qty <= 0) return (null, null, "VALIDATION_ERROR", "Receipt qty must be greater than 0.");
        var line = po.Lines.FirstOrDefault(l => l.ItemSku == itemSku);
        if (line is null) return (null, null, "ITEM_NOT_FOUND", $"Item {itemSku} is not on this purchase order.");

        // finding 2: over-receipt is unbounded → reject before any receipt/journal is written (ERP-PROCUREMENT-057)
        var already = db.Receipts
            .Where(r => r.TenantId == db.Tenant.Id && r.LegalEntityId == db.Entity.Id && r.PurchaseOrderId == poId && r.ItemSku == itemSku)
            .Sum(r => r.Qty);
        if (already + qty > line.Qty)
            return (null, null, "OVER_RECEIVE", $"Over-receipt blocked for {itemSku}: already received {already} of {line.Qty} ordered, cannot receive {qty} more.");

        var receipt = new Receipt(db.NextDoc("RC"), db.Tenant.Id, db.Entity.Id, poId, itemSku, qty, DateTimeOffset.UtcNow, user.Id);
        db.Receipts.Add(receipt);

        // stock upsert at the default receiving location
        var loc = db.DefaultLocation;
        Stock.Apply(db, loc.WarehouseId, loc.Id, itemSku, qty);

        var amount = decimal.Round(qty * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var journal = new Journal(db.NextDoc("J"), db.Tenant.Id, db.Entity.Id, JournalStatus.Posted,
            $"AP accrual for receipt {receipt.Id} (PO {poId})",
            [new JournalLine("1400", "Inventory", amount, 0), new JournalLine("2100", "Accounts Payable", 0, amount)],
            DateTimeOffset.UtcNow, user.Id);
        db.Journals.Add(journal);

        // every line received? flip PO status
        var receivedBySku = db.Receipts.Where(r => r.TenantId == db.Tenant.Id && r.LegalEntityId == db.Entity.Id && r.PurchaseOrderId == poId).GroupBy(r => r.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        if (po.Lines.All(l => receivedBySku.TryGetValue(l.ItemSku, out var got) && got >= l.Qty))
            db.PurchaseOrders[db.PurchaseOrders.IndexOf(po)] = po with { Status = PoStatus.Received };

        Audit.Log(db, user, "receive", "receipt", receipt.Id, $"Received {qty} x {itemSku} against {poId}; journal {journal.Id} posted");
        Audit.Emit(db, user, "procurement.receipt.posted", new { receiptId = receipt.Id, poId, itemSku, qty, journalId = journal.Id });
        return (receipt, journal, null, null);
    }
}

public static class SalesService
{
    public static SalesOrder Create(Db db, CurrentUser user, string customerId, IReadOnlyList<SoLine> lines)
    {
        var so = new SalesOrder(db.NextDoc("SO"), db.Tenant.Id, db.Entity.Id, customerId, SoStatus.Draft, lines, user.Id, DateTimeOffset.UtcNow);
        db.SalesOrders.Add(so);
        Audit.Log(db, user, "create", "sales_order", so.Id, $"SO created for customer {customerId} with {lines.Count} line(s)");
        return so;
    }

    public static (SalesOrder? so, string? code, string? error) Approve(Db db, CurrentUser approver, string soId, string? reason)
    {
        var so = db.SalesOrders.FirstOrDefault(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.Id == soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.PendingApproval) return (null, "SO_APPROVAL_ERROR", $"Only pending sales orders can be approved (current: {so.Status}).");
        if (so.CreatedBy == approver.Id) return (null, "SO_APPROVAL_ERROR", "Maker-checker: the creator cannot approve their own sales order.");
        if (!approver.Has("so:write") && !approver.Has("*")) return (null, "FORBIDDEN", "so:write permission required.");

        var approved = reason is null
            ? so with { Status = SoStatus.Confirmed, ApprovedBy = approver.Id, ApprovedAt = DateTimeOffset.UtcNow }
            : so with { Status = SoStatus.Rejected, ApprovedBy = approver.Id, ApprovedAt = DateTimeOffset.UtcNow, RejectionReason = reason };
        db.SalesOrders[db.SalesOrders.IndexOf(so)] = approved;
        Audit.Log(db, approver, reason is null ? "approve" : "reject", "sales_order", so.Id,
            reason is null ? $"SO approved by {approver.Email}" : $"SO rejected: {reason}");
        if (reason is null) Audit.Emit(db, approver, "sales.so.approved", new { soId = so.Id, approver = approver.Id });
        return (approved, null, null);
    }

    public static (Shipment? shipment, Journal? journal, string? code, string? error) Ship(
        Db db, CurrentUser user, string soId, string itemSku, decimal qty)
    {
        var so = db.SalesOrders.FirstOrDefault(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.Id == soId);
        if (so is null) return (null, null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Confirmed) return (null, null, "SHIPMENT_ERROR", $"Only confirmed sales orders can be shipped (current: {so.Status}).");
        if (qty <= 0) return (null, null, "VALIDATION_ERROR", "Shipment qty must be greater than 0.");
        if (so.Lines.FirstOrDefault(l => l.ItemSku == itemSku) is null) return (null, null, "ITEM_NOT_FOUND", $"Item {itemSku} is not on this sales order.");

        var onHand = Stock.QtyOf(db, itemSku);
        if (onHand < qty)
            return (null, null, "INSUFFICIENT_STOCK", $"Insufficient stock for {itemSku}: available {onHand}, requested {qty}.");

        Stock.Draw(db, itemSku, qty);

        var shipment = new Shipment(db.NextDoc("SHP"), db.Tenant.Id, db.Entity.Id, soId, itemSku, qty, DateTimeOffset.UtcNow, user.Id);
        db.Shipments.Add(shipment);

        // COGS at inventory cost basis — weighted average of receipt PO unit prices (0 when no receipts yet)
        var unitCost = Costing.AverageUnitCost(db, itemSku);
        var amount = decimal.Round(unitCost * qty, 2, MidpointRounding.AwayFromZero);
        var journal = new Journal(db.NextDoc("J"), db.Tenant.Id, db.Entity.Id, JournalStatus.Posted,
            $"COGS for shipment {shipment.Id} (SO {soId})",
            [new JournalLine("5100", "Cost of Goods Sold", amount, 0), new JournalLine("1400", "Inventory", 0, amount)],
            DateTimeOffset.UtcNow, user.Id);
        db.Journals.Add(journal);

        // all lines shipped? flip SO status
        var shippedBySku = db.Shipments.Where(sh => sh.TenantId == db.Tenant.Id && sh.LegalEntityId == db.Entity.Id && sh.SalesOrderId == soId).GroupBy(sh => sh.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        if (so.Lines.All(l => shippedBySku.TryGetValue(l.ItemSku, out var got) && got >= l.Qty))
            db.SalesOrders[db.SalesOrders.IndexOf(so)] = so with { Status = SoStatus.Shipped };

        Audit.Log(db, user, "ship", "shipment", shipment.Id, $"Shipped {qty} x {itemSku} against {soId}; journal {journal.Id} posted");
        Audit.Emit(db, user, "sales.shipment.posted", new { shipmentId = shipment.Id, soId, itemSku, qty, journalId = journal.Id });
        return (shipment, journal, null, null);
    }

    // shortcut: approval gate = SO approval; separate invoice approval when spec card demands it
    public static (Journal? journal, string? code, string? error) Invoice(Db db, CurrentUser user, string soId)
    {
        var so = db.SalesOrders.FirstOrDefault(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.Id == soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Shipped) return (null, "INVOICE_ERROR", $"Only shipped sales orders can be invoiced (current: {so.Status}).");

        var total = so.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var journal = new Journal(db.NextDoc("J"), db.Tenant.Id, db.Entity.Id, JournalStatus.Posted,
            $"AR invoice for SO {soId}",
            [new JournalLine("1200", "Accounts Receivable", total, 0), new JournalLine("4000", "Sales Revenue", 0, total)],
            DateTimeOffset.UtcNow, user.Id);
        db.Journals.Add(journal);
        db.SalesOrders[db.SalesOrders.IndexOf(so)] = so with { Status = SoStatus.Invoiced };

        Audit.Log(db, user, "invoice", "sales_order", so.Id, $"SO invoiced for {total}; journal {journal.Id} posted");
        Audit.Emit(db, user, "sales.invoice.posted", new { soId, total, journalId = journal.Id });
        return (journal, null, null);
    }
}

public static class JournalService
{
    public static (Journal? reversal, string? code, string? error) Reverse(Db db, CurrentUser user, string journalId)
    {
        var j = db.Journals.FirstOrDefault(x => x.TenantId == db.Tenant.Id && x.LegalEntityId == db.Entity.Id && x.Id == journalId);
        if (j is null) return (null, "JOURNAL_NOT_FOUND", "Journal not found.");
        if (j.Status != JournalStatus.Posted) return (null, "JOURNAL_REVERSAL_ERROR", "Only posted journals can be reversed.");
        if (j.ReversalOfId is not null) return (null, "JOURNAL_REVERSAL_ERROR", "A reversal journal cannot be reversed.");
        if (!user.Has("journals:approve")) return (null, "FORBIDDEN", "journals:approve permission required.");

        var lines = j.Lines.Select(l => new JournalLine(l.AccountCode, l.AccountName, l.Credit, l.Debit)).ToList();
        var rev = new Journal(db.NextDoc("J"), j.TenantId, j.LegalEntityId, JournalStatus.Posted,
            $"Reversal of {j.Id}: {j.Memo}", lines, DateTimeOffset.UtcNow, user.Id, j.Id);
        db.Journals.Add(rev);

        // finding 4: receipt-derived journals must unwind their stock increment (floor 0) + emit reversal event
        var receipt = db.Receipts.FirstOrDefault(r => r.TenantId == j.TenantId && r.LegalEntityId == j.LegalEntityId
            && j.Memo.Contains(r.Id, StringComparison.Ordinal));
        var stockNote = "";
        if (receipt is not null)
        {
            var idx = db.Stock.FindIndex(s => s.TenantId == receipt.TenantId && s.LegalEntityId == receipt.LegalEntityId && s.ItemSku == receipt.ItemSku);
            if (idx >= 0)
            {
                var remaining = Math.Max(0m, db.Stock[idx].QtyOnHand - receipt.Qty);
                db.Stock[idx] = db.Stock[idx] with { QtyOnHand = remaining };
                stockNote = $"; stock {receipt.ItemSku} adjusted to {remaining}";
            }
        }

        Audit.Emit(db, user, "finance.journal.reversed", new { journalId = j.Id, reversalId = rev.Id, receiptId = receipt?.Id });
        Audit.Log(db, user, "reverse", "journal", rev.Id, $"Journal {j.Id} reversed by {user.Email} (linked provenance){stockNote}");
        return (rev, null, null);
    }
}

// Inventory deep slice (ERP-INVENTORY-005 locations, -014 atomic movement, -020 negative-stock block)
public static class InventoryService
{
    private static StockLocation? Find(Db db, string locationId) =>
        db.Locations.FirstOrDefault(l => l.TenantId == db.Tenant.Id && l.LegalEntityId == db.Entity.Id && l.Id == locationId);

    public static (Warehouse? warehouse, string? code, string? error) CreateWarehouse(Db db, CurrentUser user, string code, string name)
    {
        if (db.Warehouses.Any(w => w.TenantId == db.Tenant.Id && w.LegalEntityId == db.Entity.Id && w.Code == code))
            return (null, "WAREHOUSE_EXISTS", $"Warehouse code {code} already exists.");
        var wh = new Warehouse(db.NextDoc("WH"), db.Tenant.Id, db.Entity.Id, code, name);
        db.Warehouses.Add(wh);
        Audit.Log(db, user, "create", "warehouse", wh.Id, $"Warehouse '{code}' created");
        return (wh, null, null);
    }

    public static (StockLocation? location, string? code, string? error) CreateLocation(Db db, CurrentUser user, string warehouseId, string code, string name)
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

    public static (StockTransfer? transfer, string? code, string? error) Transfer(Db db, CurrentUser user, string fromLocationId, string toLocationId, string itemSku, decimal qty)
    {
        if (fromLocationId == toLocationId) return (null, "VALIDATION_ERROR", "Source and destination locations must differ.");
        var from = Find(db, fromLocationId);
        if (from is null) return (null, "LOCATION_NOT_FOUND", "Source location not found.");
        var to = Find(db, toLocationId);
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

    public static (StockCount? count, string? code, string? error) OpenCount(Db db, CurrentUser user, string locationId, IReadOnlyList<CountItemReq> items)
    {
        var loc = Find(db, locationId);
        if (loc is null) return (null, "LOCATION_NOT_FOUND", "Location not found.");

        // snapshot system qty at open — variance at close is measured against this frozen basis
        var count = new StockCount(db.NextDoc("CNT"), db.Tenant.Id, db.Entity.Id, loc.Id, CountStatus.Open,
            items.Select(i => new CountLine(i.ItemSku, i.CountedQty, Stock.QtyAt(db, loc.Id, i.ItemSku))).ToList(),
            user.Id, DateTimeOffset.UtcNow);
        db.Counts.Add(count);
        Audit.Log(db, user, "open", "stock_count", count.Id, $"Count opened at {loc.Code} for {items.Count} item(s)");
        return (count, null, null);
    }

    public static (StockCount? count, IReadOnlyList<StockAdjustment>? adjustments, string? code, string? error) Close(Db db, CurrentUser closer, string countId)
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
    public static object Valuation(Db db) =>
        db.Stock.Where(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id)
            .GroupBy(s => s.ItemSku)
            .Select(g =>
            {
                var qty = g.Sum(s => s.QtyOnHand);
                var unitCost = Costing.AverageUnitCost(db, g.Key);
                return new { itemSku = g.Key, qtyOnHand = qty, unitCost, value = decimal.Round(qty * unitCost, 2, MidpointRounding.AwayFromZero) };
            })
            .ToList();
}
