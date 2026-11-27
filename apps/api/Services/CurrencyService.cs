using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Api.Services;

// P5 currency/FX master (006/007/008): settlement-scale currency registry, provenance-carrying
// FX rates with maker-checked approval, and the exactly-one-or-block resolver. The functional
// currency lives as a FinanceSetting (007) — changes are effective-dated, history stays in audit.
public sealed class CurrencyService(ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : ICurrencyService, IFxRateResolver
{
    public const string FunctionalCurrencyKey = "finance.functional_currency";

    private IQueryable<Currency> Currencies => db.Currencies
        .Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    private IQueryable<FxRate> Rates => db.FxRates
        .Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    public object ListCurrencies() => Currencies.OrderBy(c => c.Code).ToList();

    public (Currency? currency, string? code, string? message) CreateCurrency(CurrentUser user, CreateCurrencyRequest request)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
            return (null, "VALIDATION_ERROR", "Currency Code must be exactly three ASCII letters (ISO 4217 style).");
        if (request.DecimalScale is not (0 or 2 or 3))
            return (null, "VALIDATION_ERROR", "DecimalScale must be 0, 2 or 3 — settlement scales only.");
        if (Currencies.Any(c => c.Code == code))
            return (null, "CURRENCY_EXISTS", $"Currency {code} already exists for this legal entity.");
        var currency = new Currency($"CUR-{code}", ec.TenantId, ec.EntityId, code,
            request.Name.Trim(), request.DecimalScale, request.Active ?? true);
        db.Currencies.Add(currency);
        audit.Log(user, "create", "currency", currency.Id, $"Currency {code} added ({request.DecimalScale}dp, {(currency.Active ? "active" : "inactive")}).");
        db.SaveChanges();
        return (currency, null, null);
    }

    public object ListRates() => Rates.OrderBy(r => r.Id).ToList();

    public (FxRate? rate, string? code, string? message) CreateRate(CurrentUser user, CreateFxRateRequest request)
    {
        var baseError = ValidateRatePair(request.BaseCurrency, request.QuoteCurrency, request.Rate);
        if (baseError is not null) return (null, baseError.Value.code, baseError.Value.message);
        var source = request.Source?.Trim();
        if (Guards.Check(source, "Source", 200) is { } sourceError)
            return (null, "VALIDATION_ERROR", sourceError); // 008: provenance is mandatory at intake
        var rate = new FxRate(numbers.Next("FXR"), ec.TenantId, ec.EntityId,
            request.BaseCurrency.Trim().ToUpperInvariant(), request.QuoteCurrency.Trim().ToUpperInvariant(),
            request.RateDate, request.RateType, request.Rate, request.Precision ?? 6,
            source!, DateTimeOffset.UtcNow, FxRateStatus.Pending, user.Id);
        db.FxRates.Add(rate);
        audit.Log(user, "create", "fx_rate", rate.Id,
            $"FX rate {rate.BaseCurrency}/{rate.QuoteCurrency} {rate.Rate} {rate.RateType} for {rate.RateDate:yyyy-MM-dd} drafted from '{rate.Source}' (precision {rate.Precision}).");
        db.SaveChanges();
        return (rate, null, null);
    }

    public (FxRate? rate, string? code, string? message) ApproveRate(CurrentUser approver, string rateId)
    {
        var rate = Rates.FirstOrDefault(r => r.Id == rateId);
        if (rate is null) return (null, "FX_RATE_NOT_FOUND", "FX rate not found.");
        if (rate.Status != FxRateStatus.Pending) return (null, "FX_RATE_APPROVAL_ERROR", "Only pending FX rates can be approved.");
        if (rate.CreatedBy == approver.Id)
            return (null, "FX_RATE_APPROVAL_ERROR", "Maker-checker: the preparer cannot approve their own FX rate.");
        // 008 fix round: approving a second rate over an already-approved key would create the
        // exact ambiguity the resolver blocks on — refuse BEFORE any state change. The recovery
        // path is withdraw-with-reason on one of the rates, not a silent pick at resolve time.
        var blocking = Rates.FirstOrDefault(r => r.Id != rateId && r.BaseCurrency == rate.BaseCurrency
            && r.QuoteCurrency == rate.QuoteCurrency && r.RateDate == rate.RateDate
            && r.RateType == rate.RateType && r.Status == FxRateStatus.Approved);
        if (blocking is not null)
            return (null, "FX_RATE_AMBIGUOUS",
                $"{blocking.Id} is already approved for {rate.BaseCurrency}/{rate.QuoteCurrency} on {rate.RateDate:yyyy-MM-dd} ({rate.RateType}); withdraw it before approving a competing rate.");
        var approved = rate with { Status = FxRateStatus.Approved, ApprovedBy = approver.Id, ApprovedAt = DateTimeOffset.UtcNow };
        db.FxRates.Entry(rate).CurrentValues.SetValues(approved);
        audit.Log(approver, "approve", "fx_rate", rate.Id,
            $"FX rate {rate.Id} {rate.BaseCurrency}/{rate.QuoteCurrency} {rate.Rate} approved by {approver.Email} (source '{rate.Source}', retrieved {rate.RetrievedAt:u}).");
        audit.Emit(approver, "finance.fxrate.approved",
            new { rateId = rate.Id, pair = $"{rate.BaseCurrency}/{rate.QuoteCurrency}", rateDate = rate.RateDate, rateType = rate.RateType.ToString(), rate.Rate, rate.Precision, approver = approver.Id });
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException ex) when (ex.InnerException is NpgsqlException { SqlState: "23505" })
        {
            // re-review F1: the pre-check above is check-then-act — a competing approval can commit
            // between the read and this SaveChanges; the partial unique index settles the race into
            // the same 409 the pre-check returns (barrier-tested like the void path).
            return (null, "FX_RATE_AMBIGUOUS",
                $"A competing rate for {rate.BaseCurrency}/{rate.QuoteCurrency} on {rate.RateDate:yyyy-MM-dd} ({rate.RateType}) was approved concurrently; withdraw one to resolve.");
        }
        return (approved, null, null);
    }

    // 008 fix round: the recovery path out of ambiguity (or a mis-keyed draft) — a Pending or
    // Approved rate leaves the exactly-one window; the reason is mandatory and audited.
    public (FxRate? rate, string? code, string? message) WithdrawRate(CurrentUser user, string rateId, string reason)
    {
        var rate = Rates.FirstOrDefault(r => r.Id == rateId);
        if (rate is null) return (null, "FX_RATE_NOT_FOUND", "FX rate not found.");
        if (rate.Status == FxRateStatus.Withdrawn)
            return (null, "FX_RATE_WITHDRAWAL_ERROR", $"FX rate {rateId} is already withdrawn.");
        var withdrawn = rate with { Status = FxRateStatus.Withdrawn };
        db.FxRates.Entry(rate).CurrentValues.SetValues(withdrawn);
        audit.Log(user, "withdraw", "fx_rate", rate.Id,
            $"FX rate {rate.Id} {rate.BaseCurrency}/{rate.QuoteCurrency} {rate.Rate} {rate.RateType} for {rate.RateDate:yyyy-MM-dd} withdrawn by {user.Email}: {reason}");
        db.SaveChanges();
        return (withdrawn, null, null);
    }

    // 008 AC1/AC2 — exactly one approved rate per pair/date/type, else a blocking error
    public (FxRate? rate, string? code, string? message) Resolve(string baseCurrency, string quoteCurrency, DateOnly date, FxRateType type)
    {
        var matches = Rates.Where(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == quoteCurrency
                && r.RateDate == date && r.RateType == type && r.Status == FxRateStatus.Approved)
            .OrderBy(r => r.Id).ToList();
        if (matches.Count == 0)
            return (null, "FX_RATE_MISSING",
                $"No approved FX rate exists for {baseCurrency}/{quoteCurrency} on {date:yyyy-MM-dd} ({type}); posting is blocked — no fallback is applied.");
        if (matches.Count > 1)
            return (null, "FX_RATE_AMBIGUOUS",
                $"{matches.Count} approved FX rates exist for {baseCurrency}/{quoteCurrency} on {date:yyyy-MM-dd} ({type}); resolution is blocked until one is withdrawn.");
        return (matches[0], null, null);
    }

    // 007: effective-dated functional currency — one row per key, prior values live in audit
    public (FinanceSetting? setting, string? code, string? message) UpsertSetting(CurrentUser user, UpsertFinanceSettingRequest request)
    {
        var key = request.Key.Trim();
        if (Guards.Check(key, "Key", 100) is { } keyError) return (null, "VALIDATION_ERROR", keyError);
        var value = request.Value.Trim();
        if (key == FunctionalCurrencyKey)
        {
            var currency = Currencies.FirstOrDefault(c => c.Code == value.ToUpperInvariant());
            if (currency is null)
                return (null, "CURRENCY_NOT_FOUND", $"Currency {value} is not in the currency master.");
            if (!currency.Active)
                return (null, "VALIDATION_ERROR", $"Currency {currency.Code} is inactive and cannot become the functional currency.");
            value = currency.Code;
        }
        var existing = db.FinanceSettings.FirstOrDefault(s =>
            s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Key == key);
        var previous = existing?.Value;
        FinanceSetting row;
        if (existing is null)
        {
            row = new FinanceSetting($"FS-{key.Replace('.', '-').Replace('_', '-')}",
                ec.TenantId, ec.EntityId, key, value, request.EffectiveFrom, user.Id, DateTimeOffset.UtcNow);
            db.FinanceSettings.Add(row);
        }
        else
        {
            row = existing with { Value = value, EffectiveFrom = request.EffectiveFrom ?? existing.EffectiveFrom, UpdatedBy = user.Id, UpdatedAt = DateTimeOffset.UtcNow };
            db.FinanceSettings.Entry(existing).CurrentValues.SetValues(row);
        }
        if (key == FunctionalCurrencyKey)
        {
            var effective = row.EffectiveFrom;
            audit.Log(user, "change", "finance_setting", key,
                $"Finance setting {key} changed {previous ?? "(none)"} → {value}, effective {effective?.ToString("yyyy-MM-dd") ?? "immediately"}; posted journals keep their stored functional values.");
            audit.Emit(user, "finance.functional_currency.changed",
                new { key, previous, value, effectiveFrom = effective, changedBy = user.Id });
        }
        else
        {
            audit.Log(user, "change", "finance_setting", key,
                $"Finance setting {key} changed {previous ?? "(none)"} → {value}.");
        }
        db.SaveChanges();
        return (row, null, null);
    }

    private (string code, string message)? ValidateRatePair(string baseRaw, string quoteRaw, decimal rate)
    {
        var b = baseRaw.Trim().ToUpperInvariant();
        var q = quoteRaw.Trim().ToUpperInvariant();
        if (b == q) return ("VALIDATION_ERROR", "BaseCurrency and QuoteCurrency must differ.");
        foreach (var code in new[] { b, q })
        {
            var currency = Currencies.FirstOrDefault(c => c.Code == code);
            if (currency is null) return ("CURRENCY_NOT_FOUND", $"Currency {code} is not in the currency master.");
            if (!currency.Active) return ("VALIDATION_ERROR", $"Currency {code} is inactive.");
        }
        if (rate <= 0) return ("VALIDATION_ERROR", "Rate must be greater than 0.");
        return null;
    }
}

