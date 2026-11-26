using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Quotes + revisions + approvals + acceptances (021/022/023/030) — revisions are retained
// forever (022 AC1); acceptances pin one revision and never migrate
public sealed class QuoteRepository(ErpDbContext db, EntityContext ec) : IQuoteRepository
{
    private IQueryable<Quote> Scoped() =>
        db.Quotes.Where(q => q.TenantId == ec.TenantId && q.LegalEntityId == ec.EntityId);

    private IQueryable<QuoteRevision> RevScoped() =>
        db.QuoteRevisions.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    public List<Quote> Quotes() => Scoped().OrderBy(q => q.Id).ToList();

    public Quote? FindQuote(string quoteId) => Scoped().FirstOrDefault(q => q.Id == quoteId);

    public void AddQuote(Quote quote) => db.Quotes.Add(quote);

    public void SetQuoteStatus(Quote quote, QuoteStatus status) =>
        db.Entry(quote).Property(nameof(Quote.Status)).CurrentValue = status;

    public void SetQuoteCurrentRevision(Quote quote, int revision, string? terms, DateOnly? expiresAt)
    {
        db.Entry(quote).Property(nameof(Quote.CurrentRevision)).CurrentValue = revision;
        if (terms is not null) db.Entry(quote).Property(nameof(Quote.Terms)).CurrentValue = terms;
        if (expiresAt is not null) db.Entry(quote).Property(nameof(Quote.ExpiresAt)).CurrentValue = expiresAt;
    }

    public QuoteRevision? FindRevision(string revisionId) =>
        RevScoped().Include(r => r.Lines).FirstOrDefault(r => r.Id == revisionId);

    public QuoteRevision? FindRevision(string quoteId, int revision) =>
        RevScoped().Include(r => r.Lines).FirstOrDefault(r => r.QuoteId == quoteId && r.Revision == revision);

    public List<QuoteRevision> RevisionsFor(string quoteId) =>
        RevScoped().Include(r => r.Lines).Where(r => r.QuoteId == quoteId).OrderBy(r => r.Revision).ToList();

    public void AddRevision(QuoteRevision revision)
    {
        db.QuoteRevisions.Add(revision);
        foreach (var line in revision.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public List<QuoteApproval> ApprovalsFor(string revisionId) =>
        db.QuoteApprovals
            .Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId && a.RevisionId == revisionId)
            .OrderBy(a => a.CreatedAt).ToList();

    public void AddApproval(QuoteApproval approval) => db.QuoteApprovals.Add(approval);

    public void SetRevisionStatus(QuoteRevision revision, QuoteRevisionStatus status, string? approverId)
    {
        db.Entry(revision).Property(nameof(QuoteRevision.Status)).CurrentValue = status;
        if (approverId is not null)
        {
            db.Entry(revision).Property(nameof(QuoteRevision.ApprovedBy)).CurrentValue = approverId;
            db.Entry(revision).Property(nameof(QuoteRevision.ApprovedAt)).CurrentValue = DateTimeOffset.UtcNow;
        }
    }

    public QuoteAcceptance? FindAcceptance(string revisionId, string externalEventId) =>
        db.QuoteAcceptances.FirstOrDefault(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId
            && a.RevisionId == revisionId && a.ExternalEventId == externalEventId);

    public List<QuoteAcceptance> AcceptancesFor(string quoteId) =>
        db.QuoteAcceptances
            .Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId && a.QuoteId == quoteId)
            .OrderBy(a => a.AcceptedAt).ToList();

    public void AddAcceptance(QuoteAcceptance acceptance) => db.QuoteAcceptances.Add(acceptance);

    public int Save() => db.SaveChanges();
}
