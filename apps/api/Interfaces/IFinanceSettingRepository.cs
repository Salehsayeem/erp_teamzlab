namespace Erp.Api.Interfaces;

public interface IFinanceSettingRepository
{
    FinanceSetting? Get(string key);
    void Add(FinanceSetting setting);
    int Save();
}
