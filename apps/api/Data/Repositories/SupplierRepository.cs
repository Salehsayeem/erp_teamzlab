using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Every query filters TenantId + LegalEntityId from the scoped EntityContext (task-2 §3)
public sealed class SupplierRepository(ErpDbContext db, EntityContext ec) : ISupplierRepository
{
    private IQueryable<Supplier> Scoped() =>
        db.Suppliers.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    public List<Supplier> All() => Scoped().OrderBy(s => s.Id).ToList();

    public bool Exists(string supplierId) => Scoped().Any(s => s.Id == supplierId);

    public void Add(Supplier supplier) => db.Suppliers.Add(supplier);

    public int Save() => db.SaveChanges();
}
