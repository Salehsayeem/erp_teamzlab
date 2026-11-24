using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// PEOPLE deep storage over ErpDbContext (see IPeopleRepository for the status-flip/token rules).
public sealed class PeopleRepository(ErpDbContext db, EntityContext ec) : IPeopleRepository
{
    private IQueryable<Employment> WorkerScope() =>
        db.Employments.Where(w => w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId);

    private IQueryable<TimeEntry> TimeScope() =>
        db.TimeEntries.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId);

    private IQueryable<PayRun> RunScope() =>
        db.PayRuns.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    // ---- worker register -------------------------------------------------------------

    public Employment? FindWorker(string workerId) => WorkerScope().FirstOrDefault(w => w.Id == workerId);

    public List<Employment> AllWorkers(EmploymentStatus? status, string? search) =>
        WorkerScope()
            .Where(w => status == null || w.Status == status)
            .Where(w => search == null
                || w.FullName.ToLower().Contains(search.ToLower())
                || (w.NationalId != null && w.NationalId.Contains(search)))
            .OrderBy(w => w.Id).ToList();

    // 004 edge: normalized exact match (stored ids are already Trim+ToLower)
    public Employment? FindByNationalId(string normalizedNationalId) =>
        WorkerScope().FirstOrDefault(w => w.NationalId == normalizedNationalId);

    public Employment? FindByEmail(string normalizedEmail) =>
        WorkerScope().FirstOrDefault(w => w.Email == normalizedEmail);

    public void AddWorker(Employment worker) => db.Employments.Add(worker);

    public void SetWorkerStatus(Employment worker, EmploymentStatus status, DateOnly? terminationDate, string? terminationReason)
    {
        db.Entry(worker).Property(nameof(Employment.Status)).CurrentValue = status;
        db.Entry(worker).Property(nameof(Employment.TerminationDate)).CurrentValue = terminationDate;
        db.Entry(worker).Property(nameof(Employment.TerminationReason)).CurrentValue = terminationReason;
    }

    // ---- compensation ----------------------------------------------------------------

    public void AddCompensation(CompensationEvent evt) => db.CompensationEvents.Add(evt);

    public void CloseCompensation(CompensationEvent evt, DateOnly effectiveTo) =>
        db.Entry(evt).Property(nameof(CompensationEvent.EffectiveTo)).CurrentValue = effectiveTo;

    public List<CompensationEvent> CompensationsFor(string employmentId) =>
        db.CompensationEvents.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId
                && c.EmploymentId == employmentId)
            .OrderBy(c => c.EffectiveFrom).ThenBy(c => c.Id).ToList();

    // 024 AC1: the event whose range covers the as-of date; later-starting ranges win
    public CompensationEvent? ApplicableCompensation(string employmentId, DateOnly asOf) =>
        db.CompensationEvents.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId
                && c.EmploymentId == employmentId
                && c.EffectiveFrom <= asOf
                && (c.EffectiveTo == null || c.EffectiveTo >= asOf))
            .OrderByDescending(c => c.EffectiveFrom).FirstOrDefault();

    // ---- pay groups --------------------------------------------------------------------

    public PayGroup? FindPayGroup(string payGroupId) =>
        db.PayGroups.FirstOrDefault(g => g.Id == payGroupId
            && g.TenantId == ec.TenantId && g.LegalEntityId == ec.EntityId);

    public List<PayGroup> AllPayGroups() =>
        db.PayGroups.Where(g => g.TenantId == ec.TenantId && g.LegalEntityId == ec.EntityId)
            .OrderBy(g => g.Id).ToList();

    public void AddPayGroup(PayGroup group) => db.PayGroups.Add(group);

    public List<WorkerPayGroup> MembershipsFor(string employmentId) =>
        db.WorkerPayGroups.Where(m => m.TenantId == ec.TenantId && m.LegalEntityId == ec.EntityId
                && m.EmploymentId == employmentId)
            .OrderBy(m => m.EffectiveFrom).ToList();

    public void AddMembership(WorkerPayGroup membership) => db.WorkerPayGroups.Add(membership);

    // run population (034): every non-terminated, onboarded member of the group
    public List<Employment> WorkersInGroup(string payGroupId) =>
        db.WorkerPayGroups.Where(m => m.TenantId == ec.TenantId && m.LegalEntityId == ec.EntityId
                && m.PayGroupId == payGroupId)
            .Join(WorkerScope(), m => m.EmploymentId, w => w.Id, (m, w) => w)
            .Where(w => w.Status == EmploymentStatus.Active
                || w.Status == EmploymentStatus.OnLeave
                || w.Status == EmploymentStatus.Suspended) // run population (034): no Draft/Prehire/Terminated
            .OrderBy(w => w.Id).ToList();

    // ---- time ------------------------------------------------------------------------

    public void AddTimeEntry(TimeEntry entry) => db.TimeEntries.Add(entry);

    public TimeEntry? FindTimeEntry(string entryId) => TimeScope().FirstOrDefault(t => t.Id == entryId);

    public List<TimeEntry> TimeEntriesFor(string? employmentId, TimeEntryStatus? status) =>
        TimeScope()
            .Where(t => employmentId == null || t.EmploymentId == employmentId)
            .Where(t => status == null || t.Status == status)
            .OrderBy(t => t.Id).ToList();

    public List<TimeEntry> TimeInWindow(DateOnly from, DateOnly to, TimeEntryStatus status,
        IReadOnlyCollection<string> employmentIds) =>
        employmentIds.Count == 0
            ? []
            : TimeScope()
                .Where(t => t.WorkDate >= from && t.WorkDate <= to && t.Status == status
                    && employmentIds.Contains(t.EmploymentId))
                .OrderBy(t => t.EmploymentId).ThenBy(t => t.WorkDate).ThenBy(t => t.Id)
                .ToList();

    public void SetTimeEntryDecision(TimeEntry entry, TimeEntryStatus status, string? decisionReason)
    {
        db.Entry(entry).Property(nameof(TimeEntry.Status)).CurrentValue = status;
        db.Entry(entry).Property(nameof(TimeEntry.Note)).CurrentValue =
            decisionReason is null ? entry.Note : $"{entry.Note} [{decisionReason}]";
    }

    // 027: approval supersedes the payable hours in place — the correction row keeps the trail
    public void ApplyCorrectedHours(TimeEntry entry, decimal hours, decimal overtimeHours)
    {
        db.Entry(entry).Property(nameof(TimeEntry.Hours)).CurrentValue = hours;
        db.Entry(entry).Property(nameof(TimeEntry.OvertimeHours)).CurrentValue = overtimeHours;
    }

    public void SetCorrectionDecision(TimeCorrection correction, TimeCorrectionStatus status,
        string approvedBy, DateTimeOffset approvedAt, string? decisionReason)
    {
        db.Entry(correction).Property(nameof(TimeCorrection.Status)).CurrentValue = status;
        db.Entry(correction).Property(nameof(TimeCorrection.ApprovedBy)).CurrentValue = approvedBy;
        db.Entry(correction).Property(nameof(TimeCorrection.ApprovedAt)).CurrentValue = approvedAt;
        db.Entry(correction).Property(nameof(TimeCorrection.DecisionReason)).CurrentValue = decisionReason;
    }

    // ---- corrections -----------------------------------------------------------------

    public void AddCorrection(TimeCorrection correction) => db.TimeCorrections.Add(correction);

    public TimeCorrection? FindCorrection(string correctionId) =>
        db.TimeCorrections.FirstOrDefault(c => c.Id == correctionId
            && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public List<TimeCorrection> CorrectionsFor(string? employmentId)
    {
        var query = db.TimeCorrections
            .Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);
        if (employmentId is not null)
            query = query.Where(c => db.TimeEntries.Any(t => t.Id == c.TimeEntryId && t.EmploymentId == employmentId));
        return query.OrderBy(c => c.Id).ToList();
    }

    // 027 trim (Q10): the entry fed a POSTED run — corrections wait for the next period
    public bool EntryHasPostedLine(string timeEntryId) =>
        RunScope().Any(r => r.Status == PayRunStatus.Posted && r.Lines.Any(l => l.SourceEntryId == timeEntryId));

    // ---- pay codes -------------------------------------------------------------------

    public void AddPayCode(EarningDeductionCode code) => db.EarningDeductionCodes.Add(code);

    public EarningDeductionCode? FindPayCode(string code) =>
        db.EarningDeductionCodes.FirstOrDefault(c => c.Code == code
            && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public EarningDeductionCode? FindPayCodeById(string payCodeId) =>
        db.EarningDeductionCodes.FirstOrDefault(c => c.Id == payCodeId
            && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public List<EarningDeductionCode> AllPayCodes(PayCodeKind? kind) =>
        db.EarningDeductionCodes.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId)
            .Where(c => kind == null || c.Kind == kind)
            .OrderBy(c => c.Code).ToList();

    // ---- pay runs --------------------------------------------------------------------

    public void AddPayRun(PayRun run)
    {
        db.PayRuns.Add(run);
        PinLines(run);
    }

    public PayRun? FindPayRun(string runId) =>
        RunScope().Include(r => r.Lines).OrderBy(r => r.Id).FirstOrDefault(r => r.Id == runId);

    public List<PayRun> AllPayRuns(PayRunStatus? status, string? periodId) =>
        RunScope().Include(r => r.Lines)
            .Where(r => status == null || r.Status == status)
            .Where(r => periodId == null || r.PeriodId == periodId)
            .OrderBy(r => r.Id).ToList();

    // 049 AC1: the live (non-reversed) run for a group+period key
    public PayRun? FindLiveRun(string payGroupId, string periodId) =>
        RunScope().Include(r => r.Lines)
            .FirstOrDefault(r => r.PayGroupId == payGroupId && r.PeriodId == periodId
                && r.Status != PayRunStatus.Reversed);

    public void MarkCalculated(PayRun run, decimal gross, decimal deductions, decimal net, string by, DateTimeOffset at)
    {
        db.Entry(run).Property(nameof(PayRun.Status)).CurrentValue = PayRunStatus.Calculated;
        db.Entry(run).Property(nameof(PayRun.GrossTotal)).CurrentValue = gross;
        db.Entry(run).Property(nameof(PayRun.DeductionTotal)).CurrentValue = deductions;
        db.Entry(run).Property(nameof(PayRun.NetTotal)).CurrentValue = net;
        db.Entry(run).Property(nameof(PayRun.CalculatedBy)).CurrentValue = by;
        db.Entry(run).Property(nameof(PayRun.CalculatedAt)).CurrentValue = at;
    }

    // 038 AC1: a rerun re-freezes byte-equal lines — detach the tracked snapshot, physically
    // delete the old rows (parameterized SQL), then insert the fresh set in ONE SaveChanges
    public void ReplaceLines(PayRun run, IReadOnlyList<PayRunLine> lines)
    {
        foreach (var entry in db.ChangeTracker.Entries<PayRunLine>().ToList()
                     .Where(e => (string?)e.Property("PayRunId").CurrentValue == run.Id
                         && (string?)e.Property("TenantId").CurrentValue == ec.TenantId
                         && (string?)e.Property("LegalEntityId").CurrentValue == ec.EntityId))
            entry.State = EntityState.Detached;
        db.Database.ExecuteSql(
            $"DELETE FROM pay_run_lines WHERE pay_run_id = {run.Id} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId}");
        run.Lines = [.. lines];
        PinLines(run);
    }

    public void MarkApproved(PayRun run, string by, DateTimeOffset at)
    {
        db.Entry(run).Property(nameof(PayRun.Status)).CurrentValue = PayRunStatus.Approved;
        db.Entry(run).Property(nameof(PayRun.ApprovedBy)).CurrentValue = by;
        db.Entry(run).Property(nameof(PayRun.ApprovedAt)).CurrentValue = at;
    }

    public void MarkPosted(PayRun run, string journalId, string by, DateTimeOffset at)
    {
        db.Entry(run).Property(nameof(PayRun.Status)).CurrentValue = PayRunStatus.Posted;
        db.Entry(run).Property(nameof(PayRun.JournalId)).CurrentValue = journalId;
        db.Entry(run).Property(nameof(PayRun.PostedBy)).CurrentValue = by;
        db.Entry(run).Property(nameof(PayRun.PostedAt)).CurrentValue = at;
    }

    public void MarkReversed(PayRun run, string reason, bool fundsReleased)
    {
        db.Entry(run).Property(nameof(PayRun.Status)).CurrentValue = PayRunStatus.Reversed;
        db.Entry(run).Property(nameof(PayRun.ReversalReason)).CurrentValue = reason;
        db.Entry(run).Property(nameof(PayRun.FundsReleased)).CurrentValue = fundsReleased;
    }

    private void PinLines(PayRun run)
    {
        foreach (var line in run.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public int Save() => db.SaveChanges();
}
