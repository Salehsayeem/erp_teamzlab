namespace Erp.Api.Interfaces;

public interface IJournalRepository
{
    Journal? Find(string journalId);
    List<Journal> All();
    void Add(Journal journal);
    int Save();
}
