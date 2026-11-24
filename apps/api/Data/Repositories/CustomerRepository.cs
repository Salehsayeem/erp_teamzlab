using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class CustomerRepository(ErpDbContext db, EntityContext ec) : ICustomerRepository
{
    private IQueryable<Customer> Scoped() =>
        db.Customers.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public List<Customer> All() => Scoped().OrderBy(c => c.Id).ToList();

    public Customer? Find(string customerId) => Scoped().FirstOrDefault(c => c.Id == customerId);

    public bool Exists(string customerId) => Scoped().Any(c => c.Id == customerId);

    // 005 (trimmed, R-T7): duplicate detection matches the normalized tax id — trim + invariant
    // case-fold, then exact equality. Never auto-merge; the caller surfaces the candidate.
    public Customer? FindByNormalizedTaxId(string taxId)
    {
        var normalized = taxId.Trim();
        return Scoped().ToList().FirstOrDefault(c => c.TaxId.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(Customer customer) => db.Customers.Add(customer);

    public int Save() => db.SaveChanges();
}
