using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Invoice documents + credit/debit adjustments. The adjustments table is "sales_adjustments" —
// inventory's stock-adjustment table already owns the plain name.
public sealed class InvoiceRepository(ErpDbContext db, EntityContext ec) : IInvoiceRepository
{
    private IQueryable<Invoice> InvoiceScope() =>
        db.Invoices.Where(i => i.TenantId == ec.TenantId && i.LegalEntityId == ec.EntityId);

    private IQueryable<AdjustmentDocument> AdjustmentScope() =>
        db.SalesAdjustments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);

    private Invoice? LoadInvoice(IQueryable<Invoice> query) =>
        query.Include(i => i.Lines).OrderBy(i => i.Id).FirstOrDefault(); // invoice_lines is an owned table

    public Invoice? FindInvoice(string invoiceId) => LoadInvoice(InvoiceScope().Where(i => i.Id == invoiceId));

    public List<Invoice> AllInvoices(InvoiceStatus? status, string? customerId) =>
        InvoiceScope().Where(i => status == null || i.Status == status)
            .Where(i => customerId == null || i.CustomerId == customerId)
            .Include(i => i.Lines).OrderBy(i => i.Id).ToList();

    // 042 AC2: one live invoice per shipment source event — the partial unique index backs the
    // service check up under concurrency. DISCARDED drafts release their source event (1b).
    public Invoice? FindBySourceEvent(string sourceEventId) =>
        InvoiceScope().FirstOrDefault(i => i.SourceEventId == sourceEventId && i.Status != InvoiceStatus.Discarded);

    public void AddInvoice(Invoice invoice)
    {
        db.Invoices.Add(invoice);
        foreach (var line in invoice.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    // 048 AC2: pin the resolved revision + computed tax on the tracked line (in-place mutation —
    // replacing owned rows in the list would race the change tracker)
    public void PinLineTax(InvoiceLine line, string taxCodeId, int taxCodeVersion, decimal taxAmount)
    {
        db.Entry(line).Property(nameof(InvoiceLine.TaxCodeId)).CurrentValue = taxCodeId;
        db.Entry(line).Property(nameof(InvoiceLine.TaxCodeVersion)).CurrentValue = taxCodeVersion;
        db.Entry(line).Property(nameof(InvoiceLine.TaxAmount)).CurrentValue = taxAmount;
    }

    public void MarkInvoicePosted(Invoice invoice, string journalId, string postedBy, DateOnly dueDate, decimal taxTotal, decimal total)
    {
        // Status is a concurrency token (review 1a): the UPDATE carries WHERE status =
        // <original>, so a stale concurrent poster updates zero rows and SaveChanges throws
        db.Entry(invoice).Property(nameof(Invoice.Status)).CurrentValue = InvoiceStatus.Posted;
        db.Entry(invoice).Property(nameof(Invoice.JournalId)).CurrentValue = journalId;
        db.Entry(invoice).Property(nameof(Invoice.PostedBy)).CurrentValue = postedBy;
        db.Entry(invoice).Property(nameof(Invoice.PostedAt)).CurrentValue = DateTimeOffset.UtcNow;
        db.Entry(invoice).Property(nameof(Invoice.DueDate)).CurrentValue = dueDate; // 053 AC2: frozen from here on
        db.Entry(invoice).Property(nameof(Invoice.TaxTotal)).CurrentValue = taxTotal;
        db.Entry(invoice).Property(nameof(Invoice.Total)).CurrentValue = total;
    }

    // review 1b: terminal — frees the source event (partial index + FindBySourceEvent skip it)
    public void MarkInvoiceDiscarded(Invoice invoice) =>
        db.Entry(invoice).Property(nameof(Invoice.Status)).CurrentValue = InvoiceStatus.Discarded;

    public List<AdjustmentDocument> AdjustmentsForInvoice(string invoiceId) =>
        AdjustmentScope().Where(a => a.InvoiceId == invoiceId)
            .Include(a => a.Lines).OrderBy(a => a.Id).ToList();

    public AdjustmentDocument? FindAdjustment(string adjustmentId) =>
        AdjustmentScope().Where(a => a.Id == adjustmentId).Include(a => a.Lines).FirstOrDefault();

    public void AddAdjustment(AdjustmentDocument adjustment)
    {
        db.SalesAdjustments.Add(adjustment);
        foreach (var line in adjustment.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public void PinAdjustmentLineTax(AdjustmentLine line, string taxCodeId, int taxCodeVersion, decimal taxAmount)
    {
        db.Entry(line).Property(nameof(AdjustmentLine.TaxCodeId)).CurrentValue = taxCodeId;
        db.Entry(line).Property(nameof(AdjustmentLine.TaxCodeVersion)).CurrentValue = taxCodeVersion;
        db.Entry(line).Property(nameof(AdjustmentLine.TaxAmount)).CurrentValue = taxAmount;
    }

    public void MarkAdjustmentApproved(AdjustmentDocument adjustment, string approverId, string journalId, decimal taxTotal, decimal total)
    {
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.Status)).CurrentValue = AdjustmentStatus.Approved;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.JournalId)).CurrentValue = journalId;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.ApprovedBy)).CurrentValue = approverId;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.ApprovedAt)).CurrentValue = DateTimeOffset.UtcNow;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.TaxTotal)).CurrentValue = taxTotal;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.Total)).CurrentValue = total;
    }

    public void MarkAdjustmentRejected(AdjustmentDocument adjustment, string approverId, string reason)
    {
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.Status)).CurrentValue = AdjustmentStatus.Rejected;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.ApprovedBy)).CurrentValue = approverId;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.ApprovedAt)).CurrentValue = DateTimeOffset.UtcNow;
        db.Entry(adjustment).Property(nameof(AdjustmentDocument.RejectionReason)).CurrentValue = reason;
    }

    public int Save() => db.SaveChanges();
}
