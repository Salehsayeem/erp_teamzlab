using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P5 foundations (FINANCE-owned per brief §5): currency/FX master (006/007/008), dimensions
// (003), number-series admin (016). Reads gate fx:read, writes gate fx:write (R-FIN only —
// the R-FIN2 maker has no fx permissions by design), house envelope + Idem on POSTs.

[ApiController]
[Route("api/v1/currencies")]
public sealed class CurrenciesController(ICurrencyService currencies) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("fx:read")
            ? Envelope.Ok<object>(currencies.ListCurrencies())
            : Envelope.Fail("FORBIDDEN", "fx:read permission required.", 403);
    }

    [HttpPost]
    public IResult Create(CreateCurrencyRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 3) ?? Guards.Check(request.Name, "Name", 100);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:write")) return (Envelope.FailBody("FORBIDDEN", "fx:write permission required."), 403);
        var (currency, code, error) = currencies.CreateCurrency(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(currency!), 201);
    });
}

[ApiController]
[Route("api/v1/fx-rates")]
public sealed class FxRatesController(ICurrencyService currencies, IFxRateResolver resolver) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("fx:read")
            ? Envelope.Ok<object>(currencies.ListRates())
            : Envelope.Fail("FORBIDDEN", "fx:read permission required.", 403);
    }

    // 008 AC1: the read-side contract — exactly one approved rate per pair/date/type or a
    // blocking error (FX_RATE_MISSING / FX_RATE_AMBIGUOUS); P3 AP and P4 AR consume this
    [HttpGet("resolve")]
    public IResult Resolve([FromQuery] string @base, [FromQuery] string quote, [FromQuery] DateOnly date, [FromQuery] string? type)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:read")) return Envelope.Fail("FORBIDDEN", "fx:read permission required.", 403);
        if (!Enum.TryParse<FxRateType>(type, ignoreCase: true, out var rateType))
            return Envelope.Fail("VALIDATION_ERROR", $"type '{type}' is not a valid FX rate type (Spot, PeriodAverage, Closing).", 400);
        var (rate, code, error) = resolver.Resolve(@base.Trim().ToUpperInvariant(), quote.Trim().ToUpperInvariant(), date, rateType);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(rate!);
    }

    [HttpPost]
    public IResult Create(CreateFxRateRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.BaseCurrency, "BaseCurrency", 3)
            ?? Guards.Check(request.QuoteCurrency, "QuoteCurrency", 3);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:write")) return (Envelope.FailBody("FORBIDDEN", "fx:write permission required."), 403);
        var (rate, code, error) = currencies.CreateRate(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(rate!), 201);
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:write")) return Envelope.Fail("FORBIDDEN", "fx:write permission required.", 403);
        var (rate, code, error) = currencies.ApproveRate(u, id);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(rate!);
    }
}

[ApiController]
[Route("api/v1")]
public sealed class DimensionsController(IDimensionService dimensions) : ControllerBase
{
    [HttpGet("dimension-sets")]
    public IResult ListSets()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("fx:read")
            ? Envelope.Ok<object>(dimensions.ListSets())
            : Envelope.Fail("FORBIDDEN", "fx:read permission required.", 403);
    }

    [HttpPost("dimension-sets")]
    public IResult CreateSet(CreateDimensionSetRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:write")) return (Envelope.FailBody("FORBIDDEN", "fx:write permission required."), 403);
        var (set, code, error) = dimensions.CreateSet(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(set!), 201);
    });

    [HttpGet("dimension-values")]
    public IResult ListValues([FromQuery] string? setCode)
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("fx:read")
            ? Envelope.Ok<object>(dimensions.ListValues(setCode))
            : Envelope.Fail("FORBIDDEN", "fx:read permission required.", 403);
    }

    [HttpPost("dimension-values")]
    public IResult CreateValue(CreateDimensionValueRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:write")) return (Envelope.FailBody("FORBIDDEN", "fx:write permission required."), 403);
        var (value, code, error) = dimensions.CreateValue(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(value!), 201);
    });
}

[ApiController]
[Route("api/v1/number-series")]
public sealed class NumberSeriesController(INumberSeries numbers, IAuditRepository audit, EntityContext ec) : ControllerBase
{
    // 016 AC1: the admin view over the EXISTING sequence service (expose, never rebuild)
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("fx:read")
            ? Envelope.Ok<object>(numbers.List())
            : Envelope.Fail("FORBIDDEN", "fx:read permission required.", 403);
    }

    // 016 AC2: void-with-reason; the sequence never reissues, the record is the audit trail
    [HttpPost("{prefix}/voids")]
    public IResult Void(string prefix, VoidNumberRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Number, "Number", 50) ?? Guards.Check(request.Reason, "Reason", 500);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:write")) return (Envelope.FailBody("FORBIDDEN", "fx:write permission required."), 403);
        var normalized = prefix.Trim().ToUpperInvariant();
        var number = request.Number.Trim();
        if (!numbers.Exists(normalized))
            return (Envelope.FailBody("NUMBER_SERIES_NOT_FOUND", $"Number series {normalized} does not exist."), 404);
        if (numbers.AlreadyVoided(normalized, number))
            return (Envelope.FailBody("NUMBER_ALREADY_VOIDED", $"{number} is already voided."), 409);
        var v = new NumberSeriesVoid($"NSV-{number}", ec.TenantId, ec.EntityId, normalized, number, request.Reason.Trim(), u.Id, DateTimeOffset.UtcNow);
        numbers.AddVoid(v);
        audit.Log(u, "void", "number_series", $"{normalized}/{number}",
            $"Doc number {number} voided from series {normalized}: {request.Reason.Trim()} (per policy the sequence does not reissue it).");
        audit.Emit(u, "finance.number-series.voided", new { prefix = normalized, number, reason = request.Reason.Trim(), voidedBy = u.Id });
        audit.Save(); // same scoped context — one SaveChanges commits the void + audit trail
        return (Envelope.OkBody(v), 201);
    });
}

// 007: finance settings upsert — the functional-currency change is effective-dated, recorded in
// the setting row and audited; posted journals are never restated by any later change
[ApiController]
[Route("api/v1/finance-settings")]
public sealed class FinanceSettingsController(ICurrencyService currencies) : ControllerBase
{
    [HttpPost]
    public IResult Upsert(UpsertFinanceSettingRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("fx:write")) return (Envelope.FailBody("FORBIDDEN", "fx:write permission required."), 403);
        var (setting, code, error) = currencies.UpsertSetting(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(setting!), 200);
    });
}
