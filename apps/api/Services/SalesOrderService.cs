using Erp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class SalesOrderService(
    ICustomerRepository customers, ISalesOrderRepository orders, IPurchaseOrderRepository costing,
    IInventoryRepository inventory, IJournalRepository journals, IInvoiceRepository invoiceDocs,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : ISalesOrderService
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

    // R-T7i: approval is gated on so:approve (moved off so:write); approver ≠ creator still holds.
    // 057 AC2: an approval that would push exposure past the customer's credit limit lands the
    // order in CreditHold instead of Confirmed — release is a separate credit:override act.
    public (SalesOrder? order, string? code, string? message) Approve(CurrentUser approver, string soId, string? reason)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.PendingApproval) return (null, "SO_APPROVAL_ERROR", $"Only pending sales orders can be approved (current: {so.Status}).");
        if (so.CreatedBy == approver.Id) return (null, "SO_APPROVAL_ERROR", "Maker-checker: the creator cannot approve their own sales order.");
        if (!approver.Has("so:approve")) return (null, "FORBIDDEN", "so:approve permission required.");

        if (reason is not null)
        {
            orders.MarkRejected(so, approver.Id, reason);
            audit.Log(approver, "reject", "sales_order", so.Id, $"SO rejected: {reason}");
            orders.Save();
            return (so, null, null);
        }

        // review 2a: the exposure check and the approval commit are serialized per customer —
        // SELECT … FOR UPDATE on the customer row inside the approval transaction means a
        // concurrent approval for the same customer waits, then re-reads fresh balances.
        // Without this, two officers approving together could both pass the limit check
        // against committed state and both confirm. When Idem.Wrap already opened the request
        // transaction, we join it instead of nesting a second one.
        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        LockCustomerForExposure(so.CustomerId);

        var (overLimit, exposure) = ExposureIfApproved(so);
        if (overLimit)
        {
            orders.MarkCreditHold(so, approver.Id);
            audit.Log(approver, "credit_hold", "sales_order", so.Id,
                $"SO approved but held: exposure {exposure:0.00} exceeds the customer credit limit");
            orders.Save();
            if (ownsTx) tx!.Commit();
            return (so, null, null);
        }

        orders.MarkConfirmed(so, approver.Id);
        audit.Log(approver, "approve", "sales_order", so.Id, $"SO approved by {approver.Email}");
        audit.Emit(approver, "sales.so.approved", new { soId = so.Id, approver = approver.Id });
        orders.Save();
        if (ownsTx) tx!.Commit();
        return (so, null, null);
    }

    // 057 AC2 release: CreditHold → Confirmed, credit:override gate, audited, outbox event
    public (SalesOrder? order, string? code, string? message) ReleaseCreditHold(CurrentUser user, string soId)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.CreditHold) return (null, "SO_STATE_ERROR", $"Only credit-held sales orders can be released (current: {so.Status}).");
        if (!user.Has("credit:override")) return (null, "FORBIDDEN", "credit:override permission required.");

        orders.MarkConfirmed(so, user.Id);
        audit.Log(user, "credit_release", "sales_order", so.Id, $"Credit hold released by {user.Email}; SO confirmed");
        audit.Emit(user, "sales.credit.released", new { soId = so.Id, releasedBy = user.Id });
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

    // would confirming this order push the customer's exposure past the credit limit?
    // exposure = open posted invoices (− credits + debits) + open Confirmed/CreditHold SO totals (R-T7e)
    private (bool overLimit, decimal exposure) ExposureIfApproved(SalesOrder so)
    {
        var customer = customers.Find(so.CustomerId);
        if (customer?.CreditLimit is not { } limit) return (false, 0); // no limit configured → no hold

        var openInvoiced = OpenInvoiced(so.CustomerId);
        var openOrders = orders.OpenForCustomer(so.CustomerId).Where(o => o.Id != so.Id).Sum(CreditMath.OrderTotal);
        var exposure = openInvoiced + openOrders + CreditMath.OrderTotal(so);
        return (exposure > limit, exposure);
    }

    internal decimal OpenInvoiced(string customerId) =>
        invoiceDocs.AllInvoices(InvoiceStatus.Posted, customerId)
            .Sum(i => CreditMath.InvoiceOpen(i, invoiceDocs.AdjustmentsForInvoice(i.Id)));

    // review 2a: parameterized row lock — the caller's transaction holds it until commit, so
    // exposure computation and commit are serialized per customer (pair with ExposureIfApproved)
    private void LockCustomerForExposure(string customerId) =>
        db.Customers
            .FromSql($"SELECT * FROM customers WHERE id = {customerId} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId} FOR UPDATE")
            .ToList();
}
