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

        // stock upsert
        var idx = db.Stock.FindIndex(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.ItemSku == itemSku);
        if (idx >= 0) db.Stock[idx] = db.Stock[idx] with { QtyOnHand = db.Stock[idx].QtyOnHand + qty };
        else db.Stock.Add(new StockLevel(db.Tenant.Id, db.Entity.Id, itemSku, qty));

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

        var idx = db.Stock.FindIndex(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.ItemSku == itemSku);
        var onHand = idx >= 0 ? db.Stock[idx].QtyOnHand : 0m;
        if (onHand < qty)
            return (null, null, "INSUFFICIENT_STOCK", $"Insufficient stock for {itemSku}: available {onHand}, requested {qty}.");

        db.Stock[idx] = db.Stock[idx] with { QtyOnHand = onHand - qty };

        var shipment = new Shipment(db.NextDoc("SHP"), db.Tenant.Id, db.Entity.Id, soId, itemSku, qty, DateTimeOffset.UtcNow, user.Id);
        db.Shipments.Add(shipment);

        // COGS at inventory cost basis — weighted average of receipt PO unit prices (0 when no receipts yet)
        var receipts = db.Receipts.Where(r => r.TenantId == db.Tenant.Id && r.LegalEntityId == db.Entity.Id && r.ItemSku == itemSku).ToList();
        var totalQty = receipts.Sum(r => r.Qty);
        var unitCost = totalQty > 0
            ? decimal.Round(receipts.Sum(r => r.Qty * CostOf(db, r.PurchaseOrderId, itemSku)) / totalQty, 4, MidpointRounding.AwayFromZero)
            : 0m;
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

    private static decimal CostOf(Db db, string poId, string itemSku) =>
        db.PurchaseOrders.First(p => p.TenantId == db.Tenant.Id && p.LegalEntityId == db.Entity.Id && p.Id == poId)
            .Lines.First(l => l.ItemSku == itemSku).UnitPrice;
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
