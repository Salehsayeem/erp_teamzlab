using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Quotes (ERP-SALES-021/022/023/030): immutable revisions on material change, maker-checker
// approvals above the configured threshold or the discount cap, acceptance pinned to one
// revision, idempotent on externalEventId, expiry with authorized revalidation.
public sealed class QuoteService(
    IQuoteRepository quotes, ICustomerRepository customers, IPriceResolutionService prices,
    IFinanceSettingRepository settings, INumberSeries numbers, IAuditRepository audit,
    EntityContext ec, ErpDbContext db) : IQuoteService
{
    private static decimal Gross(QuoteLineRequest l) => decimal.Round(l.Qty * (l.UnitPrice ?? 0), 2, MidpointRounding.AwayFromZero);
    private static decimal Discount(QuoteLineRequest l) => decimal.Round(Gross(l) * l.DiscountPct / 100, 2, MidpointRounding.AwayFromZero);

    public object List() => quotes.Quotes().Select(q => new
    {
        q.Id, q.CustomerId, q.Currency, q.Terms, q.ExpiresAt, q.Status, q.CurrentRevision, q.CreatedBy, q.CreatedAt,
    }).ToList();

    public (object? data, string? code, string? message) Create(CurrentUser u, CreateQuoteRequest request)
    {
        if (!customers.Exists(request.CustomerId))
            return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        var quoteId = numbers.Next("QTE");
        var (revision, code, message) = BuildRevision(u, request.CustomerId, quoteId, $"{quoteId}:R1", 1, request.Lines);
        if (code is not null) return (null, code, message);

        var quote = new Quote(quoteId, ec.TenantId, ec.EntityId, request.CustomerId,
            request.Currency, request.Terms, request.ExpiresAt, QuoteStatus.Draft, 1, u.Id, DateTimeOffset.UtcNow);
        quotes.AddQuote(quote);
        quotes.AddRevision(revision!);
        audit.Log(u, "create", "quote", quote.Id, $"Quote created for customer {request.CustomerId} ({revision!.Total:0.00} {request.Currency})");
        quotes.Save();
        return (QuoteView(quote, revision), null, null);
    }

    public (object? data, string? code, string? message) CreateRevision(CurrentUser u, string quoteId, CreateQuoteRevisionRequest request)
    {
        var quote = quotes.FindQuote(quoteId);
        if (quote is null) return (null, "QUOTE_NOT_FOUND", "Quote not found.");
        var revisionNo = quote.CurrentRevision + 1;
        var (revision, code, message) = BuildRevision(u, quote.CustomerId, quoteId, $"{quoteId}:R{revisionNo}", revisionNo, request.Lines);
        if (code is not null) return (null, code, message);

        // 022 AC1: material change → new revision with a distinct id; the prior revision and
        // its approvals are retained untouched (a fresh revision starts with zero approvals,
        // which is exactly how a post-approval material edit invalidates them, 023 AC2)
        quotes.AddRevision(revision!);
        quotes.SetQuoteCurrentRevision(quote, revisionNo, request.Terms, request.ExpiresAt);
        audit.Log(u, "revise", "quote", quote.Id,
            $"Revision {revisionNo} created ({revision!.Total:0.00}); rationale: {request.Rationale ?? "none given"}");
        quotes.Save();
        return (QuoteView(quote, revision), null, null);
    }

    public (object? data, string? code, string? message) ApproveRevision(CurrentUser u, string quoteId, int revision, string? reason)
    {
        var quote = quotes.FindQuote(quoteId);
        if (quote is null) return (null, "QUOTE_NOT_FOUND", "Quote not found.");
        var rev = quotes.FindRevision(quoteId, revision);
        if (rev is null) return (null, "QUOTE_REVISION_NOT_FOUND", "Quote revision not found.");
        if (rev.Status != QuoteRevisionStatus.PendingApproval)
            return (null, "QUOTE_STATE_ERROR", $"Only pending quote revisions can be approved (current: {rev.Status}).");
        // maker-checker (023): approver ≠ issuer; permission gate inside so the SoD check wins
        if (rev.CreatedBy == u.Id)
            return (null, "QUOTE_APPROVAL_ERROR", "Maker-checker: the issuer cannot approve their own quote revision.");
        if (!u.Has("quotes:approve")) return (null, "FORBIDDEN", "quotes:approve permission required.");

        if (reason is not null)
        {
            quotes.SetRevisionStatus(rev, QuoteRevisionStatus.Rejected, u.Id);
            audit.Log(u, "reject", "quote", quote.Id, $"Revision {revision} rejected: {reason}");
            quotes.Save();
            return (RevisionView(quoteId, rev, quotes.ApprovalsFor(rev.Id)), null, null);
        }

        quotes.AddApproval(new QuoteApproval($"{rev.Id}:{u.Id}", ec.TenantId, ec.EntityId, rev.Id, u.Id,
            QuoteApprovalStatus.Approved, null, DateTimeOffset.UtcNow));
        quotes.SetRevisionStatus(rev, QuoteRevisionStatus.Approved, u.Id);
        audit.Log(u, "approve", "quote", quote.Id, $"Revision {revision} approved by {u.Email}");
        quotes.Save();
        return (RevisionView(quoteId, rev, quotes.ApprovalsFor(rev.Id)), null, null);
    }

    public (object? data, bool replayed, string? code, string? message) AcceptRevision(CurrentUser u, string quoteId, int revision, AcceptQuoteRequest request)
    {
        var quote = quotes.FindQuote(quoteId);
        if (quote is null) return (null, false, "QUOTE_NOT_FOUND", "Quote not found.");
        var rev = quotes.FindRevision(quoteId, revision);
        if (rev is null) return (null, false, "QUOTE_REVISION_NOT_FOUND", "Quote revision not found.");

        // 030 AC2: duplicate external acceptance event → idempotent replay of the same acceptance
        QuoteAcceptance? existing = null;
        if (request.ExternalEventId is not null)
            existing = quotes.FindAcceptance(rev.Id, request.ExternalEventId);
        if (existing is not null) return (AcceptanceView(existing, idempotent: true), true, null, null);

        if (rev.Status != QuoteRevisionStatus.Approved)
            return (null, false, "QUOTE_STATE_ERROR", $"Only approved quote revisions can be accepted (current: {rev.Status}).");
        if (quote.Status == QuoteStatus.Accepted)
            return (null, false, "QUOTE_STATE_ERROR", "This quote already has an accepted revision (030 AC1: it never migrates).");
        // 021 AC1: expired quote cannot be accepted without authorized revalidation
        if (quote.ExpiresAt is { } expiry && expiry < DateOnly.FromDateTime(DateTime.UtcNow))
            return (null, false, "QUOTE_EXPIRED", $"Quote expired {expiry:yyyy-MM-dd} — authorized revalidation required before acceptance.");

        var token = request.ExternalEventId ?? $"{Guid.NewGuid():N}"[..12];
        var acceptance = new QuoteAcceptance($"{rev.Id}:{token}", ec.TenantId, ec.EntityId, quote.Id, rev.Id,
            request.Method, request.ExternalEventId, request.EvidenceRef, u.Id, DateTimeOffset.UtcNow);
        try
        {
            quotes.AddAcceptance(acceptance);
            quotes.SetQuoteStatus(quote, QuoteStatus.Accepted);
            audit.Log(u, "accept", "quote", quote.Id, $"Revision {revision} accepted ({request.Method}); evidence {request.EvidenceRef ?? "none"}");
            audit.Emit(u, "sales.quote.accepted", new { quoteId, revision, method = request.Method, acceptedBy = u.Id });
            quotes.Save();
        }
        catch (DbUpdateConcurrencyException)
        {
            // status token caught a concurrent double-accept — the winner's acceptance stands
            return (null, false, "QUOTE_STATE_ERROR", "Quote was accepted concurrently by another session.");
        }
        return (AcceptanceView(acceptance, idempotent: false), false, null, null);
    }

    public (object? data, string? code, string? message) Revalidate(CurrentUser u, string quoteId, RevalidateQuoteRequest request)
    {
        if (!u.Has("quotes:approve")) return (null, "FORBIDDEN", "quotes:approve permission required.");
        var quote = quotes.FindQuote(quoteId);
        if (quote is null) return (null, "QUOTE_NOT_FOUND", "Quote not found.");
        // instance-level maker-checker (brief §3: quotes:approve acts are approver ≠ issuer)
        if (quote.CreatedBy == u.Id)
            return (null, "QUOTE_APPROVAL_ERROR", "Maker-checker: the quote issuer cannot revalidate their own expired quote.");
        if (request.ExpiresAt < DateOnly.FromDateTime(DateTime.UtcNow))
            return (null, "VALIDATION_ERROR", "Revalidated expiry must be today or later.");
        db.Entry(quote).Property(nameof(Quote.ExpiresAt)).CurrentValue = request.ExpiresAt;
        audit.Log(u, "revalidate", "quote", quoteId, $"Expiry revalidated to {request.ExpiresAt:yyyy-MM-dd}: {request.Reason}");
        quotes.Save();
        return (QuoteView(quote, null), null, null);
    }

    public (object? data, string? code, string? message) Detail(string quoteId)
    {
        var quote = quotes.FindQuote(quoteId);
        if (quote is null) return (null, "QUOTE_NOT_FOUND", "Quote not found.");
        var revisions = quotes.RevisionsFor(quoteId)
            .Select(r => RevisionView(quoteId, r, quotes.ApprovalsFor(r.Id))).ToList();
        var acceptances = quotes.AcceptancesFor(quoteId).Select(a => AcceptanceView(a, idempotent: false)).ToList();
        var acceptedRevision = (int?)null;
        if (quotes.AcceptancesFor(quoteId).FirstOrDefault()?.RevisionId is { } rid)
            acceptedRevision = RevisionNoOf(rid);
        return (new
        {
            quote.Id, quote.CustomerId, quote.Currency, quote.Terms, quote.ExpiresAt, quote.Status,
            revision = quote.CurrentRevision, acceptedRevision,
            createdBy = quote.CreatedBy, createdAt = quote.CreatedAt,
            revisions, acceptances,
        }, null, null);
    }

    // shared line processing: validation → price resolution → caps → totals → revision row.
    // 023 AC1: total above the configured threshold → PendingApproval. 025 AC2: a line discount
    // above the cap also forces PendingApproval no matter how small the total (approver ≠ issuer).
    private (QuoteRevision? revision, string? code, string? message) BuildRevision(
        CurrentUser u, string customerId, string quoteId, string revisionId, int revisionNo, IReadOnlyList<QuoteLineRequest>? lines)
    {
        if (lines is null || lines.Count == 0)
            return (null, "VALIDATION_ERROR", "At least one quote line is required.");
        var cap = decimal.Parse(settings.Get("sales.discount_cap_pct")?.Value ?? "15");
        var threshold = decimal.Parse(settings.Get("quotes.approval_threshold")?.Value ?? "10000");
        var onDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var evidence = new List<object>();
        var priced = new List<(QuoteLineRequest req, QuoteLine line, decimal gross, decimal discount)>();

        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l.ItemSku)) return (null, "VALIDATION_ERROR", "Each quote line needs an item SKU.");
            if (l.Qty <= 0) return (null, "VALIDATION_ERROR", "Line qty must be greater than 0.");
            if (l.DiscountPct < 0 || l.DiscountPct >= 100)
                return (null, "VALIDATION_ERROR", "Discount percent must be >= 0 and < 100 — the net must stay positive.");
            decimal unitPrice;
            if (l.UnitPrice is { } manual)
            {
                unitPrice = manual;
                evidence.Add(new { itemSku = l.ItemSku, priceBookId = (string?)null, rule = "manual",
                    effectiveFrom = onDate, effectiveTo = (DateOnly?)null, baseValue = manual, sourceRef = "manual" });
            }
            else
            {
                var (res, code, message) = prices.Resolve(customerId, l.ItemSku, l.Qty, onDate);
                if (code is not null) return (null, code, message);
                unitPrice = res!.UnitPrice;
                evidence.Add(new { itemSku = l.ItemSku, priceBookId = res.PriceBookId, rule = res.Rule,
                    effectiveFrom = res.EffectiveFrom, effectiveTo = res.EffectiveTo, baseValue = res.BaseValue, sourceRef = res.SourceRef });
            }
            var req = l with { UnitPrice = unitPrice };
            priced.Add((req, new QuoteLine(l.ItemSku, $"Item {l.ItemSku}", l.Qty, unitPrice, l.DiscountPct),
                Gross(req), Discount(req)));
        }

        var subTotal = priced.Sum(p => p.gross);
        var discountTotal = priced.Sum(p => p.discount);
        var taxTotal = 0m; // quotes are pre-tax documents; the reconciliation formula stays explicit
        var total = subTotal - discountTotal + taxTotal;
        var capBreached = lines.Any(l => l.DiscountPct > cap);
        var needsApproval = total > threshold || capBreached;
        var status = needsApproval ? QuoteRevisionStatus.PendingApproval : QuoteRevisionStatus.Approved;

        var now = DateTimeOffset.UtcNow;
        var revision = new QuoteRevision(revisionId, ec.TenantId, ec.EntityId, quoteId, revisionNo, status,
            subTotal, discountTotal, taxTotal, total, JsonSerializer.Serialize(evidence, Idem.JsonOptions),
            u.Id, now, priced.Select(p => p.line).ToList(),
            needsApproval ? null : u.Id, needsApproval ? null : now); // auto-approved under threshold
        return (revision, null, null);
    }

    private static object QuoteView(Quote q, QuoteRevision? current) => new
    {
        q.Id, q.CustomerId, q.Currency, q.Terms, q.ExpiresAt, q.Status, revision = q.CurrentRevision,
        revisionStatus = current?.Status,
        subTotal = current?.SubTotal, discountTotal = current?.DiscountTotal, taxTotal = current?.TaxTotal, total = current?.Total,
        priceEvidence = current is null ? null : Deserialize(current.PriceEvidenceJson),
        createdBy = q.CreatedBy, createdAt = q.CreatedAt,
    };

    private static object RevisionView(string quoteId, QuoteRevision r, List<QuoteApproval> approvals) => new
    {
        id = r.Id, quoteId, r.Revision, r.Status, r.SubTotal, r.DiscountTotal, r.TaxTotal, r.Total,
        priceEvidence = Deserialize(r.PriceEvidenceJson),
        lines = r.Lines.Select(l => new { l.ItemSku, l.Description, l.Qty, l.UnitPrice, l.DiscountPct }),
        approvals = approvals.Select(a => new { a.ApproverId, a.Status, a.Reason, a.CreatedAt }),
        approvedBy = r.ApprovedBy, approvedAt = r.ApprovedAt, createdBy = r.CreatedBy, createdAt = r.CreatedAt,
    };

    private static object AcceptanceView(QuoteAcceptance a, bool idempotent) => new
    {
        a.Id, a.QuoteId, a.RevisionId, revision = RevisionOf(a.RevisionId), a.Method, a.ExternalEventId,
        a.EvidenceRef, a.AcceptedBy, a.AcceptedAt, idempotent,
    };

    // revision ids are "{quoteId}:R{n}" — the segment after the last ':' is "R<n>"
    private static int RevisionOf(string revisionId)
    {
        var idx = revisionId.LastIndexOf(':');
        return int.TryParse(revisionId.AsSpan(idx + 1).TrimStart('R'), out var n) ? n : 0;
    }

    private static int? RevisionNoOf(string revisionId)
    {
        var parsed = RevisionOf(revisionId);
        return parsed == 0 ? null : parsed;
    }

    private static object? Deserialize(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<object>(json, Idem.JsonOptions);
}
