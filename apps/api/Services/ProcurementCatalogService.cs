using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P2 (002–004): category tree + UoM registry with explicit conversions. Draft requisition lines stay
// tolerant (011 AC1); Resolve() is the strict unit gate consumed at requisition submission.
public sealed class ProcurementCatalogService(
    IProcurementCatalogRepository catalog, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IProcurementCatalogService
{
    public object Categories() => catalog.AllCategories().Select(c => new
    {
        c.Id, c.ParentId, c.Name, c.Status, c.EffectiveFrom, c.EffectiveTo, c.ExternalCode, c.CreatedBy, c.CreatedAt,
    }).ToList();

    public ProcurementCategory? FindCategory(string id) => catalog.FindCategory(id);

    public (ProcurementCategory? category, string? code, string? message) CreateCategory(
        CurrentUser u, string? name, string? parentId, string? externalCode, DateOnly? effectiveFrom)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, "VALIDATION_ERROR", "Name is required.");
        if (parentId is { } pid)
        {
            var parent = catalog.FindCategory(pid);
            if (parent is null) return (null, "CATEGORY_NOT_FOUND", "Parent category does not exist.");
            if (parent.Status is CategoryStatus.Deprecated)
                return (null, "CATEGORY_DEPRECATED", $"Parent category {pid} is deprecated; new children are not allowed.");
        }
        var category = new ProcurementCategory(numbers.Next("CAT"), ec.TenantId, ec.EntityId, parentId,
            name.Trim(), CategoryStatus.Active, effectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow), null,
            externalCode, u.Id, DateTimeOffset.UtcNow);
        catalog.AddCategory(category);
        audit.Log(u, "create", "procurement_category", category.Id, $"Category '{category.Name}' created");
        catalog.Save();
        return (category, null, null);
    }

    public (ProcurementCategory? category, string? code, string? message) UpdateCategory(
        CurrentUser u, string id, string? name, string? parentId, string? externalCode, DateOnly? effectiveTo)
    {
        var category = catalog.FindCategory(id);
        if (category is null) return (null, "CATEGORY_NOT_FOUND", "Category not found.");
        if (category.Status is CategoryStatus.Deprecated)
            return (null, "CATEGORY_STATE_ERROR", "Deprecated categories are read-only (003: readable, not selectable).");
        if (parentId is { } && parentId != category.ParentId)
        {
            if (parentId == id || WalkAncestors(parentId).Contains(id))
                return (null, "CATEGORY_CYCLE", $"Re-parenting {id} under {parentId} would create a cycle.");
            if (catalog.FindCategory(parentId) is null)
                return (null, "CATEGORY_NOT_FOUND", "Parent category does not exist.");
            catalog.SetField(category, nameof(ProcurementCategory.ParentId), parentId);
        }
        if (!string.IsNullOrWhiteSpace(name)) catalog.SetField(category, nameof(ProcurementCategory.Name), name.Trim());
        if (externalCode is { } code) catalog.SetField(category, nameof(ProcurementCategory.ExternalCode), code);
        if (effectiveTo is { } et) catalog.SetField(category, nameof(ProcurementCategory.EffectiveTo), et);
        audit.Log(u, "update", "procurement_category", id, "Category updated (name/parent/effective changes)");
        catalog.Save();
        return (category, null, null);
    }

    public (ProcurementCategory? category, string? code, string? message) DeprecateCategory(CurrentUser u, string id)
    {
        var category = catalog.FindCategory(id);
        if (category is null) return (null, "CATEGORY_NOT_FOUND", "Category not found.");
        if (category.Status is CategoryStatus.Deprecated)
            return (null, "CATEGORY_STATE_ERROR", "Category is already deprecated.");
        catalog.SetField(category, nameof(ProcurementCategory.Status), CategoryStatus.Deprecated);
        audit.Log(u, "deprecate", "procurement_category", id, $"Category '{category.Name}' deprecated (readable, not selectable)");
        catalog.Save();
        return (category, null, null);
    }

    public object Units() => catalog.AllUoms().Select(u => new
    {
        u.Id, u.Code, u.Name, u.Dimension, u.Precision, u.IsBase, u.CreatedBy, u.CreatedAt,
    }).ToList();

    public (UnitOfMeasure? uom, string? code, string? message) CreateUom(
        CurrentUser u, string? code, string? name, UomDimension dimension, int precision, bool isBase)
    {
        if (string.IsNullOrWhiteSpace(code)) return (null, "VALIDATION_ERROR", "Code is required.");
        if (precision < 0 || precision > 6) return (null, "VALIDATION_ERROR", "Precision must be between 0 and 6 decimal places.");
        if (catalog.FindUomByCode(code.Trim()) is not null)
            return (null, "UOM_STATE_ERROR", $"Unit '{code}' is already registered.");
        var uom = new UnitOfMeasure(numbers.Next("UOM"), ec.TenantId, ec.EntityId, code.Trim(), name ?? code.Trim(),
            dimension, precision, isBase, u.Id, DateTimeOffset.UtcNow);
        catalog.AddUom(uom);
        audit.Log(u, "create", "unit_of_measure", uom.Code, $"UoM {uom.Code} ({dimension}) registered");
        catalog.Save();
        return (uom, null, null);
    }

    public (UomConversion? conversion, string? code, string? message) CreateConversion(
        CurrentUser u, string fromCode, string? toCode, decimal factor, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        var from = catalog.FindUomByCode(fromCode);
        if (from is null) return (null, "UOM_NOT_FOUND", $"Unit '{fromCode}' is not registered.");
        var to = catalog.FindUomByCode(toCode ?? "");
        if (to is null) return (null, "UOM_NOT_FOUND", $"Unit '{toCode}' is not registered.");
        if (from.Id == to.Id)
            return (null, "UOM_CONVERSION_INVALID", "A unit converts to itself by definition; no conversion row is needed.");
        if (from.Dimension != to.Dimension)
            return (null, "UOM_CONVERSION_INVALID", $"Conversion {from.Code} → {to.Code} crosses dimensions ({from.Dimension} vs {to.Dimension}).");
        if (factor <= 0) return (null, "VALIDATION_ERROR", "Factor must be greater than 0.");
        if (effectiveTo is { } t && t < effectiveFrom)
            return (null, "VALIDATION_ERROR", "EffectiveTo must not be before EffectiveFrom.");
        var conversion = new UomConversion($"{from.Code}:{to.Code}:{effectiveFrom:yyyyMMdd}", ec.TenantId, ec.EntityId,
            from.Code, to.Code, factor, effectiveFrom, effectiveTo, u.Id, DateTimeOffset.UtcNow);
        if (catalog.ConversionsFrom(from.Code).Any(c => c.Id == conversion.Id))
            return (null, "UOM_CONVERSION_INVALID", $"A conversion {from.Code} → {to.Code} effective {effectiveFrom:yyyyMMdd} already exists.");
        catalog.AddConversion(conversion);
        audit.Log(u, "create", "uom_conversion", conversion.Id, $"Conversion {from.Code} → {to.Code} ×{factor} active from {effectiveFrom:yyyy-MM-dd}");
        catalog.Save();
        return (conversion, null, null);
    }

    // 004 AC1–3: entered unit kept, base unit normalized, over-precision rejected (never rounded),
    // conversion applied only when its active interval covers the txn date.
    public (decimal BaseQty, string BaseUnit, string? Code, string? Message) Resolve(string? unitCode, decimal qty, DateOnly asOf)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
            return (0m, "", "VALIDATION_ERROR", "Unit is required.");
        var uom = catalog.FindUomByCode(unitCode);
        if (uom is null)
            return (0m, "", "VALIDATION_ERROR", $"Unit '{unitCode}' is not a registered purchasing unit.");
        if (decimal.Round(qty, uom.Precision, MidpointRounding.AwayFromZero) != qty)
            return (0m, "", "QTY_PRECISION", $"Quantity {qty} exceeds the {uom.Precision}-decimal precision allowed for '{uom.Code}' (004: rejected, never rounded).");
        if (uom.IsBase) return (qty, uom.Code, null, null);

        var baseUnit = catalog.AllUoms().FirstOrDefault(b => b.IsBase && b.Dimension == uom.Dimension);
        if (baseUnit is null)
            return (0m, "", "VALIDATION_ERROR", $"No base unit is registered for dimension {uom.Dimension}.");
        var conversion = catalog.ConversionsFrom(uom.Code).FirstOrDefault(c =>
            c.ToCode == baseUnit.Code && c.EffectiveFrom <= asOf && (c.EffectiveTo is null || c.EffectiveTo >= asOf));
        if (conversion is null)
            return (0m, "", "VALIDATION_ERROR", $"No conversion from '{uom.Code}' to '{baseUnit.Code}' is active for {asOf:yyyy-MM-dd} (004 AC2).");
        return (decimal.Round(qty * conversion.Factor, uom.Precision, MidpointRounding.AwayFromZero), baseUnit.Code, null, null);
    }

    // cycles can only form through re-parenting, so the check walks the proposed parent's ancestors
    private HashSet<string> WalkAncestors(string startId)
    {
        var seen = new HashSet<string>();
        var current = catalog.FindCategory(startId);
        while (current is { } c && seen.Add(c.Id))
            current = c.ParentId is { } p ? catalog.FindCategory(p) : null;
        return seen;
    }
}
