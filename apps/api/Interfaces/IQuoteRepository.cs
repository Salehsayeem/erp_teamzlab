using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IQuoteRepository
{
    List<Quote> Quotes();
    Quote? FindQuote(string quoteId);
    void AddQuote(Quote quote);
    void SetQuoteStatus(Quote quote, QuoteStatus status);
    void SetQuoteCurrentRevision(Quote quote, int revision, string? terms, DateOnly? expiresAt);
    QuoteRevision? FindRevision(string revisionId);
    QuoteRevision? FindRevision(string quoteId, int revision);
    List<QuoteRevision> RevisionsFor(string quoteId);
    void AddRevision(QuoteRevision revision);
    List<QuoteApproval> ApprovalsFor(string revisionId);
    void AddApproval(QuoteApproval approval);
    void SetRevisionStatus(QuoteRevision revision, QuoteRevisionStatus status, string? approverId);
    QuoteAcceptance? FindAcceptance(string revisionId, string externalEventId);
    List<QuoteAcceptance> AcceptancesFor(string quoteId);
    void AddAcceptance(QuoteAcceptance acceptance);
    int Save();
}
