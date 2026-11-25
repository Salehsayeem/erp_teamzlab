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

// ---- deferred screens wave (PM1–PM10) ----------------------------------------------
// Same conventions as the landed slice: tenant+entity on every table from day one, ids from the
// shared doc_ sequences (or deterministic natural keys for child/dedup rows), enums append-only.
// Rulings applied: Q-PM1 (leave approval emits a dated absence TimeEntry on the 034 contract),
// Q-PM5 (second HR user seeds maker-checker), maker-checker per brief §3.

// PM1 — org hierarchy (002): effective-dated units; the parent interval-overlap check is the
// service's AC; LocationId references WorkLocation.
public record OrgUnit(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string? ParentOrgUnitId, string? LocationId, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string CreatedBy, DateTimeOffset CreatedAt);

// PM4 shares this table: the IANA timezone is what makes schedule expansion produce unambiguous
// instants (025 AC2).
public record WorkLocation(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string Timezone, string? Address, bool Active);

public enum PositionStatus { Open, Closed }

// PM1/PM3/PM9: budget authorization (008) — BudgetedFte/BudgetedHeadcount are the limits the
// assignment gate enforces; GradeCode ties compensation policy checks (023); RequiredCredentialCode
// ties the credential gate (058 → assignment).
public record Position(string Id, string TenantId, string LegalEntityId, string Code, string Title,
    string? OrgUnitId, string? GradeCode, string? RequiredCredentialCode,
    decimal BudgetedFte, int BudgetedHeadcount, PositionStatus Status,
    string? SourcePlanId, string? SourcePlanLineCode, string CreatedBy, DateTimeOffset CreatedAt);

public enum AssignmentStatus { PendingException, Active, Ended }

// 005/008/058: over-limit, out-of-employment or missing-credential assignments land as
// PendingException (never silently) and need an R-FIN exception decision (payroll:approve,
// approver ≠ maker) to activate; the approved variance is logged on the row.
public record PositionAssignment(string Id, string TenantId, string LegalEntityId, string PositionId,
    string EmploymentId, decimal Fte, string? CostCenter, string? ManagerUserId, string? LocationId,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, AssignmentStatus Status,
    string? ExceptionApprovedBy, decimal? ApprovedVariance, string? ExceptionReason,
    string CreatedBy, DateTimeOffset CreatedAt);

// PM2 — onboarding tasks derive from category-scoped templates (011 AC1); deterministic natural
// key {categoryCode}:{taskKey} so re-generation converges on the same template set.
public record OnboardingTemplate(string Id, string TenantId, string LegalEntityId, string CategoryCode,
    string TaskKey, string Name, int DueOffsetDays, string OwnerRole, bool Required, bool Active,
    string CreatedBy, DateTimeOffset CreatedAt);

public enum OnboardingTaskStatus { Pending, Completed, Waived }

