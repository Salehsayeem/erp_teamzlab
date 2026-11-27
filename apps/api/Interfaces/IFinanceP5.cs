using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// P5 currency/FX master (006/007/008) — CurrencyService implements both faces.
public interface ICurrencyService
{
    object ListCurrencies();
    (Currency? currency, string? code, string? message) CreateCurrency(CurrentUser user, CreateCurrencyRequest request);
    object ListRates();
    (FxRate? rate, string? code, string? message) CreateRate(CurrentUser user, CreateFxRateRequest request);
    (FxRate? rate, string? code, string? message) ApproveRate(CurrentUser approver, string rateId);
    (FxRate? rate, string? code, string? message) WithdrawRate(CurrentUser user, string rateId, string reason);
    (FinanceSetting? setting, string? code, string? message) UpsertSetting(CurrentUser user, UpsertFinanceSettingRequest request);
}

// 008 AC1/AC2: resolution returns EXACTLY ONE approved rate per pair/date/type or a blocking
// error — FX_RATE_MISSING (no approved rate) / FX_RATE_AMBIGUOUS (>1). No silent fallback.
public interface IFxRateResolver
{
    (FxRate? rate, string? code, string? message) Resolve(string baseCurrency, string quoteCurrency, DateOnly date, FxRateType type);
}

// 003 dimension master — sets + permitted values with effective intervals.
public interface IDimensionService
{
    object ListSets();
    (DimensionSet? set, string? code, string? message) CreateSet(CurrentUser user, CreateDimensionSetRequest request);
    object ListValues(string? setCode);
    (DimensionValue? value, string? code, string? message) CreateValue(CurrentUser user, CreateDimensionValueRequest request);
}
