namespace Erp.Api;

// PEOPLE deep domain records (ERP-PEOPLE-003/004/005/006/007/024/025/026/027/035/037/038/039/
// 046-049/051/053/055/056). Rulings R-PEO1..11 are binding; the big ones:
//  • R-PEO3 ONE-STEP register — there is NO person entity in the pilot; the person-identity
//    fields (FullName/PreferredName/NationalId/Email/Phone) ride the Employment row. The brief's
//    Person record and doc_per sequence therefore have no consumer (doc_per is still created so
//    the id-space stays reserved for a later person split).
//  • R-PEO4 one active employment per person — the rehire duplicate check surfaces a 409 with
//    the candidate id instead of auto-merging.
//  • R-PEO5 pay runs anchor to the existing FiscalPeriod rows via PayDate; no payroll calendar.
//  • R-PEO6/R-PEO7/R-PEO10 — net pay accrues in 2300 (no payment handoff), BDT only, no retro
//    runs: late time waits for the next period.
//
// Enum values are APPEND-ONLY (EF maps enums as ints — inserting mid-enum renumbers existing
// postgres rows), matching the SoStatus/InvoiceStatus convention.

public enum EmploymentStatus { Draft, Prehire, Active, OnLeave, Suspended, Terminated }

// R-PEO3: person identity + employment relationship live on one row. WorkerNo is the WKR doc
// number (same value as Id). Termination never deletes history (007 AC2) — the row flips status
// and stamps date/reason; WeeklyPlannedHours is the 025 trim (schedule versioning deferred).
public record Employment(string Id, string TenantId, string LegalEntityId,
    string FullName, string? PreferredName, string? NationalId, string? Email, string? Phone,
    string WorkerNo, EmploymentStatus Status, string CategoryCode, string? ManagerUserId, string? LocationId,
    decimal WeeklyPlannedHours, DateOnly HireDate, DateOnly? TerminationDate, string? TerminationReason,
    string CreatedBy, DateTimeOffset CreatedAt);

// 024: effective-dated approved compensation. New events close the previously open range at
// EffectiveFrom-1, so a future-dated change co-exists with today's terms; as-of resolution is
// EffectiveFrom <= date && (EffectiveTo ?? ∞) >= date. Currency is validated == entity currency
// at creation (R-PEO7 single-currency BDT).
public record CompensationEvent(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    decimal Amount, string Currency, string PayBasis, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string Reason, string ApprovedBy, string CreatedBy, DateTimeOffset CreatedAt);

public enum TimeEntryStatus { Pending, Approved, Rejected }

// 026: Source ∈ {Self, Manager} is DERIVED, not client-asserted — only the employment's own
// manager can attest time (Source=Manager → Approved at entry); every other creator's entry is
// Self/Pending and needs a time:approve decision. IdemKey (the request Idempotency-Key) is
// unique per tenant+entity so a replayed submission can never become a second payable row.
// Corrections never overwrite the trail — they supersede via TimeCorrection.
public record TimeEntry(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    DateOnly WorkDate, decimal Hours, decimal OvertimeHours, TimeEntryStatus Status, string Source,
    string? IdemKey, string? Note, string CreatedBy, DateTimeOffset CreatedAt);

public enum TimeCorrectionStatus { Pending, Approved, Rejected }

// 027: proposed adjustments keep the audit trail — Previous*/Proposed* carry original and delta,
// the original entry row is never replaced, and approval swaps the entry's payable hours.
// Status is a concurrency token so a stale second decision updates zero rows.
public record TimeCorrection(string Id, string TenantId, string LegalEntityId, string TimeEntryId,
    decimal PreviousHours, decimal PreviousOvertimeHours, decimal ProposedHours, decimal ProposedOvertimeHours,
    string Reason, TimeCorrectionStatus Status, string? DecisionReason, string RequestedBy,
    string? ApprovedBy, DateTimeOffset? ApprovedAt, string CreatedBy, DateTimeOffset CreatedAt);

// 035 (trim): PG-001 seeded monthly; the membership table is the one-active-group-per-worker
// rule's backstop (R-PEO4-style single active membership, split-pay deferred). Deterministic
// natural key {employmentId}:{payGroupId}.
public record PayGroup(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string Frequency, bool Active);

public record WorkerPayGroup(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string PayGroupId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public enum PayCodeKind { Earning, Deduction }

// 037: versioned earning/deduction code with REQUIRED accounting mapping before activation.
// RatePct carries the rate — for Percent basis a percentage, for Amount basis the flat per-period
// value (the brief's single decimal column does double duty; the basis says which). A mapping
// change must create a new version; closed runs keep their frozen lines (037 AC2).
public record EarningDeductionCode(string Id, string TenantId, string LegalEntityId, string Code,
    PayCodeKind Kind, string Name, string CalcBasis, decimal? RatePct, string DebitAccount,
    string CreditAccount, bool Active, DateOnly EffectiveFrom, int Version, string CreatedBy, DateTimeOffset CreatedAt);

public enum PayRunStatus { Open, Calculated, Approved, Posted, Reversed }

// 048/049: the run is the frozen payroll document. (TenantId, LegalEntityId, PayGroupId, PeriodId)
// unique (excluding Reversed rows) IS the 049 run key; Status is a concurrency token so a stale
// second approval/post/reversal updates ZERO rows and surfaces as a 409 instead of double
// posting (T7 review-1a mechanism). Reversal runs are NEW ids linked via ReversalOfRunId.
public record PayRun(string Id, string TenantId, string LegalEntityId, string PayGroupId, string PeriodId,
    PayRunStatus Status, string Currency, DateOnly PayDate, decimal GrossTotal, decimal DeductionTotal,
    decimal NetTotal, string? CalculatedBy, DateTimeOffset? CalculatedAt, string? ApprovedBy,
    DateTimeOffset? ApprovedAt, string? JournalId, string? PostedBy, DateTimeOffset? PostedAt,
    string? ReversalOfRunId, string? ReversalReason, bool? FundsReleased, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<PayRunLine> Lines { get; set; } = [];
}

// 038 AC2: every line identifies its source input (SourceEntryId = the time entry for time-based
// lines, null for period-flat lines), code, basis-quantity, rate, amount and currency; amounts
// are frozen at calculation so a later code-version change cannot rewrite a closed run. Owned
// collection like invoice_lines — the PK is a generated shadow line number, no client ids.
public record PayRunLine(string EmploymentId, string PayCodeId, string PayCode, PayCodeKind Kind,
    decimal Hours, decimal Rate, decimal Amount, string Currency, string? SourceEntryId);
