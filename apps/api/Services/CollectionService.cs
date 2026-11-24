using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Collections (ERP-SALES-057/058/059): worklist off authoritative balances, manual reminder
// records (R-T7g — no scheduler), disputes with promise-to-pay, credit exposure with the
// hold/override gate (R-T7e basis: plain BDT sums; FX deferred).
public sealed class CollectionService(
    ICollectionsRepository collections, IInvoiceRepository invoices, ICustomerRepository customers,
    ISalesOrderRepository orders, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : ICollectionService
{
    // 058 source + 059 AC2: posted invoices past due from authoritative balances, flagged
    // disputed/suppressed; a broken promise-to-pay marks the follow-up
    public object Worklist()
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = new List<(DateOnly DueDate, object Row)>();
        foreach (var invoice in invoices.AllInvoices(InvoiceStatus.Posted, null))
        {
            var dueDate = invoice.DueDate;
            if (dueDate is null || dueDate.Value >= asOf) continue; // only past-due documents
            var open = decimal.Round(CreditMath.InvoiceOpen(invoice, invoices.AdjustmentsForInvoice(invoice.Id)), 2, MidpointRounding.AwayFromZero);
            if (open <= 0) continue; // fully credited/settled — nothing to collect

            var activeDispute = ActiveDispute(invoice.Id);
            var customer = customers.Find(invoice.CustomerId);
            rows.Add((dueDate.Value, new
            {
                invoiceId = invoice.Id,
                customerId = invoice.CustomerId,
                invoiceDate = invoice.InvoiceDate,
                dueDate = dueDate.Value,
                daysPastDue = asOf.DayNumber - dueDate.Value.DayNumber,
                openAmount = open,
                disputed = activeDispute is not null,
                suppressed = customer?.RemindersSuppressed == true,
                followUp = activeDispute is { Status: DisputeStatus.Promised, PromiseDueDate: { } promise } && promise < asOf,
            }));
        }
        return rows.OrderBy(r => r.DueDate.DayNumber).Select(r => r.Row).ToList();
    }

    // 058 AC2 read: the invoice's append-only reminder history
    public (object? data, string? code, string? message) RemindersFor(string invoiceId)
    {
        if (invoices.FindInvoice(invoiceId) is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        return (collections.RemindersForInvoice(invoiceId), null, null);
    }

    // 059 read: the invoice's disputes with promise-to-pay state
    public (object? data, string? code, string? message) DisputesFor(string invoiceId)
    {
        if (invoices.FindInvoice(invoiceId) is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        return (collections.DisputesForInvoice(invoiceId), null, null);
    }

    // 058 AC1: reminders are refused for settled, disputed (059 AC1) or suppressed invoices —
    // the open amount is re-read CURRENT at logging time (partial payments are honored)
    public (CollectionReminder? reminder, string? code, string? message) LogReminder(CurrentUser user, Dtos.LogReminderRequest request)
    {
        var invoice = invoices.FindInvoice(request.InvoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        if (invoice.Status != InvoiceStatus.Posted)
            return (null, "REMINDER_SUPPRESSED", "Reminders apply to posted invoices only.");

        var open = CreditMath.InvoiceOpen(invoice, invoices.AdjustmentsForInvoice(invoice.Id));
        if (open <= 0) return (null, "REMINDER_SUPPRESSED", $"Invoice {invoice.Id} is settled (open 0.00); no reminder.");
        if (ActiveDispute(invoice.Id) is { } dispute)
            return (null, "REMINDER_SUPPRESSED", $"Invoice {invoice.Id} has an active {dispute.Status} dispute; reminders are suppressed.");
        if (customers.Find(invoice.CustomerId)?.RemindersSuppressed == true)
            return (null, "REMINDER_SUPPRESSED", $"Customer reminders are suppressed; no reminder for {invoice.Id}.");

        var reminder = new CollectionReminder(numbers.Next("REM"), ec.TenantId, ec.EntityId, invoice.Id,
            request.Channel, request.TemplateVersion, request.Recipient, DateTimeOffset.UtcNow, user.Id);
        collections.AddReminder(reminder);
        audit.Log(user, "remind", "invoice", invoice.Id,
            $"Collection reminder via {request.Channel} to {request.Recipient} (open {open:0.00})");
        collections.Save();
        return (reminder, null, null);
    }

    // 059 AC1: an Open/Promised dispute suppresses reminders without touching posted amounts
    public (Dispute? dispute, string? code, string? message) CreateDispute(CurrentUser user, Dtos.CreateDisputeRequest request)
    {
        var invoice = invoices.FindInvoice(request.InvoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        if (invoice.Status != InvoiceStatus.Posted)
            return (null, "DISPUTE_ERROR", "Disputes apply to posted invoices only.");

        var dispute = new Dispute(numbers.Next("DSP"), ec.TenantId, ec.EntityId, invoice.Id, request.Reason,
            request.PromiseDueDate is null ? DisputeStatus.Open : DisputeStatus.Promised,
            request.PromiseDueDate, null, user.Id, user.Id, DateTimeOffset.UtcNow);
        collections.AddDispute(dispute);
        audit.Log(user, "dispute", "invoice", invoice.Id,
            $"Dispute {dispute.Id} opened ({request.Reason})" +
            (request.PromiseDueDate is { } p ? $" with promise-to-pay by {p:yyyy-MM-dd}" : ""));
        collections.Save();
        return (dispute, null, null);
    }

    // 059: resolution re-enables reminders
    public (Dispute? dispute, string? code, string? message) ResolveDispute(CurrentUser user, string disputeId, string outcome)
    {
        var dispute = collections.FindDispute(disputeId);
        if (dispute is null) return (null, "DISPUTE_NOT_FOUND", "Dispute not found.");
        if (dispute.Status == DisputeStatus.Resolved)
            return (null, "DISPUTE_STATE_ERROR", "This dispute is already resolved.");
        collections.MarkDisputeResolved(dispute, outcome);
        audit.Log(user, "resolve", "dispute", disputeId, $"Dispute {disputeId} resolved: {outcome} — reminders re-enabled");
        collections.Save();
        return (dispute, null, null);
    }

    // 057: exposure = open posted invoices (− credits + debits) + open Confirmed/CreditHold SO
    // totals vs CreditLimit. no-data says so explicitly — never silent (057 edge).
    public (object? data, string? code, string? message) CreditExposure(string customerId)
    {
        var customer = customers.Find(customerId);
        if (customer is null) return (null, "CUSTOMER_NOT_FOUND", "Customer not found.");

        var posted = invoices.AllInvoices(InvoiceStatus.Posted, customerId);
        var openInvoiced = posted.Sum(i => CreditMath.InvoiceOpen(i, invoices.AdjustmentsForInvoice(i.Id)));
        var openOrders = orders.OpenForCustomer(customerId);
        var openOrderTotal = openOrders.Sum(CreditMath.OrderTotal);
        var total = decimal.Round(openInvoiced + openOrderTotal, 2, MidpointRounding.AwayFromZero);

        var data = new
        {
            customerId,
            customer.CreditLimit,
            openInvoiced = decimal.Round(openInvoiced, 2, MidpointRounding.AwayFromZero),
            openOrders = openOrderTotal,
            total,
            overLimit = customer.CreditLimit is { } limit && total > limit,
            hasData = posted.Count > 0 || openOrders.Count > 0,
        };
        return (data, null, null);
    }

    private Dispute? ActiveDispute(string invoiceId) =>
        collections.DisputesForInvoice(invoiceId)
            .FirstOrDefault(d => d.Status is DisputeStatus.Open or DisputeStatus.Promised);
}
