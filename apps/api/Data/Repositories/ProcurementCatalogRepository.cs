using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// P2 taxonomy + purchasing-unit reads/writes — tenant+entity scoped on every query (R14).
public sealed class ProcurementCatalogRepository(ErpDbContext db, EntityContext ec) : IProcurementCatalogRepository
{
    private IQueryable<ProcurementCategory> CategoriesScoped() =>
        db.ProcurementCategories.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    private IQueryable<UnitOfMeasure> UomsScoped() =>
        db.UnitOfMeasures.Where(u => u.TenantId == ec.TenantId && u.LegalEntityId == ec.EntityId);

    private IQueryable<UomConversion> ConversionsScoped() =>
        db.UomConversions.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public ProcurementCategory? FindCategory(string id) => CategoriesScoped().FirstOrDefault(c => c.Id == id);
    public List<ProcurementCategory> AllCategories() => CategoriesScoped().OrderBy(c => c.Id).ToList();
    public void AddCategory(ProcurementCategory category) => db.ProcurementCategories.Add(category);

    public UnitOfMeasure? FindUomByCode(string code) => UomsScoped().FirstOrDefault(u => u.Code == code);
    public List<UnitOfMeasure> AllUoms() => UomsScoped().OrderBy(u => u.Code).ToList();
    public void AddUom(UnitOfMeasure uom) => db.UnitOfMeasures.Add(uom);

    public List<UomConversion> ConversionsFrom(string fromCode) =>
        ConversionsScoped().Where(c => c.FromCode == fromCode).OrderBy(c => c.EffectiveFrom).ToList();

    public void AddConversion(UomConversion conversion) => db.UomConversions.Add(conversion);

    // records carry init-only properties — mutations go through the EF change tracker (house pattern)
    public void SetField(object entity, string name, object value) =>
        db.Entry(entity).Property(name).CurrentValue = value;

    public int Save() => db.SaveChanges();
}
