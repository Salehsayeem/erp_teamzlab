using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class CustomerRepository(ErpDbContext db, EntityContext ec) : ICustomerRepository
{
    private IQueryable<Customer> Scoped() =>
        db.Customers.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public List<Customer> All() => Scoped().OrderBy(c => c.Id).ToList();

    public bool Exists(string customerId) => Scoped().Any(c => c.Id == customerId);

    public void Add(Customer customer) => db.Customers.Add(customer);

    public int Save() => db.SaveChanges();
}
