using Erp.Api.Interfaces;

namespace Erp.Api.Data.Repositories;

public sealed class AccountRepository(ErpDbContext db, EntityContext ec) : IAccountRepository
{
    private IQueryable<Account> Scoped() =>
        db.Accounts.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);

    public Account? Find(string accountId) => Scoped().FirstOrDefault(a => a.Id == accountId);

    public Account? FindByCode(string code) =>
        Scoped().OrderByDescending(a => a.Version).FirstOrDefault(a => a.Code == code);

    public List<Account> All() => Scoped().OrderBy(a => a.Code).ToList();

    public bool CodeExists(string code) => Scoped().Any(a => a.Code == code);

    public void Add(Account account) => db.Accounts.Add(account);

    public int Save() => db.SaveChanges();
}
