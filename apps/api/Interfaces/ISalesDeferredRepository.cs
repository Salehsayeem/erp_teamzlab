using Erp.Api;

namespace Erp.Api.Interfaces;

// S7/S8 aggregate rows: sales returns, return receipts, refund requests, receipt candidates.
// One scoped repository per tenant+entity, same pattern as the other sales repos.
public interface ISalesDeferredRepository
{
    // returns (061)
    void AddReturn(SalesReturn ret);
    SalesReturn? FindReturn(string returnId);
    List<SalesReturn> Returns();
    List<SalesReturn> ReturnsForOrder(string soId);
    void SetReturnStatus(SalesReturn ret, ReturnStatus status);
    void AddReturnReceipt(ReturnReceipt receipt);
    List<ReturnReceipt> ReceiptsFor(string returnId);
    // refunds (062)
    RefundRequest? FindRefund(string refundId);
    RefundRequest? FindRefundByAdjustment(string adjustmentId);
    List<RefundRequest> FindRefunds();
    void AddRefund(RefundRequest refund);
    void SetRefundStatus(RefundRequest refund, RefundStatus status, string? approverId, string? rejectionReason = null);
    // receipt candidates (054)
    void AddCandidate(ReceiptCandidate candidate);
    ReceiptCandidate? FindCandidate(string candidateId);
    List<ReceiptCandidate> Candidates();
    void SetCandidatePayer(ReceiptCandidate candidate, string customerId);
    // rebates (063)
    void AddAgreement(RebateAgreement agreement);
    RebateAgreement? FindAgreement(string agreementId);
    List<RebateAgreement> Agreements();
    RebateAccrual? FindAccrual(string agreementId, string period);
    void AddAccrual(RebateAccrual accrual);
    void SetAccrual(RebateAccrual accrual, decimal accruedAmount, string eligibleTxnJson);
    void SetAccrualApproved(RebateAccrual accrual, string approverId);
    void AddClaim(RebateClaim claim);
    RebateClaim? FindClaim(string claimId);
    List<RebateClaim> ClaimsFor(string agreementId, string period);
    void SetClaimStatus(RebateClaim claim, RebateClaimStatus status, string? approverId);
    int Save();
}
