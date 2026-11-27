using Erp.Api;

namespace Erp.Api.Interfaces;

// 063: rebate agreements with auditable accrual → claim; settlement above the approved
// accrual is refused absent a documented adjustment; 064: commission events ride the outbox
public interface IRebateService
{
    (object? data, string? code, string? message) CreateAgreement(CurrentUser u, string customerId, string basis, decimal ratePct, string accrualPeriod, DateOnly validFrom);
    (object? data, string? code, string? message) ListAgreements();
    (object? data, string? code, string? message) CalculateAccrual(CurrentUser u, string agreementId, string period);
    (object? data, string? code, string? message) ApproveAccrual(CurrentUser u, string agreementId, string period);
    (object? data, string? code, string? message) CreateClaim(CurrentUser u, string agreementId, string period, decimal amount, IReadOnlyList<string> eligibleTxnIds);
    (object? data, string? code, string? message) ListClaims();
    (object? data, string? code, string? message) ApproveClaim(CurrentUser u, string claimId, string? reason);
    (object? data, string? code, string? message) RejectClaim(CurrentUser u, string claimId, string? reason);
    (object? data, string? code, string? message) RecalculationPreview(string agreementId, string period, decimal newRatePct);
}
