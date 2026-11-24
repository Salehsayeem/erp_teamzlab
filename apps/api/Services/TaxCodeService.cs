using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Tax code registry (FINANCE-032): jurisdiction-scoped codes with effective-dated revisions.
// Each publication is one row (Version++), ranges for a code must never overlap (032 AC2).
public sealed class TaxCodeService(ITaxCodeRepository taxCodes, IAuditRepository audit, EntityContext ec) : ITaxCodeService
{
    public object List() => taxCodes.All();

    public (TaxCode? taxCode, string? code, string? message) Create(CurrentUser user, Dtos.CreateTaxCodeRequest request, string jurisdiction)
    {
        if (taxCodes.Overlaps(request.Code, request.EffectiveFrom, request.EffectiveTo))
            return (null, "TAX_CODE_OVERLAP",
                $"Tax code {request.Code} already has an effective range overlapping {request.EffectiveFrom:yyyy-MM-dd}..{request.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open end"}.");
        var version = taxCodes.NextVersion(request.Code);
        var taxCode = new TaxCode($"TC-{request.Code}-V{version}", ec.TenantId, ec.EntityId, request.Code,
            jurisdiction, request.RatePct, request.Basis, request.Recoverable,
            request.EffectiveFrom, request.EffectiveTo, version, user.Id, DateTimeOffset.UtcNow);
        taxCodes.Add(taxCode);
        audit.Log(user, "publish", "tax_code", taxCode.Id,
            $"Tax code {taxCode.Code} v{taxCode.Version} published ({taxCode.Jurisdiction}, {taxCode.RatePct:0.##}%, {taxCode.Basis}, " +
            $"{(taxCode.Recoverable ? "recoverable" : "non-recoverable")}, effective {taxCode.EffectiveFrom:yyyy-MM-dd}..{taxCode.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"})");
        audit.Emit(user, "finance.taxcode.published",
            new { taxCodeId = taxCode.Id, code = taxCode.Code, version = taxCode.Version, ratePct = taxCode.RatePct, jurisdiction = taxCode.Jurisdiction });
        taxCodes.Save();
        return (taxCode, null, null);
    }
}
