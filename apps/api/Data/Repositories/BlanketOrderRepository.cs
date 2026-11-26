using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class BlanketOrderRepository(ErpDbContext db, EntityContext ec) : IBlanketOrderRepository
{
    private IQueryable<BlanketOrder> Scoped() =>
        db.BlanketOrders.Where(b => b.TenantId == ec.TenantId && b.LegalEntityId == ec.EntityId);

    public BlanketOrder? Find(string id) => Scoped().FirstOrDefault(b => b.Id == id);
    public List<BlanketOrder> All() => Scoped().OrderBy(b => b.Id).ToList();
    public void Add(BlanketOrder order) => db.BlanketOrders.Add(order);

    public List<BlanketRelease> ReleasesFor(string blanketOrderId) =>
        db.BlanketReleases.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId
                && r.BlanketOrderId == blanketOrderId)
            .OrderBy(r => r.ReleasedAt).ToList();

    public void AddRelease(BlanketRelease release) => db.BlanketReleases.Add(release);

    public void SetField(object entity, string name, object value) =>
        db.Entry(entity).Property(name).CurrentValue = value;

    public int Save() => db.SaveChanges();
}
