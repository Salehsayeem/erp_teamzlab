using Erp.Api;
using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Receipt candidates + AR reconciliation (ERP-SALES-054/055/056). Candidates preserve the
// bank/provider reference and raw amount/currency; an unmatched candidate stays Unapplied and
// never closes an invoice. The reconciliation view is a READ over finance state (journal
// presence + approved adjustments) — allocation itself is finance-owned (055).
public sealed class ReceiptCandidatesService(
    ISalesDeferredRepository deferred, IInvoiceRepository invoices, ICustomerRepository customers,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IReceiptCandidatesService
{
    public (object? data, string? code, string? message) Create(CurrentUser u, string? customerId, string bankRef, decimal amount, string currency, DateOnly valueDate, string channel)
    {
        if (!u.Has("receipts-candidates:write")) return (null, "FORBIDDEN", "receipts-candidates:write permission required.");
        if (customerId is not null && !customers.Exists(customerId))
            return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        var candidate = new ReceiptCandidate(numbers.Next("RCP"), ec.TenantId, ec.EntityId, customerId,
            bankRef, amount, currency, valueDate, channel, ReceiptCandidateStatus.Unapplied, null, u.Id, DateTimeOffset.UtcNow);
        deferred.AddCandidate(candidate);
        audit.Log(u, "create", "receipt_candidate", candidate.Id,
            $"{amount:0.00} {currency} via {channel} ({bankRef}){(customerId is null ? " — unmatched" : "")}");
        deferred.Save();
        return (View(candidate), null, null);
    }

    // 054 edge: the payer link is added later WITHOUT losing the bank reference; the candidate
    // stays Unapplied — only the finance module's allocation can apply money (055)
    public (object? data, string? code, string? message) LinkPayer(CurrentUser u, string candidateId, string customerId)
    {
        if (!u.Has("receipts-candidates:write")) return (null, "FORBIDDEN", "receipts-candidates:write permission required.");
        var candidate = deferred.FindCandidate(candidateId);
        if (candidate is null) return (null, "RECEIPT_CANDIDATE_NOT_FOUND", "Receipt candidate not found.");
        if (!customers.Exists(customerId))
            return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        deferred.SetCandidatePayer(candidate, customerId);
        audit.Log(u, "link_payer", "receipt_candidate", candidateId, $"Payer {customerId} linked");
        deferred.Save();
        return (View(deferred.FindCandidate(candidateId)!), null, null);
    }

    public (object? data, string? code, string? message) List() =>
        (deferred.Candidates().Select(View).ToList(), null, null);

    // 056: one explainable view — doc id / finance reference / currency / open amount / as-of;
    // missing finance state is Unresolved, open 0 after approved credits is Settled. Nothing
    // here mutates the invoice: candidates never settle documents (054 AC2/056 AC2).
    public (object? data, string? code, string? message) Reconciliation(string? asOf)
    {
        var asOfDate = string.IsNullOrWhiteSpace(asOf)
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : DateOnly.TryParse(asOf, out var parsed) ? parsed : DateOnly.MinValue;
        if (asOfDate == DateOnly.MinValue)
            return (null, "VALIDATION_ERROR", "asOf must be a valid date (YYYY-MM-DD) when supplied.");
        var posted = invoices.AllInvoices(InvoiceStatus.Posted, null);
        var rows = posted.OrderBy(i => i.Id).Select(i =>
        {
            var open = decimal.Round(
                CreditMath.InvoiceOpen(i, invoices.AdjustmentsForInvoice(i.Id)), 2, MidpointRounding.AwayFromZero);
            var status = i.JournalId is null
                ? "Unresolved"          // posted with no finance reference — flagged, never settled
                : open == 0 ? "Settled" // finance said so: approved credits brought it to zero
                : "Resolved";
            return new
            {
                docId = i.Id, docKind = "invoice", financeRef = i.JournalId, currency = i.Currency,
                invoiceDate = i.InvoiceDate, dueDate = i.DueDate, openAmount = open, asOf = asOfDate, status,
            };
        }).ToList();
        var candidates = deferred.Candidates();
        return (new
        {
            asOf = asOfDate,
            totalOpen = decimal.Round(rows.Sum(r => r.openAmount), 2, MidpointRounding.AwayFromZero),
            unresolvedCount = rows.Count(r => r.status == "Unresolved"),
            rows,
            unappliedCandidates = new
            {
                count = candidates.Count(c => c.Status == ReceiptCandidateStatus.Unapplied),
                total = decimal.Round(candidates.Where(c => c.Status == ReceiptCandidateStatus.Unapplied).Sum(c => c.Amount), 2, MidpointRounding.AwayFromZero),
            },
        }, null, null);
    }

    private static object View(ReceiptCandidate c) => new
    {
        c.Id, c.CustomerId, c.BankRef, c.Amount, c.Currency, c.ValueDate, c.Channel, c.Status,
        c.PayerRef, c.CreatedBy, c.CreatedAt,
    };
}
