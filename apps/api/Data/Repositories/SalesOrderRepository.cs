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

    public void AddShipment(Shipment shipment) => db.Shipments.Add(shipment);

    public Dictionary<string, decimal> ShippedBySku(string soId)
    {
        var sums = db.Shipments.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId)
            .GroupBy(sh => sh.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        // shipments added earlier in this request are tracked but unsaved — include them too
        foreach (var sh in db.Shipments.Local.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId))
            sums[sh.ItemSku] = sums.GetValueOrDefault(sh.ItemSku) + sh.Qty;
        return sums;
    }

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
