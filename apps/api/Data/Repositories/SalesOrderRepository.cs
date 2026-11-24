using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Sales orders + shipments — same tracked-entity mutation pattern as the PO side
public sealed class SalesOrderRepository(ErpDbContext db, EntityContext ec) : ISalesOrderRepository
{
    private IQueryable<SalesOrder> Scoped() =>
        db.SalesOrders.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    private List<SalesOrder> Ordered(IQueryable<SalesOrder> query) =>
        query.Include(s => s.Lines).OrderBy(s => s.Id).ToList(); // owned lines live in so_lines

    public SalesOrder? Find(string soId) =>
        Ordered(Scoped().Where(s => s.Id == soId)).FirstOrDefault();

    public List<SalesOrder> All() => Ordered(Scoped());

    // 057 exposure basis (R-T7e): Confirmed and CreditHold orders are open commitments
    public List<SalesOrder> OpenForCustomer(string customerId) =>
        Ordered(Scoped().Where(s => s.CustomerId == customerId
            && (s.Status == SoStatus.Confirmed || s.Status == SoStatus.CreditHold)));

    public void Add(SalesOrder so)
    {
        db.SalesOrders.Add(so);
        foreach (var line in so.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public void MarkSubmitted(SalesOrder so) =>
        Set(so, nameof(SalesOrder.Status), SoStatus.PendingApproval);

    public void MarkConfirmed(SalesOrder so, string approverId)
    {
        Set(so, nameof(SalesOrder.Status), SoStatus.Confirmed);
        Set(so, nameof(SalesOrder.ApprovedBy), approverId);
        Set(so, nameof(SalesOrder.ApprovedAt), DateTimeOffset.UtcNow);
    }

    public void MarkRejected(SalesOrder so, string approverId, string reason)
    {
        Set(so, nameof(SalesOrder.Status), SoStatus.Rejected);
        Set(so, nameof(SalesOrder.ApprovedBy), approverId);
        Set(so, nameof(SalesOrder.ApprovedAt), DateTimeOffset.UtcNow);
        Set(so, nameof(SalesOrder.RejectionReason), reason);
    }

    public void MarkShipped(SalesOrder so) => Set(so, nameof(SalesOrder.Status), SoStatus.Shipped);

    public void MarkInvoiced(SalesOrder so) => Set(so, nameof(SalesOrder.Status), SoStatus.Invoiced);

    // 057 AC2: approval happened but the exposure gate held the order — ApprovedBy stamps the
    // officer who made the hold decision (release is a separate audited act)
    public void MarkCreditHold(SalesOrder so, string approverId)
    {
        Set(so, nameof(SalesOrder.Status), SoStatus.CreditHold);
        Set(so, nameof(SalesOrder.ApprovedBy), approverId);
        Set(so, nameof(SalesOrder.ApprovedAt), DateTimeOffset.UtcNow);
    }

    public void AddShipment(Shipment shipment) => db.Shipments.Add(shipment);

    // source events for billing — earliest first (invoice drafts pick the oldest unbilled one)
    public List<Shipment> ShipmentsFor(string soId) =>
        db.Shipments.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId)
            .OrderBy(sh => sh.Id).ToList();

    public Dictionary<string, decimal> ShippedBySku(string soId)
    {
        var rows = db.Shipments.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId)
            .Select(sh => new { sh.Id, sh.ItemSku, sh.Qty }).ToList();
        var sums = rows.GroupBy(sh => sh.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        // same hazard as ReceivedBySku: the query materializes committed shipments into .Local —
        // merge only rows it did not already return or later shipments flip the SO Shipped early
        var seen = rows.Select(sh => sh.Id).ToHashSet();
        foreach (var sh in db.Shipments.Local.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId))
            if (seen.Add(sh.Id))
                sums[sh.ItemSku] = sums.GetValueOrDefault(sh.ItemSku) + sh.Qty;
        return sums;
    }

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
