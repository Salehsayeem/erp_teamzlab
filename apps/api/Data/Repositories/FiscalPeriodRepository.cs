using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Fiscal periods — tenant+entity scoped lookups only (CLAUDE.md security rule)
public sealed class FiscalPeriodRepository(ErpDbContext db, EntityContext ec) : IFiscalPeriodRepository
{
    private IQueryable<FiscalPeriod> Scoped() =>
        db.FiscalPeriods.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);

    public FiscalPeriod? Find(string periodId) =>
        Scoped().FirstOrDefault(p => p.Id == periodId);

    public List<FiscalPeriod> All() => Scoped().OrderBy(p => p.StartDate).ToList();

    // periods whose range contains the date — the posting guard uses this, never FirstOrDefault
    // on a shared query without scope
    public List<FiscalPeriod> OpenCovering(DateOnly date) =>
        Scoped().Where(p => p.StartDate <= date && p.EndDate >= date)
            .OrderByDescending(p => p.StartDate).ToList();

    public bool NameExists(string name) => Scoped().Any(p => p.Name == name);

    public bool Overlaps(DateOnly startDate, DateOnly endDate) =>
        Scoped().Any(p => p.StartDate <= endDate && p.EndDate >= startDate);

    public void Add(FiscalPeriod period) => db.FiscalPeriods.Add(period);

    // tracked-entity mutations — same pattern as SalesOrderRepository's status flips
    public void MarkClosed(FiscalPeriod period, string closedBy)
    {
        db.Entry(period).Property(nameof(FiscalPeriod.Status)).CurrentValue = PeriodStatus.Closed;
        db.Entry(period).Property(nameof(FiscalPeriod.ClosedBy)).CurrentValue = closedBy;
        db.Entry(period).Property(nameof(FiscalPeriod.ClosedAt)).CurrentValue = DateTimeOffset.UtcNow;
    }

    public void MarkReopened(FiscalPeriod period, string reason)
    {
        db.Entry(period).Property(nameof(FiscalPeriod.Status)).CurrentValue = PeriodStatus.Open;
        db.Entry(period).Property(nameof(FiscalPeriod.ReopenReason)).CurrentValue = reason;
    }

    // close gate (063 trimmed): unapproved (Pending) journals whose effective date falls in the range
    public List<Journal> PendingInRange(DateOnly start, DateOnly end) =>
        ScopedJournals().Where(j => j.Status == JournalStatus.Pending).ToList()
            .Where(j => j.EffectiveDate >= start && j.EffectiveDate <= end)
            .OrderBy(j => j.Id).ToList(); // effective date coalesce runs in memory (DateOnly vs DateTimeOffset)

    private IQueryable<Journal> ScopedJournals() =>
        db.Journals.Where(j => j.TenantId == ec.TenantId && j.LegalEntityId == ec.EntityId);

    public int Save() => db.SaveChanges();
}
