using Erp.Api.Interfaces;
using Erp.Api.Data;
using Erp.Api.Dtos;

namespace Erp.Api.Services;

public sealed class SupplierService(Db db) : ISupplierService
{
    public object List() =>
        db.WithLock(() => db.Suppliers.Select(s => new { s.Id, s.Name, s.TaxId, s.Email, s.CreatedAt, s.CreatedBy }).ToList());

    public (object data, string? code, string? message) Create(CurrentUser u, CreateSupplierRequest req) =>
        db.WithLock(() =>
        {
            var supplier = new Supplier(db.NextDoc("SUP"), db.Tenant.Id, db.Entity.Id, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
            db.Suppliers.Add(supplier);
            Audit.Log(db, u, "create", "supplier", supplier.Id, $"Supplier '{req.Name}' created");
            return ((object)supplier, (string?)null, (string?)null);
        });
}
