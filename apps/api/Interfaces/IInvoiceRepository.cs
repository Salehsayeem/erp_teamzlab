namespace Erp.Api.Interfaces;

// Invoice documents + adjustments (ERP-SALES-042..046/048/052/053)
public interface IInvoiceRepository
{
    Invoice? FindInvoice(string invoiceId);
    List<Invoice> AllInvoices(InvoiceStatus? status, string? customerId);
    void AddInvoice(Invoice invoice);
    Invoice? FindBySourceEvent(string sourceEventId);
    void PinLineTax(InvoiceLine line, string taxCodeId, int taxCodeVersion, decimal taxAmount);
    void MarkInvoicePosted(Invoice invoice, string journalId, string postedBy, DateOnly dueDate, decimal taxTotal, decimal total);
    void MarkInvoiceDiscarded(Invoice invoice);
    List<AdjustmentDocument> AdjustmentsForInvoice(string invoiceId);
    AdjustmentDocument? FindAdjustment(string adjustmentId);
    void AddAdjustment(AdjustmentDocument adjustment);
    void PinAdjustmentLineTax(AdjustmentLine line, string taxCodeId, int taxCodeVersion, decimal taxAmount);
    void MarkAdjustmentApproved(AdjustmentDocument adjustment, string approverId, string journalId, decimal taxTotal, decimal total);
    void MarkAdjustmentRejected(AdjustmentDocument adjustment, string approverId, string reason);
    int Save();
}
