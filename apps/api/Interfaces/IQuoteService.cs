using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IQuoteService
{
    object List();
    (object? data, string? code, string? message) Create(CurrentUser u, Dtos.CreateQuoteRequest request);
    (object? data, string? code, string? message) CreateRevision(CurrentUser u, string quoteId, Dtos.CreateQuoteRevisionRequest request);
    (object? data, string? code, string? message) ApproveRevision(CurrentUser u, string quoteId, int revision, string? reason);
    (object? data, bool replayed, string? code, string? message) AcceptRevision(CurrentUser u, string quoteId, int revision, Dtos.AcceptQuoteRequest request);
    (object? data, string? code, string? message) Revalidate(CurrentUser u, string quoteId, Dtos.RevalidateQuoteRequest request);
    (object? data, string? code, string? message) Detail(string quoteId);
}
