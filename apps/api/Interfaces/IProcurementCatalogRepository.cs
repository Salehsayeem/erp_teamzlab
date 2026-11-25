using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IProcurementCatalogRepository
{
    ProcurementCategory? FindCategory(string id);
    List<ProcurementCategory> AllCategories();
    void AddCategory(ProcurementCategory category);
    UnitOfMeasure? FindUomByCode(string code);
    List<UnitOfMeasure> AllUoms();
    void AddUom(UnitOfMeasure uom);
    List<UomConversion> ConversionsFrom(string fromCode);
    void AddConversion(UomConversion conversion);
    // records carry init-only properties — mutations go through the EF change tracker (house pattern)
    void SetField(object entity, string name, object value);
    int Save();
}
