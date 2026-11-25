using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// P3 change-control reads/writes over purchase orders + change/ack rows — own scoped access (R14).
public sealed class PurchaseOrderChangeRepository(ErpDbContext db, EntityContext ec) : IPurchaseOrderChangeRepository
{
    private IQueryable<PurchaseOrder> PoScoped() =>
        db.PurchaseOrders.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);

    private IQueryable<PurchaseOrderChange> ChangesScoped() =>
        db.PurchaseOrderChanges.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public PurchaseOrder? FindPo(string poId) =>
        PoScoped().Include(p => p.Lines).FirstOrDefault(p => p.Id == poId);

    public PurchaseOrderChange? FindChange(string poId, string changeId) =>
        ChangesScoped().FirstOrDefault(c => c.PoId == poId && c.Id == changeId);

    public List<PurchaseOrderChange> ChangesFor(string poId) =>
        ChangesScoped().Where(c => c.PoId == poId).OrderBy(c => c.ChangeNo).ToList();

    public void AddChange(PurchaseOrderChange change) => db.PurchaseOrderChanges.Add(change);

    public PurchaseOrderAcknowledgement? FindAck(string poId, string supplierId, int orderVersion) =>
        db.PurchaseOrderAcknowledgements.FirstOrDefault(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId
            && a.PoId == poId && a.SupplierId == supplierId && a.OrderVersion == orderVersion);

    public void AddAck(PurchaseOrderAcknowledgement ack) => db.PurchaseOrderAcknowledgements.Add(ack);

    public bool HasPendingChange(string poId) =>
        ChangesScoped().Any(c => c.PoId == poId && c.Status == PoChangeStatus.Pending);

    public void SetField(object entity, string name, object value) =>
        db.Entry(entity).Property(name).CurrentValue = value;

    public int Save() => db.SaveChanges();
}
