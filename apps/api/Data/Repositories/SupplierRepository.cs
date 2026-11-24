using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Every query filters TenantId + LegalEntityId from the scoped EntityContext (task-2 §3).
// T6 adds lifecycle status (047) and effective-dated eligibility gates (023/044).
public sealed class SupplierRepository(ErpDbContext db, EntityContext ec) : ISupplierRepository
{
    private IQueryable<Supplier> Scoped() =>
        db.Suppliers.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    private IQueryable<SupplierGate> GatesScoped() =>
        db.SupplierGates.Where(g => g.TenantId == ec.TenantId && g.LegalEntityId == ec.EntityId);

    public List<Supplier> All() => Scoped().OrderBy(s => s.Id).ToList();

    public bool Exists(string supplierId) => Scoped().Any(s => s.Id == supplierId);

    public Supplier? Find(string supplierId) => Scoped().FirstOrDefault(s => s.Id == supplierId);

    public void Add(Supplier supplier) => db.Suppliers.Add(supplier);

    public void SetSupplierStatus(object supplier, SupplierLifecycleStatus status, string? statusReason)
    {
        Set(supplier, nameof(Supplier.Status), status);
        Set(supplier, nameof(Supplier.StatusReason), statusReason);
    }

    public List<SupplierGate> GatesFor(string supplierId) =>
        GatesScoped().Where(g => g.SupplierId == supplierId).OrderBy(g => g.Id).ToList();

    public SupplierGate? FindGate(string supplierId, string gateCode, DateOnly effectiveFrom) =>
        GatesScoped().FirstOrDefault(g => g.SupplierId == supplierId && g.GateCode == gateCode && g.EffectiveFrom == effectiveFrom);

    public void AddGate(SupplierGate gate) => db.SupplierGates.Add(gate);

    public void SetGateException(object gate, string exceptionBy, DateTimeOffset exceptionUntil)
    {
        Set(gate, nameof(SupplierGate.ExceptionBy), exceptionBy);
        Set(gate, nameof(SupplierGate.ExceptionUntil), exceptionUntil);
    }

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
