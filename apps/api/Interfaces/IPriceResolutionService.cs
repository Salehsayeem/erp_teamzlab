namespace Erp.Api.Interfaces;

// 024 AC1 / 026 AC1: price resolution with explicit rule/precedence evidence — never a silent
// zero (024 edge). Contract prices beat book entries; books resolve by Precedence then minQty.
public sealed record PriceResolution(decimal UnitPrice, string? PriceBookId, string Rule, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal BaseValue, string SourceRef);

public interface IPriceResolutionService
{
    (PriceResolution? resolution, string? code, string? message) Resolve(string? customerId, string itemSku, decimal qty, DateOnly onDate);
}
