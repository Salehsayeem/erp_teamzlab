namespace Erp.Api.Interfaces;

public interface IAccountRepository
{
    Account? Find(string accountId);
    Account? FindByCode(string code);
    List<Account> All();
    bool CodeExists(string code);
    void Add(Account account);
    int Save();
}
