using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// E-invoice adapter boundary + delivery tracking (ERP-SALES-050/051). Q-S6: the adapter
// interface ships now with a NotEnabled stub for the seeded entity — jurisdiction profiles
// wait for D-01/D-13. Delivery attempts never conflate delivery state with payment state.
public sealed class StubInvoiceAdapter(EntityContext ec) : IInvoiceAdapter
{
    public (EinvoiceStatus status, string jurisdiction, string schemaVersion, string correlationId, string[] errors) Submit(Invoice invoice)
    {
        var jurisdiction = $"entity:{ec.EntityId}";
        return (EinvoiceStatus.NotEnabled, jurisdiction, "none", $"EIV-{Guid.NewGuid():N}"[..16],
            [$"No e-invoice jurisdiction profile is configured for {jurisdiction} (adapter contract fixed by Q-S6; country profiles arrive with D-01/D-13)."]);
    }
}

public sealed class SalesDeliveryService(
    IInvoiceRepository invoices, IInvoiceAdapter adapter, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec, ErpDbContext db) : ISalesDeliveryService
{
    public (object? data, string? code, string? message) SubmitEinvoice(CurrentUser u, string invoiceId)
    {
        var invoice = invoices.FindInvoice(invoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        if (invoice.Status != InvoiceStatus.Posted)
            return (null, "EINVOICE_STATE_ERROR", $"Only posted invoices can be transmitted (current: {invoice.Status}).");
        var (status, jurisdiction, schemaVersion, correlationId, errors) = adapter.Submit(invoice);
        var count = db.EinvoiceSubmissions.Count(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.InvoiceId == invoiceId);
        var submission = new EinvoiceSubmission($"{invoiceId}:EIV:{count + 1}", ec.TenantId, ec.EntityId,
            invoiceId, jurisdiction, schemaVersion, correlationId, status,
            System.Text.Json.JsonSerializer.Serialize(errors, Idem.JsonOptions), u.Id, DateTimeOffset.UtcNow);
        db.EinvoiceSubmissions.Add(submission);
        audit.Log(u, "einvoice_submit", "invoice", invoiceId, $"Adapter {status} ({jurisdiction}, schema {schemaVersion})");
        db.SaveChanges();
        // 050 edge: failed validation blocks transmission — NotEnabled carries structured errors
        return (EinvoiceView(submission, errors),
            status == EinvoiceStatus.NotEnabled ? "EINVOICE_NOT_ENABLED" : null,
            status == EinvoiceStatus.NotEnabled ? errors[0] : null);
    }

    // 051 AC1: attempts link to the same posted revision; resend = a new attempt row
    public (object? data, string? code, string? message) AddDeliveryAttempt(CurrentUser u, string invoiceId, string channel, string recipient)
    {
        var invoice = invoices.FindInvoice(invoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        if (invoice.Status != InvoiceStatus.Posted)
            return (null, "DELIVERY_STATE_ERROR", $"Delivery rides posted invoices only (current: {invoice.Status}).");
        var attemptNo = db.InvoiceDeliveries.Count(d => d.TenantId == ec.TenantId && d.LegalEntityId == ec.EntityId && d.InvoiceId == invoiceId) + 1;
        var delivery = new InvoiceDelivery(numbers.Next("DLV"), ec.TenantId, ec.EntityId, invoiceId,
            channel, recipient, DeliveryStatus.Sent, attemptNo, null, DateTimeOffset.UtcNow, null, u.Id, DateTimeOffset.UtcNow);
        db.InvoiceDeliveries.Add(delivery);
        audit.Log(u, "deliver", "invoice", invoiceId, $"Attempt {attemptNo} via {channel} to {recipient}");
        audit.Emit(u, "sales.invoice.delivered", new { invoiceId, channel, recipient, attempt = attemptNo });
        db.SaveChanges();
        return (DeliveryView(delivery), null, null);
    }

    // 051 AC2: a bounce is visible on the attempt and never touches the invoice/payment state
    public (object? data, string? code, string? message) MarkBounced(CurrentUser u, string deliveryId, string reason)
    {
        var delivery = db.InvoiceDeliveries.FirstOrDefault(d =>
            d.TenantId == ec.TenantId && d.LegalEntityId == ec.EntityId && d.Id == deliveryId);
        if (delivery is null) return (null, "DELIVERY_NOT_FOUND", "Delivery attempt not found.");
        if (delivery.Status == DeliveryStatus.Bounced)
            return (null, "DELIVERY_STATE_ERROR", "This attempt is already marked bounced.");
        db.Entry(delivery).Property(nameof(InvoiceDelivery.Status)).CurrentValue = DeliveryStatus.Bounced;
        db.Entry(delivery).Property(nameof(InvoiceDelivery.Reason)).CurrentValue = reason;
        db.Entry(delivery).Property(nameof(InvoiceDelivery.BouncedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(u, "delivery_bounce", "invoice", delivery.InvoiceId, $"Attempt {delivery.Attempt} bounced: {reason}");
        db.SaveChanges();
        return (DeliveryView(delivery), null, null);
    }

    public (object? data, string? code, string? message) DeliveriesFor(string invoiceId) =>
        (db.InvoiceDeliveries.Where(d => d.TenantId == ec.TenantId && d.LegalEntityId == ec.EntityId && d.InvoiceId == invoiceId)
            .OrderBy(d => d.Attempt).Select(DeliveryView).ToList(), null, null);

    private static object EinvoiceView(EinvoiceSubmission s, string[] errors) => new
    {
        s.Id, s.InvoiceId, s.Jurisdiction, s.SchemaVersion, s.CorrelationId, s.Status,
        errors, s.SubmittedBy, s.SubmittedAt,
    };

    private static object DeliveryView(InvoiceDelivery d) => new
    {
        d.Id, d.InvoiceId, d.Channel, d.Recipient, d.Status, d.Attempt, d.Reason,
        d.SentAt, d.BouncedAt, d.CreatedBy,
    };
}
