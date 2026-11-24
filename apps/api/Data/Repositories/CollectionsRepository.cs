using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class CollectionsRepository(ErpDbContext db, EntityContext ec) : ICollectionsRepository
{
    private IQueryable<Dispute> DisputeScope() =>
        db.Disputes.Where(d => d.TenantId == ec.TenantId && d.LegalEntityId == ec.EntityId);

    // 058 AC2: reminders are an append-only history — no update/delete path exists
    public void AddReminder(CollectionReminder reminder) => db.CollectionReminders.Add(reminder);

    public List<Dispute> DisputesForInvoice(string invoiceId) =>
        DisputeScope().Where(d => d.InvoiceId == invoiceId).OrderBy(d => d.Id).ToList();

    public Dispute? FindDispute(string disputeId) => DisputeScope().FirstOrDefault(d => d.Id == disputeId);

    public void AddDispute(Dispute dispute) => db.Disputes.Add(dispute);

    public void MarkDisputeResolved(Dispute dispute, string outcome)
    {
        db.Entry(dispute).Property(nameof(Dispute.Status)).CurrentValue = DisputeStatus.Resolved;
        db.Entry(dispute).Property(nameof(Dispute.Outcome)).CurrentValue = outcome;
    }

    public int Save() => db.SaveChanges();
}
