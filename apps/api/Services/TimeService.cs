using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Time capture + corrections (ERP-PEOPLE-025/026/027, trims per brief). Source ∈ {Self, Manager}
// is DERIVED: only the employment's own manager attests time (Manager → Approved at entry);
// anyone else's submission — including HR — lands Self/Pending and needs a time:approve decision
// (the 026 security trim: a manager cannot self-approve hidden time on another manager's report
// because only their own reports ever auto-approve). Corrections are proposed adjustments that
// keep the original entry + trail (027) and are rejected against already-posted periods (Q10).
public sealed class TimeService(
    IPeopleRepository people, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : ITimeService
{
    public object List(CurrentUser user, string? employmentId, TimeEntryStatus? status) =>
        people.TimeEntriesFor(employmentId, status).Select(Project).ToList();

    public (TimeEntry? entry, string? code, string? message) Create(CurrentUser user, Dtos.CreateTimeEntryRequest request, string? idemKey)
    {
        var worker = people.FindWorker(request.EmploymentId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");
        if (worker.Status == EmploymentStatus.Terminated)
            return (null, "WORKER_TERMINATED",
                $"Worker {worker.Id} was terminated on {worker.TerminationDate:yyyy-MM-dd}; no new time entries are accepted (007 AC2).");
        if (worker.Status is EmploymentStatus.Draft or EmploymentStatus.Prehire)
            return (null, "WORKER_NOT_ACTIVE",
                $"Worker {worker.Id} is {worker.Status}; time capture starts at Active (OnLeave/Suspended still accept time).");

        // 026 AC1: the attesting manager's entry is approved at creation — everyone else's is pending
        var isManager = user.Id == worker.ManagerUserId;
        var entry = new TimeEntry(numbers.Next("TME"), ec.TenantId, ec.EntityId, worker.Id,
            request.WorkDate, request.Hours, request.OvertimeHours,
            isManager ? TimeEntryStatus.Approved : TimeEntryStatus.Pending,
            isManager ? "Manager" : "Self", idemKey, request.Note, user.Id, DateTimeOffset.UtcNow);
        people.AddTimeEntry(entry);
        audit.Log(user, "create", "time_entry", entry.Id,
            $"Time entry {entry.Id} for {worker.Id} on {request.WorkDate:yyyy-MM-dd}: {request.Hours:0.##}h + {request.OvertimeHours:0.##}h OT " +
            $"(source {entry.Source}, {entry.Status})");
        people.Save();
        return (entry, null, null);
    }

    // 034: a pending entry must be approved before a run can pull it (approver ≠ requester)
    public (TimeEntry? entry, string? code, string? message) Decide(CurrentUser approver, string entryId, Dtos.DecideTimeEntryRequest request)
    {
        var entry = people.FindTimeEntry(entryId);
        if (entry is null) return (null, "TIME_ENTRY_NOT_FOUND", "Time entry not found.");
        if (entry.Status != TimeEntryStatus.Pending)
            return (null, "TIME_ENTRY_STATE_ERROR", $"Only pending time entries can be decided (current: {entry.Status}).");
        if (entry.CreatedBy == approver.Id)
            return (null, "TIME_APPROVAL_ERROR", "Maker-checker: the submitter cannot approve their own time entry.");

        var previousStatus = entry.Status; // same ordering rule as the worker transition audit (R4-4)
        people.SetTimeEntryDecision(entry, request.Approve ? TimeEntryStatus.Approved : TimeEntryStatus.Rejected, request.DecisionReason);
        audit.Log(approver, request.Approve ? "approve" : "reject", "time_entry", entryId,
            $"Time entry {entryId} {previousStatus} → {(request.Approve ? "Approved" : "Rejected")}" +
            (request.DecisionReason is null ? "" : $" — {request.DecisionReason}"));
        try
        {
            people.Save();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return (null, "TIME_ENTRY_ALREADY_DECIDED",
                $"Time entry {entryId} was decided concurrently; the retry carried a stale snapshot and wrote nothing.");
        }
        return (entry, null, null);
    }

    public object ListCorrections(CurrentUser user, string? employmentId, TimeCorrectionStatus? status) =>
        people.CorrectionsFor(employmentId)
            .Where(c => status == null || c.Status == status)
            .Select(ProjectCorrection).ToList();

    // 027 AC1: a correction never overwrites the original row — it proposes and trails
    public (TimeCorrection? correction, string? code, string? message) CreateCorrection(CurrentUser user, Dtos.CreateTimeCorrectionRequest request)
    {
        var entry = people.FindTimeEntry(request.TimeEntryId);
        if (entry is null) return (null, "TIME_ENTRY_NOT_FOUND", "Time entry not found.");
        if (people.EntryHasPostedLine(entry.Id))
            return (null, "TIME_PERIOD_POSTED",
                $"Time entry {entry.Id} is already payroll-posted; the correction waits for the next run (Q10, no retro runs).");

        var correction = new TimeCorrection(numbers.Next("TMC"), ec.TenantId, ec.EntityId, entry.Id,
            entry.Hours, entry.OvertimeHours, request.ProposedHours, request.ProposedOvertimeHours,
            request.Reason, TimeCorrectionStatus.Pending, null, user.Id, null, null, user.Id, DateTimeOffset.UtcNow);
        people.AddCorrection(correction);
        audit.Log(user, "propose", "time_correction", correction.Id,
            $"Correction {correction.Id} proposed for entry {entry.Id}: {correction.PreviousHours:0.##}h → {correction.ProposedHours:0.##}h " +
            $"(+{correction.PreviousOvertimeHours:0.##}h → +{correction.ProposedOvertimeHours:0.##}h OT) — {request.Reason}");
        people.Save();
        return (correction, null, null);
    }

    // 027: approver ≠ requester; approval supersedes the entry's payable hours, the correction
    // row keeps previous/proposed/delta; concurrency token backs a stale second decision
    public (TimeCorrection? correction, string? code, string? message) DecideCorrection(CurrentUser approver, string correctionId, Dtos.DecideTimeCorrectionRequest request)
    {
        var correction = people.FindCorrection(correctionId);
        if (correction is null) return (null, "TIME_CORRECTION_NOT_FOUND", "Time correction not found.");
        if (correction.Status != TimeCorrectionStatus.Pending)
            return (null, "TIME_CORRECTION_STATE_ERROR", $"Only pending corrections can be decided (current: {correction.Status}).");
        if (correction.RequestedBy == approver.Id)
            return (null, "TIME_APPROVAL_ERROR", "Maker-checker: the requester cannot approve their own correction.");
        // fix R4-3 (TOCTOU): the entry may have fed a POSTED run since the correction was proposed —
        // re-check at decision time so approval can never silently overwrite posted payable hours
        if (request.Approve && people.EntryHasPostedLine(correction.TimeEntryId))
            return (null, "TIME_PERIOD_POSTED",
                $"Time entry {correction.TimeEntryId} was payroll-posted after this correction was proposed; the decision waits for the next period (Q10).");

        var status = request.Approve ? TimeCorrectionStatus.Approved : TimeCorrectionStatus.Rejected;
        people.SetCorrectionDecision(correction, status, approver.Id, DateTimeOffset.UtcNow, request.DecisionReason);
        if (request.Approve)
        {
            var entry = people.FindTimeEntry(correction.TimeEntryId);
            if (entry is not null) people.ApplyCorrectedHours(entry, correction.ProposedHours, correction.ProposedOvertimeHours);
        }
        audit.Log(approver, request.Approve ? "approve" : "reject", "time_correction", correctionId,
            $"Correction {correctionId} {status}: entry {correction.TimeEntryId} " +
            (request.Approve ? $"payable hours now {correction.ProposedHours:0.##}h" : "proposal declined") +
            (request.DecisionReason is null ? "" : $" — {request.DecisionReason}"));
        if (request.Approve)
            audit.Emit(approver, "people.time.corrected", new
            {
                correctionId, timeEntryId = correction.TimeEntryId, employmentId = people.FindTimeEntry(correction.TimeEntryId)?.EmploymentId,
                previousHours = correction.PreviousHours, proposedHours = correction.ProposedHours,
                approvedBy = approver.Id,
            });
        try
        {
            people.Save();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return (null, "TIME_CORRECTION_ALREADY_DECIDED",
                $"Correction {correctionId} was decided concurrently; the retry carried a stale snapshot and wrote nothing.");
        }
        return (correction, null, null);
    }

    private static object Project(TimeEntry t) => new
    {
        t.Id, t.EmploymentId, t.WorkDate, t.Hours, t.OvertimeHours, t.Status, t.Source, t.IdemKey, t.Note, t.CreatedBy, t.CreatedAt,
    };

    private static object ProjectCorrection(TimeCorrection c) => new
    {
        c.Id, c.TimeEntryId, c.PreviousHours, c.PreviousOvertimeHours, c.ProposedHours, c.ProposedOvertimeHours,
        deltaHours = c.ProposedHours - c.PreviousHours,
        deltaOvertimeHours = c.ProposedOvertimeHours - c.PreviousOvertimeHours,
        c.Reason, c.Status, c.DecisionReason, c.RequestedBy, c.ApprovedBy, c.ApprovedAt, c.CreatedAt,
    };
}
