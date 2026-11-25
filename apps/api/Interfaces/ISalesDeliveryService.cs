using Erp.Api;

namespace Erp.Api.Interfaces;

// 050: versioned jurisdiction adapter boundary — real country profiles arrive with D-01/D-13
public interface IInvoiceAdapter
{
    (EinvoiceStatus status, string jurisdiction, string schemaVersion, string correlationId, string[] errors) Submit(Invoice invoice);
}

public interface ISalesDeliveryService
{
    (object? data, string? code, string? message) SubmitEinvoice(CurrentUser u, string invoiceId);
    (object? data, string? code, string? message) AddDeliveryAttempt(CurrentUser u, string invoiceId, string channel, string recipient);
    (object? data, string? code, string? message) MarkBounced(CurrentUser u, string deliveryId, string reason);
    (object? data, string? code, string? message) DeliveriesFor(string invoiceId);
}
