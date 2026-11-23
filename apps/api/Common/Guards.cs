namespace Erp.Api;

public static class Guards
{
    public static string? Check(string? value, string field, int max = 200) =>
        string.IsNullOrWhiteSpace(value) ? $"{field} is required." :
        value.Length > max ? $"{field} must be at most {max} characters." : null;

    public static string? CheckEmail(string? value) =>
        Check(value, "Email") is not null and var e ? e :
        value!.Contains('@', StringComparison.Ordinal) ? null : "Email must be valid.";

    public static string? CheckLines(IReadOnlyList<PoLine>? lines)
    {
        if (lines is null || lines.Count == 0) return "At least one order line is required.";
        return lines.Any(l => l.Qty <= 0 || l.UnitPrice < 0) ? "Line qty must be > 0 and unit price must be >= 0." : null;
    }

    public static string? CheckLines(IReadOnlyList<SoLine>? lines)
    {
        if (lines is null || lines.Count == 0) return "At least one order line is required.";
        return lines.Any(l => l.Qty <= 0 || l.UnitPrice < 0) ? "Line qty must be > 0 and unit price must be >= 0." : null;
    }

    public static string? CheckCountItems(IReadOnlyList<Dtos.CountItemRequest>? items)
    {
        if (items is null || items.Count == 0) return "At least one count line is required.";
        if (items.Any(i => string.IsNullOrWhiteSpace(i.ItemSku))) return "Each count line needs an item SKU.";
        if (items.Any(i => i.CountedQty < 0)) return "Counted qty must be >= 0.";
        return items.GroupBy(i => i.ItemSku).Any(g => g.Count() > 1) ? "Duplicate item lines are not allowed in one count." : null;
    }
}
