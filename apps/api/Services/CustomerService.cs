using Erp.Api.Interfaces;
using Erp.Api.Data;
using Erp.Api.Dtos;

namespace Erp.Api.Services;

public sealed class CustomerService(Db db) : ICustomerService
{
    public object List() =>
        db.WithLock(() => db.Customers.Select(c => new { c.Id, c.Name, c.TaxId, c.Email, c.CreatedAt, c.CreatedBy }).ToList());

    public (object data, string? code, string? message) Create(CurrentUser u, CreateCustomerRequest req) =>
        db.WithLock(() =>
        {
            var customer = new Customer(db.NextDoc("CUS"), db.Tenant.Id, db.Entity.Id, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
            db.Customers.Add(customer);
            Audit.Log(db, u, "create", "customer", customer.Id, $"Customer '{req.Name}' created");
            return ((object)customer, (string?)null, (string?)null);
        });
}