// Overdue is DERIVED on read (Pending && DueDate < today) — never stored, so it cannot rot.
// Every waiver records the named approver + reason (011 AC2).
public record OnboardingTask(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string TemplateId, string TaskKey, string Name, DateOnly DueDate, string OwnerRole, bool Required,
    OnboardingTaskStatus Status, string? DecisionReason, string? DecidedBy, DateTimeOffset? DecidedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

// PM3 — versioned catalog (022): new versions are NEW rows; historic references keep the version
// id they were attached to, so a catalog change never rewrites an assignment's attached version.
public record JobProfile(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string? Department, bool Active, string CreatedBy, DateTimeOffset CreatedAt);

public record JobProfileVersion(string Id, string TenantId, string LegalEntityId, string ProfileId,
    int Version, string Title, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string CreatedBy, DateTimeOffset CreatedAt);

public record Competency(string Id, string TenantId, string LegalEntityId, string ProfileVersionId,
    string Name, string Level, bool Required);

// 023 (money): min ≤ mid ≤ max validated at the write; Version bumps on correction.
public record GradeRange(string Id, string TenantId, string LegalEntityId, string GradeCode,
    string Currency, string Frequency, decimal MinAmount, decimal MidAmount, decimal MaxAmount,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, int Version, bool Active,
    string CreatedBy, DateTimeOffset CreatedAt);

// PM4 — versioned schedules with weekly planned hours (025 money-input) + named IANA timezone.
public record WorkSchedule(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string Timezone, string StartTimeLocal, bool Active, string CreatedBy, DateTimeOffset CreatedAt);

public record ScheduleVersion(string Id, string TenantId, string LegalEntityId, string ScheduleId,
    int Version, decimal WeeklyPlannedHours, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? HolidayCalendarId, string CreatedBy, DateTimeOffset CreatedAt);

public record HolidayCalendar(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    bool Active, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<HolidayDate> Dates { get; set; } = [];
}

public record HolidayDate(string Name, DateOnly Date);

// PM5 — leave types carry the policy (029): missing/inactive policy is an explicit config error,
// never a zero entitlement.
public record LeaveType(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string Units, string AccrualMethod, decimal? AccrualRatePerMonth, decimal? AnnualEntitlementDays,
    decimal? CarryoverLimitDays, bool RequiresApproval, bool Active,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string CreatedBy, DateTimeOffset CreatedAt);

// 030/032 quantity ledger: Qty is signed; (tenant, entity, employment, type, period, sourceKey)
// unique IS the accrual idempotency key; Kind ∈ Accrual | Absence | AbsenceReversal | Adjustment |
// Carryover; ReferenceId links reversals back to what they reverse.
public record LeaveLedgerEntry(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string LeaveTypeId, string Period, string SourceKey, string Kind, decimal Qty, DateOnly EntryDate,
    string? ReferenceId, string CreatedBy, DateTimeOffset CreatedAt);

public enum LeaveRequestStatus { Pending, Approved, Rejected, Withdrawn }

// 031: balance + schedule checked pre-submission; approval (≠ requester) emits the dated absence
// event; withdrawal only via the recorded workflow endpoint (guarded status flip).
public record LeaveRequest(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string LeaveTypeId, DateOnly FromDate, DateOnly ToDate, decimal Days, LeaveRequestStatus Status,
    string Reason, string? DecisionReason, string RequestedBy, string? DecidedBy, DateTimeOffset? DecidedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

public enum LeaveAdjustmentStatus { Pending, Applied, Rejected }

// 032: manual entries need elevated rights (leave:adjust = R-FIN) AND independent approval
// (decider ≠ requester); the applied entry is a signed ledger row referencing this adjustment.
public record LeaveAdjustment(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string LeaveTypeId, decimal Qty, string Reason, LeaveAdjustmentStatus Status,
    string? DecisionReason, string RequestedBy, string? DecidedBy, DateTimeOffset? DecidedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

// 033: SourceFact is what schedule+entry said at generation time (immutable); Disposition is the
// reviewer's call — the two stay distinguishable forever. Deterministic natural key
// {employmentId}:{workDate}:{kind} IS the dedupe (re-generation converges on existing rows).
public record AttendanceException(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    DateOnly WorkDate, string Kind, string SourceFact, string? Disposition, string? DispositionReason,
    string? DispositionedBy, DateTimeOffset? DispositionedAt, string CreatedBy, DateTimeOffset CreatedAt);

// PM6 — elections (041): verification state is explicit; payroll selects the election effective
// at the pay date; a missing mandatory election surfaces as a blocking review, never a default.
public enum ElectionStatus { Draft, Verified, Revoked }

public record TaxElection(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string Kind, string? Value, ElectionStatus Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? VerifiedBy, DateTimeOffset? VerifiedAt, string CreatedBy, DateTimeOffset CreatedAt);

// 042 (money): effective-dated rates with threshold/ceiling; version bumps on edit.
public record ContributionRule(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string Party, decimal RatePct, decimal? ThresholdAmount, decimal? CeilingAmount, string PayCodeId,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, int Version, bool Active,
    string CreatedBy, DateTimeOffset CreatedAt);

public enum GarnishmentStatus { Active, Released, Expired }

// 043 (money): outside the effective period the order is unusable; release needs payroll:release
// (R-FIN) + evidence; two active orders with the same priority conflict → blocking exception.
public record GarnishmentOrder(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string Kind, int Priority, decimal AmountPerPeriod, decimal? ProtectedMinimum, string CourtRef,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, GarnishmentStatus Status, string? ReleaseEvidence,
    string? ReleasedBy, DateTimeOffset? ReleasedAt, string CreatedBy, DateTimeOffset CreatedAt);

// 044 (money): enrollment emits a versioned deduction instruction per period; provider-feed
// reconciliation flags dup/out-of-sync without ever overwriting the election/enrollment.
public record BenefitPlan(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string? ProviderCode, decimal EmployeePct, string PayCodeId, bool Active,
    string CreatedBy, DateTimeOffset CreatedAt);

public record BenefitEnrollment(string Id, string TenantId, string LegalEntityId, string PlanId,
    string EmploymentId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Status,
    string CreatedBy, DateTimeOffset CreatedAt);

// deterministic natural key {enrollmentId}:{period}:v{version}
public record DeductionInstruction(string Id, string TenantId, string LegalEntityId, string EnrollmentId,
    string Period, decimal Amount, int Version, string Source, string CreatedBy, DateTimeOffset CreatedAt);

// PM7 — payment files (050, maker-checker): generated only from an Approved/Posted run snapshot;
// release (payroll:release) is a DIFFERENT person from the file maker; the hash pins the content.
public enum PaymentFileStatus { Prepared, Released, PartiallyRejected }

public record PaymentFile(string Id, string TenantId, string LegalEntityId, string RunId,
    PaymentFileStatus Status, string Provider, string Format, string FormatVersion, string ContentHash,
    int ItemCount, decimal TotalNet, string Currency, DateOnly PayDate,
    string? ReleasedBy, DateTimeOffset? ReleasedAt, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<PaymentFileItem> Items { get; set; } = [];
}

public enum PaymentItemStatus { Pending, Accepted, Released, Rejected }

// lifecycle fields (Status/RejectReason) mutate through release/rejection — init-only
// positional records cannot express that, so the item is a mutable record
public record PaymentFileItem
{
    public PaymentFileItem() { }
    public PaymentFileItem(string employmentId, decimal netAmount, string currency)
    {
        EmploymentId = employmentId;
        NetAmount = netAmount;
        Currency = currency;
        Status = PaymentItemStatus.Pending;
    }

    public string EmploymentId { get; set; } = "";
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "";
    public PaymentItemStatus Status { get; set; }
    public string? RejectReason { get; set; }
}

// PM8 — effective-dated account mappings (053 AC2): an ACTIVE mapping overrides the landed
// constant for its element at posting time; an INACTIVE mapping that would cover the date BLOCKS
// posting and names the element (explicit config error, never substitution). Natural key
// map:{element}:{effectiveFrom:yyyyMMdd}.
public record PayrollAccountMapping(string Id, string TenantId, string LegalEntityId, string Element,
    string AccountCode, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool Active,
    string CreatedBy, DateTimeOffset CreatedAt);

public enum PayPeriodCloseStatus { PendingAcknowledgment, Closed, Reopened }

// 054 (maker-checker): Id IS the periodId — one close record per period, resumable from
// PendingAcknowledgment without duplicate execution. SummaryJson + its SHA-256 = the signed summary.
public record PayrollPeriodClose(string Id, string TenantId, string LegalEntityId, string PeriodId,
    PayPeriodCloseStatus Status, string? SummaryJson, string? SummaryHash, string ClosedBy,
    DateTimeOffset ClosedAt, string? ReopenedBy, DateTimeOffset? ReopenedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

// PM9 — credentials (058): effective status derives Current + expired date → Expired on read;
// waivers record the named approver + reason and flip the stored status to Waived.
public enum CredentialStatus { Current, Expired, Waived, Unverified }

public record Credential(string Id, string TenantId, string LegalEntityId, string EmploymentId,
    string Code, string Name, string? IssuedBy, DateOnly? IssuedOn, DateOnly? ExpiresOn,
    CredentialStatus Status, string? EvidenceRef, string? WaiverReason, string? WaivedBy,
    DateTimeOffset? WaivedAt, string CreatedBy, DateTimeOffset CreatedAt);

// PM10 — versioned FTE plans (059): promotion into positions is an explicit workflow endpoint
// that creates linked Position rows (SourcePlanId/SourcePlanLineCode) and marks the line promoted.
public record WorkforcePlan(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    DateOnly HorizonStart, DateOnly HorizonEnd, string OwnerUserId, string? Assumptions, string Status,
    string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<WorkforcePlanLine> Lines { get; set; } = [];
}

// Promoted/PromotedPositionId flip through the explicit promotion workflow — mutable record
public record WorkforcePlanLine
{
    public WorkforcePlanLine() { }
    public WorkforcePlanLine(string positionCode, string title, decimal fte, string? orgUnitId, string? gradeCode)
    {
        PositionCode = positionCode;
        Title = title;
        Fte = fte;
        OrgUnitId = orgUnitId;
        GradeCode = gradeCode;
    }

    public string PositionCode { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal Fte { get; set; }
    public string? OrgUnitId { get; set; }
    public string? GradeCode { get; set; }
    public bool Promoted { get; set; }
    public string? PromotedPositionId { get; set; }
}

// 060 (money): approval requires Σ pct == 100 (configured unit); inactive cost objects are held
// for review on the line (recorded, never silently dropped).
public record AllocationSet(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string Unit, string Status, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<AllocationLine> Lines { get; set; } = [];
}

// HeldForReview is recorded at approval — mutable record
public record AllocationLine
{
    public AllocationLine() { }
    public AllocationLine(string costCenter, decimal pct)
    {
        CostCenter = costCenter;
        Pct = pct;
    }

    public string CostCenter { get; set; } = "";
    public decimal Pct { get; set; }
    public bool HeldForReview { get; set; }
}
