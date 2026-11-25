using Erp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Invoice documents (ERP-SALES-042/043/044/045/048/049/052/053) + credit/debit adjustments (046).
// Drafts assemble from shipment source events (no double billing); posting is maker-checker
// (editor ≠ poster) with per-line tax resolved from the effective TaxCode revision, pinned per
// line (048 AC2), one balanced journal + document flip in ONE SaveChanges (045 AC1).
// R-T7c: drafts consume the legal INV series at creation (NextDoc has no provisional ids).
public sealed class InvoiceService(
    IInvoiceRepository invoices, ISalesOrderRepository orders, ICustomerRepository customers,
    ITaxCodeRepository taxCodes, IJournalRepository journals, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec, ErpDbContext db) : IInvoiceService
{
    // R9c: rates live in tax_codes data — this pilot's standard OUTPUT code is the default
    // citation for shipment-sourced lines (0% / exempt codes publish rate 0 per fix 72931d8)
    private const string DefaultTaxCode = "VAT-STD-BD";
    private const string ArAccount = "1200";
    private const string RevenueAccount = "4000";
    private const string OutputVatAccount = "2200";

    public object List(InvoiceStatus? status, string? customerId)
    {
        var docs = invoices.AllInvoices(status, customerId);
        return docs.Select(i => new
        {
            i.Id, i.CustomerId, i.SalesOrderId, i.SourceEventId, i.Status, i.Currency,
            i.InvoiceDate, i.DueDate, i.PaymentTermCode, i.SubTotal, i.TaxTotal, i.Total,
            i.JournalId, i.PostedBy, i.PostedAt, i.CreatedBy, i.CreatedAt,
            openAmount = decimal.Round(CreditMath.InvoiceOpen(i, invoices.AdjustmentsForInvoice(i.Id)), 2, MidpointRounding.AwayFromZero),
            stale = IsStale(i), // 043 AC2 trimmed to a flag: source-order pricing changed under the draft
            lines = i.Lines,
        }).ToList();
    }

    // 046 read: the adjustment history of one invoice (kind/status/totals as stored; approved
    // memos carry the finalized tax + total, drafts 0 until approval)
    public (object? data, string? code, string? message) AdjustmentsFor(string invoiceId)
    {
        if (invoices.FindInvoice(invoiceId) is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        return (invoices.AdjustmentsForInvoice(invoiceId), null, null);
    }

    private bool IsStale(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Draft) return false;
        var so = orders.Find(invoice.SalesOrderId);
        return so is not null && invoice.Lines.Any(l =>
            so.Lines.FirstOrDefault(s => s.ItemSku == l.ItemSku) is { } soLine && soLine.UnitPrice != l.UnitPrice);
    }

    // 042/043: assemble a draft from the oldest UNBILLED shipment of a shipped order — one draft
    // per source event; a second attempt → 409 ALREADY_BILLED (unique index backs this up)
    public (Invoice? invoice, string? code, string? message) DraftFromShipments(CurrentUser user, string soId, DateOnly? invoiceDate)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        // 036: invoice eligibility is per shipment (partial fulfillment bills what shipped) —
        // a Confirmed order with an unbilled shipment event drafts like a fully shipped one,
        // while an order with nothing billable keeps the house SO_NOT_BILLABLE / ALREADY_BILLED split
        var shipments = orders.ShipmentsFor(soId);
        var source = shipments.FirstOrDefault(sh => invoices.FindBySourceEvent(sh.Id) is null);
        if (so.Status is not (SoStatus.Confirmed or SoStatus.Shipped or SoStatus.Invoiced) || (source is null && so.Status == SoStatus.Confirmed))
            return (null, "SO_NOT_BILLABLE", $"Only shipped sales orders can be invoiced (current: {so.Status}).");
        if (source is null)
            return (null, "ALREADY_BILLED",
                $"Every shipment of {soId} is already billed (source events {string.Join(", ", shipments.Select(s => s.Id))}).");

        var soLine = so.Lines.FirstOrDefault(l => l.ItemSku == source.ItemSku);
        if (soLine is null)
            return (null, "INVOICE_ERROR", $"Shipment {source.Id} item {source.ItemSku} is no longer on the order; the draft would not reconcile (043 AC1).");

        var customer = customers.Find(so.CustomerId);
        var date = invoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var lineNet = decimal.Round(source.Qty * soLine.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var invoice = new Invoice(numbers.Next("INV"), ec.TenantId, ec.EntityId, so.CustomerId, soId, source.Id,
            InvoiceStatus.Draft, EntityContext.SeedEntity.Currency, date, null,
            $"NET{customer?.PaymentTermNetDays ?? 30}", lineNet, 0, lineNet, // tax fills at posting
            null, null, null, user.Id, DateTimeOffset.UtcNow,
            [new InvoiceLine(source.ItemSku, soLine.Description, source.Qty, soLine.UnitPrice, lineNet)]);
        invoices.AddInvoice(invoice);
        audit.Log(user, "draft", "invoice", invoice.Id,
            $"Invoice draft {invoice.Id} assembled from shipment {source.Id} (SO {soId}): net {lineNet:0.00}");
        invoices.Save();
        return (invoice, null, null);
    }

    // 045: maker-checker posting — poster ≠ drafter, period open at the invoice date, per-line
    // tax pinned, one balanced journal, document flips to Posted atomically. Review 1a: the
    // status flip is a concurrency-token UPDATE — a stale second poster commits NOTHING and
    // gets 409 INVOICE_ALREADY_POSTED instead of a duplicate AR journal.
    public (object? data, string? code, string? message) Post(CurrentUser approver, string invoiceId)
    {
        var invoice = invoices.FindInvoice(invoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        if (invoice.Status == InvoiceStatus.Discarded)
            return (null, "INVOICE_DISCARDED", "A discarded invoice can never be posted; its shipment source event must be re-drafted.");
        if (invoice.Status != InvoiceStatus.Draft)
            return (null, "INVOICE_APPROVAL_ERROR", $"Only draft invoices can be posted (current: {invoice.Status}).");
        if (invoice.CreatedBy == approver.Id)
            return (null, "INVOICE_APPROVAL_ERROR", "Maker-checker: the drafter cannot post their own invoice.");
        if (!approver.Has("invoices:approve")) return (null, "FORBIDDEN", "invoices:approve permission required.");

        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, invoice.InvoiceDate);
        if (guardCode is not null) return (null, guardCode, guardMsg);

        var taxError = ResolveLinesTax(invoice.Lines, invoice.InvoiceDate, invoices.PinLineTax, out var taxTotal);
        if (taxError is not null) return (null, taxError.Value.code, taxError.Value.message);

        var subTotal = invoice.Lines.Sum(l => l.LineNet);
        var total = subTotal + taxTotal; // 049: Total := SubTotal + TaxTotal — balanced by construction
        var customer = customers.Find(invoice.CustomerId);
        var dueDate = invoice.InvoiceDate.AddDays(customer?.PaymentTermNetDays ?? 30); // 053 AC1: frozen at posting

        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
            $"AR invoice for SO {invoice.SalesOrderId}", // R-T7j: marker stays aging-compatible
            InvoiceJournal(total, subTotal, taxTotal),
            DateTimeOffset.UtcNow, approver.Id, reversalOfId: null, accountingDate: invoice.InvoiceDate);
        journals.Add(journal);

        invoices.MarkInvoicePosted(invoice, journal.Id, approver.Id, dueDate, taxTotal, total);
        FlipSoIfFullyBilled(invoice);

        audit.Log(approver, "post", "invoice", invoice.Id,
            $"Invoice {invoice.Id} posted: total {total:0.00} (net {subTotal:0.00}, tax {taxTotal:0.00}), due {dueDate:yyyy-MM-dd}; journal {journal.Id}");
        // 052: receivables handoff payload rides the outbox event
        audit.Emit(approver, "sales.invoice.posted", new
        {
            invoiceId, journalId = journal.Id, dueDate, total, subTotal, taxTotal,
            currency = invoice.Currency, customerId = invoice.CustomerId,
        });
        try
        {
            invoices.Save(); // document + pinned lines + journal + audit + outbox in ONE SaveChanges
        }
        catch (DbUpdateConcurrencyException)
        {
            // review 1a: the guarded Draft→Posted UPDATE matched zero rows — another officer
            // posted this draft while our snapshot was stale. Nothing above commits: no
            // duplicate journal, no AR overstatement.
            return (null, "INVOICE_ALREADY_POSTED",
                $"Invoice {invoiceId} was already posted; the retry carried a stale draft snapshot and wrote nothing.");
        }
        return (new { invoice, journal }, null, null);
    }

    // review 1b: a mis-drafted draft must not dead-end its shipment — discard is terminal,
    // frees the source event (partial index + FindBySourceEvent skip Discarded), and burns the
    // INV number (documented in the audit). Drafter-or-admin gate is enforced here.
    public (Invoice? invoice, string? code, string? message) Discard(CurrentUser user, string invoiceId)
    {
        var invoice = invoices.FindInvoice(invoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        if (user.Id != invoice.CreatedBy && !user.Has("*"))
            return (null, "FORBIDDEN", "Only the drafter or an admin can discard a draft invoice.");
        if (invoice.Status != InvoiceStatus.Draft)
            return (null, "INVOICE_STATE_ERROR", $"Only draft invoices can be discarded (current: {invoice.Status}).");

        invoices.MarkInvoiceDiscarded(invoice);
        audit.Log(user, "discard", "invoice", invoice.Id,
            $"Invoice {invoice.Id} discarded by {user.Email} — the INV number is burned (never reused); " +
            $"source event {invoice.SourceEventId} is released for re-billing");
        invoices.Save();
        return (invoice, null, null);
    }

    // 046: every adjustment references a posted invoice (standalone memos deferred, R-T7k)
    public (AdjustmentDocument? adjustment, string? code, string? message) CreateAdjustment(CurrentUser user, Dtos.CreateAdjustmentRequest request)
    {
        var invoice = invoices.FindInvoice(request.InvoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "Invoice not found.");
        if (invoice.Status != InvoiceStatus.Posted)
            return (null, "ADJUSTMENT_ERROR", "Adjustments must reference a posted invoice.");

        var lines = request.Lines.Select(l => new AdjustmentLine(l.ItemSku, l.Description ?? l.ItemSku, l.Qty, l.UnitPrice,
            decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero), l.TaxCodeId)).ToList();
        var subTotal = lines.Sum(l => l.LineNet);
        var adjustment = new AdjustmentDocument(numbers.Next(request.Kind == AdjustmentKind.Credit ? "CRD" : "DBT"),
            ec.TenantId, ec.EntityId, request.Kind, invoice.Id, request.Reason, subTotal, 0, 0, // tax finalizes at approval
            AdjustmentStatus.Draft, null, null, null, null, user.Id, DateTimeOffset.UtcNow, lines);
        invoices.AddAdjustment(adjustment);
        audit.Log(user, "draft", "adjustment", adjustment.Id,
            $"{request.Kind} memo {adjustment.Id} drafted against invoice {invoice.Id}: net {subTotal:0.00} — {request.Reason}");
        invoices.Save();
        return (adjustment, null, null);
    }

    public (AdjustmentDocument? adjustment, string? code, string? message) ApproveAdjustment(CurrentUser approver, string adjustmentId, string? reason)
    {
        var adjustment = invoices.FindAdjustment(adjustmentId);
        if (adjustment is null) return (null, "ADJUSTMENT_NOT_FOUND", "Adjustment not found.");
        if (adjustment.Status != AdjustmentStatus.Draft)
            return (null, "ADJUSTMENT_APPROVAL_ERROR", $"Only draft adjustments can be approved (current: {adjustment.Status}).");
        if (adjustment.CreatedBy == approver.Id)
            return (null, "ADJUSTMENT_APPROVAL_ERROR", "Maker-checker: the drafter cannot approve their own adjustment.");
        if (!approver.Has("invoices:approve")) return (null, "FORBIDDEN", "invoices:approve permission required.");

        if (reason is not null)
        {
            invoices.MarkAdjustmentRejected(adjustment, approver.Id, reason);
            audit.Log(approver, "reject", "adjustment", adjustmentId, $"Adjustment rejected: {reason}");
            invoices.Save();
            return (adjustment, null, null);
        }

        var invoice = invoices.FindInvoice(adjustment.InvoiceId);
        if (invoice is null) return (null, "INVOICE_NOT_FOUND", "The referenced invoice no longer exists.");

        // review 2a: the open-amount check and the commit are serialized per INVOICE — lock the
        // invoice row so two concurrent approvals cannot both pass the bounded-credit check
        // against the same committed balances. When Idem.Wrap already opened the request
        // transaction, we join it instead of nesting a second one.
        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        db.Invoices
            .FromSql($"SELECT * FROM invoices WHERE id = {invoice.Id} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId} FOR UPDATE")
            .ToList();

        // the memo posts at approval day — the centralized period lock applies
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, guardCode, guardMsg);

        // tax reverses what the sale charged: resolved at the INVOICE's date, pinned per line
        var taxError = ResolveLinesTax(adjustment.Lines, invoice.InvoiceDate, invoices.PinAdjustmentLineTax, out var taxTotal);
        if (taxError is not null) return (null, taxError.Value.code, taxError.Value.message);

        var subTotal = adjustment.Lines.Sum(l => l.LineNet);
        var total = subTotal + taxTotal;

        var openBefore = CreditMath.InvoiceOpen(invoice, invoices.AdjustmentsForInvoice(invoice.Id));
        // 046 AC1: a credit may never exceed the open amount
        if (adjustment.Kind == AdjustmentKind.Credit && total > openBefore)
            return (null, "ADJUSTMENT_OVER_OPEN", $"Credit {total:0.00} exceeds the invoice open amount {openBefore:0.00}.");

        var memo = $"{adjustment.Kind} memo {adjustment.Id} for invoice {invoice.Id}";
        var lines = adjustment.Kind == AdjustmentKind.Credit
            ? CreditMemoJournal(total, subTotal, taxTotal)   // Dr revenue + Dr VAT / Cr AR
            : DebitMemoJournal(total, subTotal, taxTotal);   // Dr AR / Cr revenue + Cr VAT
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted, memo,
            lines, DateTimeOffset.UtcNow, approver.Id);
        journals.Add(journal);

        invoices.MarkAdjustmentApproved(adjustment, approver.Id, journal.Id, taxTotal, total);
        audit.Log(approver, "approve", "adjustment", adjustmentId,
            $"{adjustment.Kind} memo {adjustmentId} approved: total {total:0.00} (net {subTotal:0.00}, tax {taxTotal:0.00}); journal {journal.Id}");
        audit.Emit(approver, "sales.adjustment.approved",
            new { adjustmentId, kind = adjustment.Kind.ToString(), invoiceId = invoice.Id, total, journalId = journal.Id });
        try
        {
            invoices.Save();
        }
        catch (DbUpdateConcurrencyException)
        {
            // review 1a (same family): the guarded Draft→Approved UPDATE matched zero rows —
            // a concurrent officer approved this memo against our stale snapshot; no second
            // reversing journal is written
            return (null, "ADJUSTMENT_ALREADY_APPROVED",
                $"Adjustment {adjustmentId} was already approved; the retry carried a stale snapshot and wrote nothing.");
        }
        if (ownsTx) tx!.Commit();
        return (adjustment, null, null);
    }

    // invoice posting: Dr AR total / Cr revenue net / Cr VAT tax (VAT line omitted when zero)
    private static JournalLine[] InvoiceJournal(decimal total, decimal net, decimal tax) =>
    [
        new JournalLine(ArAccount, "Accounts Receivable", total, 0),
        new JournalLine(RevenueAccount, "Sales Revenue", 0, net),
        .. tax != 0 ? new[] { new JournalLine(OutputVatAccount, "VAT Payable", 0, tax) } : [],
    ];

    // credit memo (046): reverses the sale — Dr revenue net / Dr VAT tax / Cr AR total
    private static JournalLine[] CreditMemoJournal(decimal total, decimal net, decimal tax) =>
    [
        new JournalLine(RevenueAccount, "Sales Revenue", net, 0),
        .. tax != 0 ? new[] { new JournalLine(OutputVatAccount, "VAT Payable", tax, 0) } : [],
        new JournalLine(ArAccount, "Accounts Receivable", 0, total),
    ];

    // debit memo (046): raises the receivable — Dr AR total / Cr revenue net / Cr VAT tax
    private static JournalLine[] DebitMemoJournal(decimal total, decimal net, decimal tax) =>
    [
        new JournalLine(ArAccount, "Accounts Receivable", total, 0),
        new JournalLine(RevenueAccount, "Sales Revenue", 0, net),
        .. tax != 0 ? new[] { new JournalLine(OutputVatAccount, "VAT Payable", 0, tax) } : [],
    ];

    // 048 AC1: tax per line from the revision effective on the given date; AwayFromZero; the
    // resolved revision id + version pin the component on the line so replays never re-derive it
    private (string code, string message)? ResolveLinesTax(
        IReadOnlyList<InvoiceLine> lines, DateOnly date,
        Action<InvoiceLine, string, int, decimal> pin, out decimal taxTotal)
    {
        taxTotal = 0;
        foreach (var line in lines)
        {
            var error = ResolveOneTax(line.ItemSku, line.TaxCodeId, line.LineNet, date, pin, line, out var tax);
            if (error is not null) return error;
            taxTotal += tax;
        }
        return null;
    }

    private (string code, string message)? ResolveLinesTax(
        IReadOnlyList<AdjustmentLine> lines, DateOnly date,
        Action<AdjustmentLine, string, int, decimal> pin, out decimal taxTotal)
    {
        taxTotal = 0;
        foreach (var line in lines)
        {
            var error = ResolveOneTax(line.ItemSku, line.TaxCodeId, line.LineNet, date, pin, line, out var tax);
            if (error is not null) return error;
            taxTotal += tax;
        }
        return null;
    }

    private (string code, string message)? ResolveOneTax<T>(
        string sku, string? citation, decimal lineNet, DateOnly date,
        Action<T, string, int, decimal> pin, T line, out decimal tax)
    {
        var cited = taxCodes.Find(citation ?? DefaultTaxCode)
            ?? taxCodes.ByCode(citation ?? DefaultTaxCode).FirstOrDefault();
        if (cited is null)
        {
            tax = 0;
            return ("TAX_CODE_NOT_FOUND", $"Tax code {citation ?? DefaultTaxCode} does not exist (line {sku}).");
        }
        var revision = taxCodes.EffectiveFor(cited.Code, date);
        if (revision is null)
        {
            tax = 0;
            return ("TAX_CODE_NOT_EFFECTIVE", $"No revision of tax code {cited.Code} is effective on {date:yyyy-MM-dd} (line {sku}).");
        }
        // fix 72931d8 semantics: Zero/Exempt publish a 0 component regardless of anything else
        var rate = revision.Basis is TaxBasis.Zero or TaxBasis.Exempt ? 0m : revision.RatePct;
        tax = decimal.Round(lineNet * rate / 100m, 2, MidpointRounding.AwayFromZero);
        pin(line, revision.Id, revision.Version, tax);
        return null;
    }

    // a posted document that covers every ordered qty flips the SO to Invoiced. The just-posted
    // invoice is passed explicitly: its Posted status lives only in the change tracker until
    // SaveChanges, so the SQL-side Posted filter cannot see it yet.
    private void FlipSoIfFullyBilled(Invoice posted)
    {
        var so = orders.Find(posted.SalesOrderId);
        if (so is null) return;
        var billed = invoices.AllInvoices(InvoiceStatus.Posted, null)
            .Where(i => i.SalesOrderId == posted.SalesOrderId && i.Id != posted.Id)
            .SelectMany(i => i.Lines).Concat(posted.Lines)
            .GroupBy(l => l.ItemSku)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        if (so.Lines.All(l => billed.TryGetValue(l.ItemSku, out var got) && got >= l.Qty))
            orders.MarkInvoiced(so);
    }
}
