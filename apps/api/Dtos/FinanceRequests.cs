namespace Erp.Api.Dtos;

// task-5 finance requests — validated via Guards in the controller before the service runs.
// Trailing optional parameters keep positional construction compatible with the brief shapes.

public record CreatePeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate);
// WaiveBlockers=true is the 063 AC2 waiver variant: unresolved mandatory close tasks are waived
// (reason mandatory) and retained in close evidence instead of blocking the close.
public record ClosePeriodRequest(string? Reason, bool? WaiveBlockers = null);
public record ReopenPeriodRequest(string Reason);

public record CreateAccountRequest(string Code, string Name, AccountType Type, NormalBalance NormalBalance, bool DirectPostingAllowed);

// Reason is required for Basis Zero/Exempt (032 edge: distinct from an unconfigured rate)
public record CreateTaxCodeRequest(string Code, decimal RatePct, TaxBasis Basis, bool Recoverable, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Reason = null);

// Date is the journal's accounting date — the period lock and tax revisions resolve against it.
// Optional trailing (011 source idempotency keys on the whole payload; approval defaults it to
// creation date when absent).
public record CreateJournalRequest(string Memo, IReadOnlyList<JournalLineInput> Lines, DateOnly? Date = null);
// P5: currency (null = functional), the independently-stored transaction amount, and the line's
// dimension assignments — JournalService.Create validates account eligibility (002), currency
// existence/activity + settlement scale (006) and required-dimension coverage (003) per line.
public record JournalLineInput(string AccountCode, string AccountName, decimal Debit, decimal Credit, string? TaxCodeId = null,
    string? Currency = null, decimal? AmountInCurrency = null, Dictionary<string, string>? Dimensions = null);
public record ApproveJournalRequest(string? Reason);

// --- FINANCE-deep P5 foundations (006/007/008/003/016) ---

public record CreateCurrencyRequest(string Code, string Name, int DecimalScale, bool? Active = null);

public record CreateFxRateRequest(string BaseCurrency, string QuoteCurrency, DateOnly RateDate, FxRateType RateType,
    decimal Rate, int? Precision = null, string? Source = null);

// 008 fix round: withdraw a Pending/Approved rate out of the exactly-one window; reason mandatory
public record WithdrawFxRateRequest(string Reason);

public record CreateDimensionSetRequest(string Code, string Name, bool? Required = null);

public record CreateDimensionValueRequest(string SetCode, string Value, DateOnly EffectiveFrom, DateOnly? EffectiveTo = null, bool? Active = null);

// 007: upsert; finance.functional_currency values must cite an active seeded currency
public record UpsertFinanceSettingRequest(string Key, string Value, DateOnly? EffectiveFrom = null);

// 016: void a doc number with a mandatory reason; the sequence never reissues it
public record VoidNumberRequest(string Number, string Reason);

// --- FINANCE-deep P1 (004 calendars, 062/063 close) ---

public record CreateFiscalCalendarRequest(string Name, int Year, string Pattern, DateOnly StartDate);

public record CloseTaskInput(string Title, bool Mandatory, DateOnly? DueDate = null, string? DependsOnTaskId = null);
public record CreateCloseChecklistRequest(string PeriodId, IReadOnlyList<CloseTaskInput> Tasks);
public record AddCloseTaskRequest(string Title, bool Mandatory, DateOnly? DueDate = null, string? DependsOnTaskId = null);

// 062 AC1: completion needs an evidence ref OR an authorized waiver reason — both absent is a 400
public record CompleteCloseTaskRequest(string? EvidenceRef, string? WaiverReason);

// 062 AC3: N/A carries a reason; the row is retained, never silently removed
public record NaCloseTaskRequest(string Reason);
