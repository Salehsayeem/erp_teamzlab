using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

public sealed class ReceiptService(
    IPurchaseOrderRepository orders, IInventoryRepository inventory, IJournalRepository journals,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : IReceiptService
{
    // deferred PROCUREMENT wave: tenant-scoped receipt list for the receipt workspace
    public object List() =>
        db.Receipts.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.PurchaseOrderId, r.ItemSku, r.Qty, r.ReceivedAt, r.ReceivedBy })
            .ToList();

    public (Receipt? receipt, Journal? journal, string? code, string? message) Receive(
        CurrentUser user, string poId, string itemSku, decimal qty, string? toleranceOverrideReason = null)
    {
        var po = orders.Find(poId);
        if (po is null) return (null, null, "PO_NOT_FOUND", "Purchase order not found.");
        if (po.Status is not (PoStatus.Approved or PoStatus.Sent)) return (null, null, "RECEIPT_ERROR", $"PO must be approved or sent before receiving (current: {po.Status}).");
        if (qty <= 0) return (null, null, "VALIDATION_ERROR", "Receipt qty must be greater than 0.");
        var line = po.Lines.FirstOrDefault(l => l.ItemSku == itemSku);
        if (line is null) return (null, null, "ITEM_NOT_FOUND", $"Item {itemSku} is not on this purchase order.");

        // task-5: receipts post the AP-accrual journal at today — the centralized period lock applies
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, null, guardCode, guardMsg);

        // finding 2: over-receipt is unbounded → reject before any receipt/journal is written
        // (ERP-PROCUREMENT-057). The guard is an AGGREGATE read (ReceivedQty) — read-then-write:
        // two concurrent receives both read the same already and BOTH pass. Receive is multi-write
        // (receipt + stock + AP journal + PO flip), so check+commit are serialized per PO row:
        // SELECT … FOR UPDATE inside the request transaction (joined, never nested, when
        // Idem.Wrap opened one), then the AUTHORITATIVE re-check under the lock (journal-race
        // hardening; same mechanism as JournalService.Reverse / the invoice exposure path).
        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        db.PurchaseOrders
            .FromSql($"SELECT * FROM purchase_orders WHERE id = {poId} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId} FOR UPDATE")
            .ToList();
        var already = orders.ReceivedQty(poId, itemSku);
        if (already + qty > line.Qty)
        {
            // 057 AC1: over open qty is blocked WITHOUT an audited tolerance override; with one the
            // receipt proceeds and the override is recorded (receipts:write carries the override)
            if (string.IsNullOrWhiteSpace(toleranceOverrideReason))
                return (null, null, "OVER_RECEIVE", $"Over-receipt blocked for {itemSku}: already received {already} of {line.Qty} ordered, cannot receive {qty} more.");
            audit.Log(user, "tolerance-override", "receipt", poId,
                $"Tolerance override by {user.Email}: receiving {qty} of {itemSku} (already {already}/{line.Qty}): {toleranceOverrideReason}");
        }

        var receipt = new Receipt(numbers.Next("RC"), ec.TenantId, ec.EntityId, poId, itemSku, qty, DateTimeOffset.UtcNow, user.Id);
        orders.AddReceipt(receipt);

        // stock upsert at the default receiving location
        var loc = inventory.DefaultReceivingLocation();
        inventory.ApplyStock(loc.WarehouseId, loc.Id, itemSku, qty);

        var amount = decimal.Round(qty * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
            $"AP accrual for receipt {receipt.Id} (PO {poId})",
            new JournalLine[] { new("1400", "Inventory", amount, 0), new("2100", "Accounts Payable", 0, amount) },
            DateTimeOffset.UtcNow, user.Id);
        journals.Add(journal);

        // every line received? flip PO status
        var receivedBySku = orders.ReceivedBySku(poId);
        if (po.Lines.All(l => receivedBySku.TryGetValue(l.ItemSku, out var got) && got >= l.Qty))
            orders.MarkReceived(po);

        audit.Log(user, "receive", "receipt", receipt.Id, $"Received {qty} x {itemSku} against {poId}; journal {journal.Id} posted");
        audit.Emit(user, "procurement.receipt.posted", new { receiptId = receipt.Id, poId, itemSku, qty, journalId = journal.Id });
        // PO update + stock + journal + receipt + audit + outbox commit in ONE SaveChanges (task-2 §4)
        orders.Save();
        if (ownsTx) tx!.Commit();
        return (receipt, journal, null, null);
    }
}
