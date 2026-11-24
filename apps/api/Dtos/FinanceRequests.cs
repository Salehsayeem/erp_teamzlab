namespace Erp.Api.Dtos;

// task-5 finance requests — validated via Guards in the controller before the service runs.
// Trailing optional parameters keep positional construction compatible with the brief shapes.

public record CreatePeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate);
public record ClosePeriodRequest(string? Reason);
public record ReopenPeriodRequest(string Reason);

public record CreateAccountRequest(string Code, string Name, AccountType Type, NormalBalance NormalBalance, bool DirectPostingAllowed);

// Reason is required for Basis Zero/Exempt (032 edge: distinct from an unconfigured rate)
public record CreateTaxCodeRequest(string Code, decimal RatePct, TaxBasis Basis, bool Recoverable, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Reason = null);

// Date is the journal's accounting date — the period lock and tax revisions resolve against it.
// Optional trailing (011 source idempotency keys on the whole payload; approval defaults it to
// creation date when absent).
public record CreateJournalRequest(string Memo, IReadOnlyList<JournalLineInput> Lines, DateOnly? Date = null);
public record JournalLineInput(string AccountCode, string AccountName, decimal Debit, decimal Credit, string? TaxCodeId = null);
public record ApproveJournalRequest(string? Reason);
