using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class SalesOrderService(
    ICustomerRepository customers, ISalesOrderRepository orders, IPurchaseOrderRepository costing,
    IInventoryRepository inventory, IJournalRepository journals, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec, ErpDbContext db) : ISalesOrderService
{
    public object List() =>
        orders.All().Select(s => new
        {
            s.Id, s.CustomerId, s.Status, s.Lines, s.CreatedBy, s.CreatedAt, s.ApprovedBy, s.ApprovedAt, s.RejectionReason,
            total = s.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
        }).ToList();

    public (SalesOrder? order, string? code, string? message) Create(CurrentUser u, string customerId, IReadOnlyList<SoLine> lines)
    {
        if (!customers.Exists(customerId))
            return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        var so = new SalesOrder(numbers.Next("SO"), ec.TenantId, ec.EntityId, customerId, SoStatus.Draft, lines, u.Id, DateTimeOffset.UtcNow);
        orders.Add(so);
        audit.Log(u, "create", "sales_order", so.Id, $"SO created for customer {customerId} with {lines.Count} line(s)");
        orders.Save();
        return (so, null, null);
    }

    public (SalesOrder? order, string? code, string? message) Submit(CurrentUser u, string id)
    {
        var so = orders.Find(id);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Draft) return (null, "SO_NOT_SUBMITTABLE", "Only draft sales orders can be submitted.");
        orders.MarkSubmitted(so);
        audit.Log(u, "submit", "sales_order", id, "SO submitted for approval");
        orders.Save();
        return (so, null, null);
    }

    public (SalesOrder? order, string? code, string? message) Approve(CurrentUser approver, string soId, string? reason)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.PendingApproval) return (null, "SO_APPROVAL_ERROR", $"Only pending sales orders can be approved (current: {so.Status}).");
        if (so.CreatedBy == approver.Id) return (null, "SO_APPROVAL_ERROR", "Maker-checker: the creator cannot approve their own sales order.");
        if (!approver.Has("so:write") && !approver.Has("*")) return (null, "FORBIDDEN", "so:write permission required.");

        if (reason is null) orders.MarkConfirmed(so, approver.Id);
        else orders.MarkRejected(so, approver.Id, reason);
        audit.Log(approver, reason is null ? "approve" : "reject", "sales_order", so.Id,
            reason is null ? $"SO approved by {approver.Email}" : $"SO rejected: {reason}");
        if (reason is null) audit.Emit(approver, "sales.so.approved", new { soId = so.Id, approver = approver.Id });
        orders.Save();
        return (so, null, null);
    }

    public (Shipment? shipment, Journal? journal, string? code, string? message) Ship(
        CurrentUser user, string soId, string itemSku, decimal qty)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Confirmed) return (null, null, "SHIPMENT_ERROR", $"Only confirmed sales orders can be shipped (current: {so.Status}).");
        if (qty <= 0) return (null, null, "VALIDATION_ERROR", "Shipment qty must be greater than 0.");
        if (so.Lines.FirstOrDefault(l => l.ItemSku == itemSku) is null) return (null, null, "ITEM_NOT_FOUND", $"Item {itemSku} is not on this sales order.");

        var onHand = inventory.QtyOf(itemSku);
        if (onHand < qty)
            return (null, null, "INSUFFICIENT_STOCK", $"Insufficient stock for {itemSku}: available {onHand}, requested {qty}.");

        // task-5: shipments post the COGS journal at today — the centralized period lock applies
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, null, guardCode, guardMsg);

        inventory.DrawStock(itemSku, qty);

        var shipment = new Shipment(numbers.Next("SHP"), ec.TenantId, ec.EntityId, soId, itemSku, qty, DateTimeOffset.UtcNow, user.Id);
        orders.AddShipment(shipment);

        // COGS at inventory cost basis — weighted average of receipt PO unit prices (0 when no receipts yet)
        var unitCost = costing.AverageUnitCost(itemSku);
        var amount = decimal.Round(unitCost * qty, 2, MidpointRounding.AwayFromZero);
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
            $"COGS for shipment {shipment.Id} (SO {soId})",
            new JournalLine[] { new("5100", "Cost of Goods Sold", amount, 0), new("1400", "Inventory", 0, amount) },
            DateTimeOffset.UtcNow, user.Id);
        journals.Add(journal);

        // all lines shipped? flip SO status
        var shippedBySku = orders.ShippedBySku(soId);
        if (so.Lines.All(l => shippedBySku.TryGetValue(l.ItemSku, out var got) && got >= l.Qty))
            orders.MarkShipped(so);

        audit.Log(user, "ship", "shipment", shipment.Id, $"Shipped {qty} x {itemSku} against {soId}; journal {journal.Id} posted");
        audit.Emit(user, "sales.shipment.posted", new { shipmentId = shipment.Id, soId, itemSku, qty, journalId = journal.Id });
        orders.Save(); // stock draw + shipment + journal + audit + outbox in ONE SaveChanges
        return (shipment, journal, null, null);
    }

    // shortcut: approval gate = SO approval; separate invoice approval when spec card demands it
    public (Journal? journal, string? code, string? message) Invoice(CurrentUser user, string soId)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Shipped) return (null, "INVOICE_ERROR", $"Only shipped sales orders can be invoiced (current: {so.Status}).");

        // task-5: invoices post the AR journal at today — the centralized period lock applies
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, guardCode, guardMsg);

        var total = so.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
            $"AR invoice for SO {soId}",
            new JournalLine[] { new("1200", "Accounts Receivable", total, 0), new("4000", "Sales Revenue", 0, total) },
            DateTimeOffset.UtcNow, user.Id);
        journals.Add(journal);
        orders.MarkInvoiced(so);

        audit.Log(user, "invoice", "sales_order", so.Id, $"SO invoiced for {total}; journal {journal.Id} posted");
        audit.Emit(user, "sales.invoice.posted", new { soId, total, journalId = journal.Id });
        orders.Save();
        return (journal, null, null);
    }
}
