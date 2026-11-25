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

    public int Save() => db.SaveChanges();
}