// 003 dimension master — sets + permitted values; journal-create validation lives in JournalService.
public sealed class DimensionService(ErpDbContext db, EntityContext ec, IAuditRepository audit) : IDimensionService
{
    private IQueryable<DimensionSet> Sets => db.DimensionSets
        .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    private IQueryable<DimensionValue> Values => db.DimensionValues
        .Where(v => v.TenantId == ec.TenantId && v.LegalEntityId == ec.EntityId);

    public object ListSets() => Sets.OrderBy(s => s.Code).ToList();

    public (DimensionSet? set, string? code, string? message) CreateSet(CurrentUser user, CreateDimensionSetRequest request)
    {
        var code = request.Code.Trim();
        if (Guards.Check(code, "Code", 50) is { } err) return (null, "VALIDATION_ERROR", err);
        if (Guards.Check(request.Name, "Name") is { } nameErr) return (null, "VALIDATION_ERROR", nameErr);
        if (Sets.Any(s => s.Code == code))
            return (null, "DIMENSION_SET_EXISTS", $"Dimension set {code} already exists for this legal entity.");
        var set = new DimensionSet($"DS-{code}", ec.TenantId, ec.EntityId, code, request.Name.Trim(), request.Required ?? false, true);
        db.DimensionSets.Add(set);
        audit.Log(user, "create", "dimension_set", set.Id, $"Dimension set {code} created ({(set.Required ? "required" : "optional")}).");
        db.SaveChanges();
        return (set, null, null);
    }

