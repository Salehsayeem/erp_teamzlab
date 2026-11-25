using Erp.Api;

namespace Erp.Api.Interfaces;

// 054: unapplied payment receipt candidates. Allocation stays FINANCE-owned (055) — this
// surface only records candidates and reflects the finance-owned view back (056 read).
public interface IReceiptCandidatesService
{
    (object? data, string? code, string? message) Create(CurrentUser u, string? customerId, string bankRef, decimal amount, string currency, DateOnly valueDate, string channel);
    (object? data, string? code, string? message) LinkPayer(CurrentUser u, string candidateId, string customerId);
    (object? data, string? code, string? message) List();
    (object? data, string? code, string? message) Reconciliation(string? asOf);
}
