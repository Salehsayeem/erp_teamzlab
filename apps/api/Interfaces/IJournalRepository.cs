namespace Erp.Api.Interfaces;

public interface IJournalRepository
{
    Journal? Find(string journalId);
    List<Journal> All();
    bool HasReversal(string journalId);
    void Add(Journal journal);
    void MarkPosted(Journal journal, IReadOnlyList<JournalLine>? additionalLines = null);
    int Save();
}
