using Erp.Api;

namespace Erp.Api.Interfaces;

// P2 taxonomy + purchasing units (002–004). Resolve() is the normalized-unit entry the requisition
// submit path consumes (entered unit + base unit + active-for-date conversion, 004 AC1–2).
public interface IProcurementCatalogService
{
    object Categories();
    ProcurementCategory? FindCategory(string id);
    (ProcurementCategory? category, string? code, string? message) CreateCategory(CurrentUser u, string? name, string? parentId, string? externalCode, DateOnly? effectiveFrom);
    (ProcurementCategory? category, string? code, string? message) UpdateCategory(CurrentUser u, string id, string? name, string? parentId, string? externalCode, DateOnly? effectiveTo);
    (ProcurementCategory? category, string? code, string? message) DeprecateCategory(CurrentUser u, string id);
    object Units();
    (UnitOfMeasure? uom, string? code, string? message) CreateUom(CurrentUser u, string? code, string? name, UomDimension dimension, int precision, bool isBase);
    (UomConversion? conversion, string? code, string? message) CreateConversion(CurrentUser u, string fromCode, string? toCode, decimal factor, DateOnly effectiveFrom, DateOnly? effectiveTo);
    (decimal BaseQty, string BaseUnit, string? Code, string? Message) Resolve(string? unitCode, decimal qty, DateOnly asOf);
}
