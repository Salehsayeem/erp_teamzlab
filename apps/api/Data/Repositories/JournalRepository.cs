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

    // approval: append tax control lines (shadow tenant columns set — they are new owned rows)
    // and flip Pending → Posted on the tracked entity
    public void MarkPosted(Journal journal, IReadOnlyList<JournalLine>? additionalLines = null)
    {
        if (additionalLines is not null)
            foreach (var line in additionalLines)
            {
                db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
                db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
                journal.Lines.Add(line);
            }
        db.Entry(journal).Property(nameof(Journal.Status)).CurrentValue = JournalStatus.Posted;
    }

    public int Save() => db.SaveChanges();
}