    public object ListValues(string? setCode) => string.IsNullOrWhiteSpace(setCode)
        ? Values.OrderBy(v => v.SetCode).ThenBy(v => v.Value).ToList()
        : Values.Where(v => v.SetCode == setCode.Trim()).OrderBy(v => v.Value).ToList();

    public (DimensionValue? value, string? code, string? message) CreateValue(CurrentUser user, CreateDimensionValueRequest request)
    {
        var setCode = request.SetCode.Trim();
        var value = request.Value.Trim();
        if (Guards.Check(value, "Value", 100) is { } err) return (null, "VALIDATION_ERROR", err);
        var set = Sets.FirstOrDefault(s => s.Code == setCode);
        if (set is null) return (null, "DIMENSION_SET_NOT_FOUND", $"Dimension set {setCode} does not exist.");
        if (request.EffectiveTo is { } to && to < request.EffectiveFrom)
            return (null, "VALIDATION_ERROR", "EffectiveTo must be on or after EffectiveFrom.");
        if (Values.Any(v => v.SetCode == setCode && v.Value == value))
            return (null, "DIMENSION_VALUE_EXISTS", $"Value {value} already exists in dimension set {setCode}.");
        var row = new DimensionValue($"DV-{setCode}-{value}", ec.TenantId, ec.EntityId, setCode, value,
            request.EffectiveFrom, request.EffectiveTo, request.Active ?? true);
        db.DimensionValues.Add(row);
        audit.Log(user, "create", "dimension_value", row.Id,
            $"Value {value} added to dimension set {setCode} ({row.EffectiveFrom:yyyy-MM-dd}..{row.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"}, {(row.Active ? "active" : "inactive")}).");
        db.SaveChanges();
        return (row, null, null);
    }
}
