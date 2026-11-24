namespace Erp.Api.Interfaces;

// Collections: reminders + disputes (ERP-SALES-058/059)
public interface ICollectionsRepository
{
    void AddReminder(CollectionReminder reminder);
    List<Dispute> DisputesForInvoice(string invoiceId);
    Dispute? FindDispute(string disputeId);
    void AddDispute(Dispute dispute);
    void MarkDisputeResolved(Dispute dispute, string outcome);
    int Save();
}
