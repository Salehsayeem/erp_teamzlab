using Erp.Api.Interfaces;

namespace Erp.Api.Data.Repositories;

// Tax codes — one row per published revision; revisions of a code share Code with disjoint ranges
public sealed class TaxCodeRepository(ErpDbContext db, EntityContext ec) : ITaxCodeRepository
{
    private IQueryable<TaxCode> Scoped() =>
        db.TaxCodes.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId);

    public TaxCode? Find(string taxCodeId) => Scoped().FirstOrDefault(t => t.Id == taxCodeId);

    public List<TaxCode> All() => Scoped().OrderBy(t => t.Code).ThenBy(t => t.Version).ToList();

    public List<TaxCode> ByCode(string code) => Scoped().Where(t => t.Code == code).OrderBy(t => t.Version).ToList();

    public TaxCode? EffectiveFor(string code, DateOnly date) =>
        Scoped().Where(t => t.Code == code && t.EffectiveFrom <= date && (t.EffectiveTo == null || t.EffectiveTo >= date))
            .OrderByDescending(t => t.Version).FirstOrDefault();

    public bool Overlaps(string code, DateOnly from, DateOnly? to) =>
        Scoped().Any(t => t.Code == code && t.EffectiveFrom <= (to ?? DateOnly.MaxValue) && (t.EffectiveTo == null || t.EffectiveTo >= from));

    public int NextVersion(string code)
    {
        var versions = Scoped().Where(t => t.Code == code).Select(t => t.Version).ToList();
        return versions.Count == 0 ? 1 : versions.Max() + 1;
    }

    public void Add(TaxCode taxCode) => db.TaxCodes.Add(taxCode);

    public int Save() => db.SaveChanges();
}
