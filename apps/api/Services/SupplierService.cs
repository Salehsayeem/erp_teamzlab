using Erp.Api.Interfaces;
using Erp.Api.Dtos;

namespace Erp.Api.Services;

public sealed class SupplierService(
    ISupplierRepository suppliers, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : ISupplierService
{
    public object List() =>
        suppliers.All().Select(s => new { s.Id, s.Name, s.TaxId, s.Email, s.CreatedAt, s.CreatedBy }).ToList();

    public (object data, string? code, string? message) Create(CurrentUser u, CreateSupplierRequest req)
    {
        var supplier = new Supplier(numbers.Next("SUP"), ec.TenantId, ec.EntityId, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
        suppliers.Add(supplier);
        audit.Log(u, "create", "supplier", supplier.Id, $"Supplier '{req.Name}' created");
        suppliers.Save();
        return (supplier, null, null);
    }
}
