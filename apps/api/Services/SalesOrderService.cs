using Erp.Api.Interfaces;
using Erp.Api.Data;

namespace Erp.Api.Services;

public sealed class SalesOrderService(Db db) : ISalesOrderService
{
    public object List() =>
        db.WithLock(() => db.SalesOrders.Select(s => new
        {
            s.Id, s.CustomerId, s.Status, s.Lines, s.CreatedBy, s.CreatedAt, s.ApprovedBy, s.ApprovedAt, s.RejectionReason,
            total = s.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
        }).ToList());

    public (SalesOrder? order, string? code, string? message) Create(CurrentUser u, string customerId, IReadOnlyList<SoLine> lines) =>
        db.WithLock(() =>
        {
            if (!db.Customers.Any(c => c.TenantId == db.Tenant.Id && c.LegalEntityId == db.Entity.Id && c.Id == customerId))
                return ((SalesOrder?)null, (string?)"CUSTOMER_NOT_FOUND", (string?)"Customer does not exist.");
            var so = new SalesOrder(db.NextDoc("SO"), db.Tenant.Id, db.Entity.Id, customerId, SoStatus.Draft, lines, u.Id, DateTimeOffset.UtcNow);
            db.SalesOrders.Add(so);
            Audit.Log(db, u, "create", "sales_order", so.Id, $"SO created for customer {customerId} with {lines.Count} line(s)");
            return ((SalesOrder?)so, (string?)null, (string?)null);
        });

    public (SalesOrder? order, string? code, string? message) Submit(CurrentUser u, string id) =>
        db.WithLock(() =>
        {
            var so = db.SalesOrders.FirstOrDefault(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.Id == id);
            if (so is null) return ((SalesOrder?)null, (string?)"SO_NOT_FOUND", (string?)"Sales order not found.");
            if (so.Status != SoStatus.Draft) return ((SalesOrder?)null, (string?)"SO_NOT_SUBMITTABLE", (string?)"Only draft sales orders can be submitted.");
            db.SalesOrders[db.SalesOrders.IndexOf(so)] = so with { Status = SoStatus.PendingApproval };
            Audit.Log(db, u, "submit", "sales_order", id, "SO submitted for approval");
            return ((SalesOrder?)db.SalesOrders.First(s => s.Id == id), (string?)null, (string?)null);
        });

    public (SalesOrder? order, string? code, string? message) Approve(CurrentUser approver, string soId, string? reason)
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

    public (Shipment? shipment, Journal? journal, string? code, string? message) Ship(
        CurrentUser user, string soId, string itemSku, decimal qty)
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
    public (Journal? journal, string? code, string? message) Invoice(CurrentUser user, string soId)
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
