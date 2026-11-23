using Erp.Api.Interfaces;
using Erp.Api.Data;

namespace Erp.Api.Services;

public sealed class ReceiptService(Db db) : IReceiptService
{
    public (Receipt? receipt, Journal? journal, string? code, string? message) Receive(
        CurrentUser user, string poId, string itemSku, decimal qty)
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
