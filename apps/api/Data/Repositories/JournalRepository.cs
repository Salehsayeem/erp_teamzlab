using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class JournalRepository(ErpDbContext db, EntityContext ec) : IJournalRepository
{
    private IQueryable<Journal> Scoped() =>
        db.Journals.Where(j => j.TenantId == ec.TenantId && j.LegalEntityId == ec.EntityId);

    private List<Journal> Ordered(IQueryable<Journal> query) =>
        query.Include(j => j.Lines).OrderBy(j => j.Id).ToList(); // journal_lines is an owned table

    public Journal? Find(string journalId) =>
        Ordered(Scoped().Where(j => j.Id == journalId)).FirstOrDefault();

    public List<Journal> All() => Ordered(Scoped());

    public void Add(Journal journal)
    {
        db.Journals.Add(journal);
        foreach (var line in journal.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public int Save() => db.SaveChanges();
}
