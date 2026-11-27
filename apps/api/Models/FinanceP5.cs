namespace Erp.Api;

// --- FINANCE-deep P5 foundations: currency/FX master (006/008), dimensions (003),
// number-series voids (016). Tenant+entity on every table; ids from the shared doc_ sequences.

public record Currency(string Id, string TenantId, string LegalEntityId, string Code, string Name, int DecimalScale, bool Active);

// 008: provenance rides on the row — source, retrieved-at, rate date, precision, approver
public enum FxRateType { Spot, PeriodAverage, Closing }
public enum FxRateStatus { Pending, Approved }

public record FxRate(string Id, string TenantId, string LegalEntityId, string BaseCurrency, string QuoteCurrency,
    DateOnly RateDate, FxRateType RateType, decimal Rate, int Precision, string Source, DateTimeOffset RetrievedAt,
    FxRateStatus Status, string CreatedBy, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null);

// 003: a set groups permitted values; Required=true ⇒ every journal line must carry it
public record DimensionSet(string Id, string TenantId, string LegalEntityId, string Code, string Name, bool Required, bool Active);

// a permitted value with its own effective interval — inactive-but-in-interval references stay
// valid for historical lines (003 AC2), out-of-interval or unknown values are rejected
public record DimensionValue(string Id, string TenantId, string LegalEntityId, string SetCode, string Value,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool Active);

// 016: void bookkeeping over PG doc_ sequences — the sequence itself never reissues
public record NumberSeriesVoid(string Id, string TenantId, string LegalEntityId, string Prefix, string Number, string Reason, string VoidedBy, DateTimeOffset VoidedAt);

// projection for GET /api/v1/number-series (admin view over pg_sequences + void counts)
public record NumberSeriesView(string Prefix, long? LastValue, string LastNumber, int VoidedCount);
