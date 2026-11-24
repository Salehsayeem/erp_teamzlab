using Erp.Api.Interfaces;

namespace Erp.Api.Data.Repositories;

public sealed class FinanceSettingRepository(ErpDbContext db, EntityContext ec) : IFinanceSettingRepository
{
    public FinanceSetting? Get(string key) =>
        db.FinanceSettings.FirstOrDefault(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Key == key);

    public void Add(FinanceSetting setting) => db.FinanceSettings.Add(setting);

    public int Save() => db.SaveChanges();
}
