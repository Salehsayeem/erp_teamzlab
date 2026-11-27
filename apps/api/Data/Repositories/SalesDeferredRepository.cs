using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class SalesDeferredRepository(ErpDbContext db, EntityContext ec) : ISalesDeferredRepository
{
    private IQueryable<T> Scoped<T>() where T : class =>
        db.Set<T>().Where(e => EF.Property<string>(e, "TenantId") == ec.TenantId
            && EF.Property<string>(e, "LegalEntityId") == ec.EntityId);

    public void AddReturn(SalesReturn ret)
    {
        db.SalesReturns.Add(ret);
        foreach (var line in ret.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public SalesReturn? FindReturn(string returnId) =>
        db.SalesReturns.Include(r => r.Lines).FirstOrDefault(r =>
            r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.Id == returnId);

    public List<SalesReturn> Returns() =>
        db.SalesReturns.Include(r => r.Lines).Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId)
            .OrderBy(r => r.Id).ToList();

    public List<SalesReturn> ReturnsForOrder(string soId) =>
        db.SalesReturns.Include(r => r.Lines).Where(r =>
            r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.SalesOrderId == soId).ToList();

    public void SetReturnStatus(SalesReturn ret, ReturnStatus status) =>
        db.Entry(ret).Property(nameof(SalesReturn.Status)).CurrentValue = status;

    public void AddReturnReceipt(ReturnReceipt receipt) => db.ReturnReceipts.Add(receipt);

    public List<ReturnReceipt> ReceiptsFor(string returnId) =>
        db.ReturnReceipts.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.ReturnId == returnId)
            .OrderBy(r => r.ReceivedAt).ToList();

    public RefundRequest? FindRefund(string refundId) =>
        Scoped<RefundRequest>().FirstOrDefault(r => r.Id == refundId);

    public RefundRequest? FindRefundByAdjustment(string adjustmentId) =>
        Scoped<RefundRequest>().FirstOrDefault(r => r.AdjustmentId == adjustmentId);

    public List<RefundRequest> FindRefunds() =>
        Scoped<RefundRequest>().OrderBy(r => r.RequestedAt).ToList();

    public void AddRefund(RefundRequest refund) => db.RefundRequests.Add(refund);

    public void SetRefundStatus(RefundRequest refund, RefundStatus status, string? approverId, string? rejectionReason = null)
    {
        db.Entry(refund).Property(nameof(RefundRequest.Status)).CurrentValue = status;
        if (approverId is not null)
        {
            db.Entry(refund).Property(nameof(RefundRequest.ApprovedBy)).CurrentValue = approverId;
            db.Entry(refund).Property(nameof(RefundRequest.ApprovedAt)).CurrentValue = DateTimeOffset.UtcNow;
        }
        if (rejectionReason is not null)
            db.Entry(refund).Property(nameof(RefundRequest.RejectionReason)).CurrentValue = rejectionReason;
    }

    public void AddCandidate(ReceiptCandidate candidate) => db.ReceiptCandidates.Add(candidate);

    public ReceiptCandidate? FindCandidate(string candidateId) =>
        Scoped<ReceiptCandidate>().FirstOrDefault(c => c.Id == candidateId);

    public List<ReceiptCandidate> Candidates() =>
        Scoped<ReceiptCandidate>().OrderBy(c => c.Id).ToList();

    public void SetCandidatePayer(ReceiptCandidate candidate, string customerId) =>
        db.Entry(candidate).Property(nameof(ReceiptCandidate.CustomerId)).CurrentValue = customerId;

    // --- rebates (063) ---

    public void AddAgreement(RebateAgreement agreement) => db.RebateAgreements.Add(agreement);

    public RebateAgreement? FindAgreement(string agreementId) =>
        Scoped<RebateAgreement>().FirstOrDefault(a => a.Id == agreementId);

    public List<RebateAgreement> Agreements() =>
        Scoped<RebateAgreement>().OrderBy(a => a.Id).ToList();

    public RebateAccrual? FindAccrual(string agreementId, string period) =>
        Scoped<RebateAccrual>().FirstOrDefault(a => a.AgreementId == agreementId && a.Period == period);

    public void AddAccrual(RebateAccrual accrual) => db.RebateAccruals.Add(accrual);

    public void SetAccrual(RebateAccrual accrual, decimal accruedAmount, string eligibleTxnJson)
    {
        db.Entry(accrual).Property(nameof(RebateAccrual.AccruedAmount)).CurrentValue = accruedAmount;
        db.Entry(accrual).Property(nameof(RebateAccrual.EligibleTxnJson)).CurrentValue = eligibleTxnJson;
        db.Entry(accrual).Property(nameof(RebateAccrual.CalculatedAt)).CurrentValue = DateTimeOffset.UtcNow;
    }

    public void SetAccrualApproved(RebateAccrual accrual, string approverId)
    {
        db.Entry(accrual).Property(nameof(RebateAccrual.Status)).CurrentValue = RebateAccrualStatus.Approved;
        db.Entry(accrual).Property(nameof(RebateAccrual.ApprovedAmount)).CurrentValue = accrual.AccruedAmount;
        db.Entry(accrual).Property(nameof(RebateAccrual.ApprovedBy)).CurrentValue = approverId;
        db.Entry(accrual).Property(nameof(RebateAccrual.ApprovedAt)).CurrentValue = DateTimeOffset.UtcNow;
    }

    public void AddClaim(RebateClaim claim) => db.RebateClaims.Add(claim);

    public RebateClaim? FindClaim(string claimId) =>
        Scoped<RebateClaim>().FirstOrDefault(c => c.Id == claimId);

    public List<RebateClaim> Claims() =>
        Scoped<RebateClaim>().OrderBy(c => c.Id).ToList();

    public List<RebateClaim> ClaimsFor(string agreementId, string period) =>
        Scoped<RebateClaim>().Where(c => c.AgreementId == agreementId && c.Period == period).ToList();

    public void SetClaimStatus(RebateClaim claim, RebateClaimStatus status, string? approverId, string? rejectionReason = null)
    {
        db.Entry(claim).Property(nameof(RebateClaim.Status)).CurrentValue = status;
        if (approverId is not null)
        {
            db.Entry(claim).Property(nameof(RebateClaim.ApprovedBy)).CurrentValue = approverId;
            db.Entry(claim).Property(nameof(RebateClaim.ApprovedAt)).CurrentValue = DateTimeOffset.UtcNow;
        }
        if (rejectionReason is not null)
            db.Entry(claim).Property(nameof(RebateClaim.RejectionReason)).CurrentValue = rejectionReason;
    }

    public int Save() => db.SaveChanges();
}
