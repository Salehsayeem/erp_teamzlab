using Erp.Api.Interfaces;

namespace Erp.Api.Interfaces;

// PEOPLE deep storage — one repository for the worker/time/payroll aggregates (SourcingRepository
// precedent: one repo per slice, scoped queries filter TenantId + LegalEntityId on every read).
// Status flips go through focused Mark* methods so the concurrency-token columns (employment,
// time_correction, pay_run status) carry WHERE status = <loaded> in every UPDATE.
public interface IPeopleRepository
{
    // worker register
    Employment? FindWorker(string workerId);
    List<Employment> AllWorkers(EmploymentStatus? status, string? search);
    Employment? FindByNationalId(string normalizedNationalId);
    Employment? FindByEmail(string normalizedEmail);
    void AddWorker(Employment worker);
    void SetWorkerStatus(Employment worker, EmploymentStatus status, DateOnly? terminationDate, string? terminationReason);

    // effective-dated compensation (024)
    void AddCompensation(CompensationEvent evt);
    void CloseCompensation(CompensationEvent evt, DateOnly effectiveTo);
    List<CompensationEvent> CompensationsFor(string employmentId);
    CompensationEvent? ApplicableCompensation(string employmentId, DateOnly asOf);

    // pay groups + memberships (035)
    PayGroup? FindPayGroup(string payGroupId);
    List<PayGroup> AllPayGroups();
    void AddPayGroup(PayGroup group);
    List<WorkerPayGroup> MembershipsFor(string employmentId);
    void AddMembership(WorkerPayGroup membership);
    List<Employment> WorkersInGroup(string payGroupId);

    // time capture (026)
    void AddTimeEntry(TimeEntry entry);
    TimeEntry? FindTimeEntry(string entryId);
    List<TimeEntry> TimeEntriesFor(string? employmentId, TimeEntryStatus? status);
    List<TimeEntry> TimeInWindow(DateOnly from, DateOnly to, TimeEntryStatus status, IReadOnlyCollection<string> employmentIds);
    void SetTimeEntryDecision(TimeEntry entry, TimeEntryStatus status, string? decisionReason);
    void ApplyCorrectedHours(TimeEntry entry, decimal hours, decimal overtimeHours);
    void SetCorrectionDecision(TimeCorrection correction, TimeCorrectionStatus status, string approvedBy, DateTimeOffset approvedAt, string? decisionReason);

    // corrections (027)
    void AddCorrection(TimeCorrection correction);
    TimeCorrection? FindCorrection(string correctionId);
    List<TimeCorrection> CorrectionsFor(string? employmentId);
    bool EntryHasPostedLine(string timeEntryId);

    // earning/deduction catalog (037)
    void AddPayCode(EarningDeductionCode code);
    EarningDeductionCode? FindPayCode(string code);
    EarningDeductionCode? FindPayCodeById(string payCodeId);
    List<EarningDeductionCode> AllPayCodes(PayCodeKind? kind);

    // pay runs (048/049)
    void AddPayRun(PayRun run);
    PayRun? FindPayRun(string runId);
    List<PayRun> AllPayRuns(PayRunStatus? status, string? periodId);
    PayRun? FindLiveRun(string payGroupId, string periodId);
    void MarkCalculated(PayRun run, decimal gross, decimal deductions, decimal net, string by, DateTimeOffset at);
    void ReplaceLines(PayRun run, IReadOnlyList<PayRunLine> lines);
    void MarkApproved(PayRun run, string by, DateTimeOffset at);
    void MarkPosted(PayRun run, string journalId, string by, DateTimeOffset at);
    void MarkReversed(PayRun run, string reason, bool fundsReleased);

    int Save();
}
