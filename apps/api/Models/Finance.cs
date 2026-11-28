namespace Erp.Api;

public enum JournalStatus { Pending, Posted }

// trailing optional tax fields (task-5): taxCodeId pins the resolved revision row (032 provenance),
// taxAmount is the signed rounded tax for the line — Round((Debit−Credit) × RatePct/100, 2, AwayFromZero).
// Existing 4-field call sites compile unchanged.
// P5 (006/007/003): optional transaction currency (null = the functional currency), the
// independently-stored transaction amount, and the line's dimension assignments —
// validated at journal create (currency exists/active, scale, required dimensions).
public record JournalLine(string AccountCode, string AccountName, decimal Debit, decimal Credit, string? TaxCodeId = null, decimal TaxAmount = 0,
    string? Currency = null, decimal? AmountInCurrency = null, Dictionary<string, string>? Dimensions = null);

// Lines as a settable body property for EF (see PurchaseOrder) — same JSON shape, same call sites
public record Journal(string Id, string TenantId, string LegalEntityId, JournalStatus Status, string Memo, DateTimeOffset CreatedAt, string CreatedBy, string? ReversalOfId = null, DateOnly? AccountingDate = null)
{
    public List<JournalLine> Lines { get; set; } = [];
    public Journal(string id, string tenantId, string legalEntityId, JournalStatus status, string memo,
        IReadOnlyList<JournalLine> lines, DateTimeOffset createdAt, string createdBy, string? reversalOfId = null, DateOnly? accountingDate = null)
        : this(id, tenantId, legalEntityId, status, memo, createdAt, createdBy, reversalOfId, accountingDate)
    { Lines = [.. lines]; }

    // the accounting date the period lock and tax revisions resolve against (005/032); defaults to the creation date
    public DateOnly EffectiveDate => AccountingDate ?? DateOnly.FromDateTime(CreatedAt.UtcDateTime);
}

// --- task-5: fiscal periods, chart of accounts, tax codes, finance settings ---

// 005: reopen is an event (audited reason), not a status — SoftClosed/Closed both reject posting (R9e)
public enum PeriodStatus { Open, SoftClosed, Closed }

// 004: entity-specific calendar; LE-001 seeds plain calendar months (R9d — 4-4-5 deferred).
// P1: IsAdjustment marks the designated adjustment period (late events route there, 063 edge).
public record FiscalPeriod(string Id, string TenantId, string LegalEntityId, string Name, DateOnly StartDate, DateOnly EndDate, PeriodStatus Status, string? ClosedBy = null, DateTimeOffset? ClosedAt = null, string? ReopenReason = null, bool IsAdjustment = false);

// 001/002: versioned accounts with type, normal balance and direct-posting eligibility
public enum AccountType { Asset, Liability, Equity, Revenue, Expense }
public enum NormalBalance { Debit, Credit }

public record Account(string Id, string TenantId, string LegalEntityId, string Code, string Name, AccountType Type, NormalBalance NormalBalance, bool DirectPostingAllowed, int Version, string CreatedBy, DateTimeOffset CreatedAt);

// 032: one row per published revision — revisions of a code share Code, disjoint effective ranges
public enum TaxBasis { Standard, Zero, Exempt }

public record TaxCode(string Id, string TenantId, string LegalEntityId, string Code, string Jurisdiction, decimal RatePct, TaxBasis Basis, bool Recoverable, DateOnly EffectiveFrom, DateOnly? EffectiveTo, int Version, string PublishedBy, DateTimeOffset PublishedAt);

// R9b: aging bucket boundaries + credit terms live as data, never as code constants.
// P5 (007): changes are effective-dated — EffectiveFrom plus who/when (audit keeps the trail);
// trailing optional parameters keep every existing constructor call site compiling.
public record FinanceSetting(string Id, string TenantId, string LegalEntityId, string Key, string Value,
    DateOnly? EffectiveFrom = null, string? UpdatedBy = null, DateTimeOffset? UpdatedAt = null);
