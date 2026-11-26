using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// PM5 — leave types/policies, idempotent accrual ledger, requests with coverage approval,
// adjustments, attendance exception review (ERP-PEOPLE-029/030/031/032/033).
// Q-PM1 ruling: an APPROVED leave request emits a dated absence event on the 034 batch contract
// (an APPROVED Source=Leave TimeEntry) so the pay-run input pull stays one-source.
public sealed class LeaveService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : ILeaveService
{
    public object ListLeaveTypes(CurrentUser user) =>
        db.LeaveTypes.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId)
            .OrderBy(t => t.Code).ToList().Select(ProjectType).ToList();

    public (LeaveType? type, string? code, string? message) CreateLeaveType(CurrentUser user, Dtos.CreateLeaveTypeRequest request)
    {
        if (request.AccrualMethod is not ("None" or "Monthly" or "Annual"))
            return (null, "VALIDATION_ERROR", "AccrualMethod must be None, Monthly or Annual.");
        if (db.LeaveTypes.Any(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId && t.Code == request.Code))
            return (null, "LEAVE_TYPE_EXISTS", $"Leave type {request.Code} already exists.");
        var type = new LeaveType(numbers.Next("LTY"), ec.TenantId, ec.EntityId, request.Code, request.Name,
            request.Units, request.AccrualMethod, request.AccrualRatePerMonth, request.AnnualEntitlementDays,
            request.CarryoverLimitDays, request.RequiresApproval, true, request.EffectiveFrom,
            request.EffectiveTo, user.Id, DateTimeOffset.UtcNow);
        db.LeaveTypes.Add(type);
        audit.Log(user, "create", "leave_type", type.Id, $"Leave type {request.Code} ({request.AccrualMethod}, {request.Units}) created");
        db.SaveChanges();
        return (type, null, null);
    }

    // 030 AC1: the same period+source replay adds nothing — the unique index is the backstop and
    // the existing-entry scan is the skip. Missing/inactive accrual policy = explicit config error.
    public (object? data, string? code, string? message) RunAccrual(CurrentUser user, string leaveTypeId, Dtos.RunAccrualRequest request)
    {
        var type = FindType(leaveTypeId);
        if (type is null) return (null, "LEAVE_TYPE_NOT_FOUND", "Leave type not found.");
        if (!type.Active || !DeferredUtil.Covers(type.EffectiveFrom, type.EffectiveTo, request.AsOf))
            return (null, "LEAVE_POLICY_MISSING",
                $"Leave type {type.Code} has no policy effective {request.AsOf:yyyy-MM-dd} — missing config is an explicit error, never zero entitlement (029 AC2).");
        if (type.AccrualMethod == "None")
            return (null, "LEAVE_POLICY_MISSING",
                $"Leave type {type.Code} declares AccrualMethod None — there is no accrual policy to run; configure Monthly/Annual first (029 AC2).");
        if (type.AccrualMethod == "Monthly" && type.AccrualRatePerMonth is not { } monthly)
            return (null, "LEAVE_POLICY_MISSING", $"Leave type {type.Code} accrues Monthly but has no AccrualRatePerMonth (029 AC2).");
        if (type.AccrualMethod == "Annual" && type.AnnualEntitlementDays is not { } annual)
            return (null, "LEAVE_POLICY_MISSING", $"Leave type {type.Code} accrues Annual but has no AnnualEntitlementDays (029 AC2).");

        var workers = db.Employments.Where(w => w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId
                && w.Status == EmploymentStatus.Active
                && (request.EmploymentId == null || w.Id == request.EmploymentId))
            .Where(w => w.HireDate <= request.AsOf).OrderBy(w => w.Id).ToList();
        var period = request.Period;
        var created = new List<LeaveLedgerEntry>();
        foreach (var worker in workers)
        {
            var entryDate = request.AsOf < PeriodEnd(period) ? request.AsOf : PeriodEnd(period);
            var qty = type.AccrualMethod switch
            {
                "Monthly" => type.AccrualRatePerMonth ?? 0m,
                "Annual" => decimal.Round((type.AnnualEntitlementDays ?? 0m) / 12m, 2, MidpointRounding.AwayFromZero),
                _ => 0m,
            };
            if (qty > 0 && !LedgerExists(worker.Id, type.Id, period, $"accrual:{period}"))
                created.Add(AddEntry(worker.Id, type.Id, period, $"accrual:{period}", "Accrual", qty,
                    entryDate, null, user.Id));

            // 032 AC2: carryover on the boundary — the January run carries last year's balance up
            // to the type's limit as a dated Carryover entry
            if (qty > 0 && period.EndsWith("-01") && (type.CarryoverLimitDays ?? 0m) > 0m)
            {
                var boundary = new DateOnly(int.Parse(period[..4]) - 1, 12, 31);
                var prior = BalanceFor(worker.Id, type.Id, boundary);
                var carry = Math.Min(prior, type.CarryoverLimitDays ?? 0m);
                if (carry > 0m && !LedgerExists(worker.Id, type.Id, period, $"carryover:{period}"))
                    created.Add(AddEntry(worker.Id, type.Id, period, $"carryover:{period}", "Carryover",
                        carry, boundary, null, user.Id));
            }
        }
        audit.Log(user, "accrual_run", "leave_type", type.Id,
            $"Accrual run {type.Code} period {period}: {created.Count} new entr(y/ies) for {workers.Count} worker(s) (replay adds nothing)");
        db.SaveChanges();
        return (new { leaveTypeId = type.Id, period, created = created.Count, entries = created.Select(ProjectEntry) },
            null, null);
    }

    // 030 AC2: the balance is opening (zero) + dated entries ≤ asOf — reproducible by construction
    public (object? data, string? code, string? message) Balance(CurrentUser user, string workerId, string? leaveTypeId, DateOnly? asOf)
    {
        var worker = db.Employments.FirstOrDefault(w => w.Id == workerId
            && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");
        var at = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var entries = db.LeaveLedgerEntries.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId
                && e.EmploymentId == workerId && (leaveTypeId == null || e.LeaveTypeId == leaveTypeId)
                && e.EntryDate <= at)
            .OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToList();
        var balance = entries.Sum(e => e.Qty);
        return (new
        {
            workerId, asOf = at, balance,
            entries = entries.Select(ProjectEntry),
        }, null, null);
    }

    public object ListLeaveRequests(CurrentUser user, string? employmentId, LeaveRequestStatus? status) =>
        db.LeaveRequests.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId)
            .Where(r => employmentId == null || r.EmploymentId == employmentId)
            .Where(r => status == null || r.Status == status)
            .OrderBy(r => r.Id).ToList().Select(ProjectRequest).ToList();

    // 031 AC1: balance + policy checked PRE-submission; the request never lands on hope
    public (LeaveRequest? request, string? code, string? message) CreateLeaveRequest(CurrentUser user, Dtos.CreateLeaveRequestRequest request)
    {
        var type = FindType(request.LeaveTypeId);
        if (type is null) return (null, "LEAVE_TYPE_NOT_FOUND", "Leave type not found.");
        var worker = db.Employments.FirstOrDefault(w => w.Id == request.EmploymentId
            && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");
        if (request.Days <= 0) return (null, "VALIDATION_ERROR", "Days must be greater than 0.");
        if (request.ToDate < request.FromDate) return (null, "VALIDATION_ERROR", "ToDate must not precede FromDate.");
        if (!type.Active || !DeferredUtil.Covers(type.EffectiveFrom, type.EffectiveTo, request.FromDate))
            return (null, "LEAVE_POLICY_MISSING",
                $"Leave type {type.Code} has no policy covering {request.FromDate:yyyy-MM-dd} — requests on unconfigured policies are refused, never zero-rated (029 AC2).");
        var available = BalanceFor(request.EmploymentId, type.Id, request.FromDate);
        if (available < request.Days)
            return (null, "INSUFFICIENT_BALANCE",
                $"Worker {request.EmploymentId} holds {available:0.##} {type.Units} of {type.Code} as of {request.FromDate:yyyy-MM-dd}; the request needs {request.Days:0.##} (031 AC1 pre-submission check).");

        var leaveRequest = new LeaveRequest(numbers.Next("LRQ"), ec.TenantId, ec.EntityId,
            request.EmploymentId, type.Id, request.FromDate, request.ToDate, request.Days,
            LeaveRequestStatus.Pending, request.Reason, null, user.Id, null, null, user.Id, DateTimeOffset.UtcNow);
        db.LeaveRequests.Add(leaveRequest);
        audit.Log(user, "create", "leave_request", leaveRequest.Id,
            $"Leave request {type.Code} {request.Days:0.##}{type.Units} {request.FromDate:yyyy-MM-dd}..{request.ToDate:yyyy-MM-dd} for {worker.Id} (balance {available:0.##} checked pre-submission)");
        db.SaveChanges();
        return (leaveRequest, null, null);
    }

    // 031 AC2: approval by an officer ≠ requester emits the dated absence event (Q-PM1).
    public (LeaveRequest? request, string? code, string? message) DecideLeaveRequest(CurrentUser approver, string requestId, Dtos.DecideLeaveRequestRequest request)
    {
        if (!approver.Has("leave:approve")) return (null, "FORBIDDEN", "leave:approve permission required.");
        var leaveRequest = FindRequest(requestId);
        if (leaveRequest is null) return (null, "LEAVE_REQUEST_NOT_FOUND", "Leave request not found.");
        if (leaveRequest.Status != LeaveRequestStatus.Pending)
            return (null, "LEAVE_REQUEST_STATE_ERROR", $"Request {requestId} is {leaveRequest.Status}; only Pending requests decide.");
        if (leaveRequest.CreatedBy == approver.Id)
            return (null, "LEAVE_APPROVAL_ERROR", "Maker-checker: the requester cannot decide their own leave (031 AC2).");

        if (!request.Approve)
        {
            SetRequestStatus(leaveRequest, LeaveRequestStatus.Rejected, approver.Id, request.DecisionReason);
            audit.Log(approver, "reject", "leave_request", requestId, $"Leave request {requestId} rejected — {request.DecisionReason}");
            SaveGuarded(requestId);
            return (leaveRequest, null, null);
        }

        SetRequestStatus(leaveRequest, LeaveRequestStatus.Approved, approver.Id, request.DecisionReason);
        var type = FindType(leaveRequest.LeaveTypeId)!;
        // Q-PM1: dated absence event on the 034 contract — payroll's approved-time pull sees it
        var absence = new TimeEntry(numbers.Next("TME"), ec.TenantId, ec.EntityId, leaveRequest.EmploymentId,
            leaveRequest.FromDate, 0m, 0m, TimeEntryStatus.Approved, "Leave", $"leave:{leaveRequest.Id}",
            $"absence {type.Code} {leaveRequest.Days:0.##}d (request {leaveRequest.Id})",
            approver.Id, DateTimeOffset.UtcNow);
        db.TimeEntries.Add(absence);
        AddEntry(leaveRequest.EmploymentId, leaveRequest.LeaveTypeId, DeferredUtil.PeriodOf(leaveRequest.FromDate),
            leaveRequest.Id, "Absence", -leaveRequest.Days, leaveRequest.FromDate, leaveRequest.Id, approver.Id);
        audit.Log(approver, "approve", "leave_request", requestId,
            $"Leave request {requestId} approved: absence event {absence.Id} dated {leaveRequest.FromDate:yyyy-MM-dd}, ledger {leaveRequest.Days:0.##}d");
        audit.Emit(approver, "people.leave.approved", new
        {
            requestId, absenceEntryId = absence.Id, employmentId = leaveRequest.EmploymentId,
            leaveTypeId = leaveRequest.LeaveTypeId, days = leaveRequest.Days, fromDate = leaveRequest.FromDate,
        });
        SaveGuarded(requestId);
        return (leaveRequest, null, null);
    }

    // withdrawal ONLY via this recorded workflow (031 AC2); an approved absence reverses through
    // a NEW signed ledger entry — history is never rewritten
    public (LeaveRequest? request, string? code, string? message) Withdraw(CurrentUser user, string requestId, string reason)
    {
        var leaveRequest = FindRequest(requestId);
        if (leaveRequest is null) return (null, "LEAVE_REQUEST_NOT_FOUND", "Leave request not found.");
        if (leaveRequest.Status is not (LeaveRequestStatus.Pending or LeaveRequestStatus.Approved))
            return (null, "LEAVE_REQUEST_STATE_ERROR", $"Request {requestId} is {leaveRequest.Status}; only Pending/Approved requests withdraw.");
        if (leaveRequest.CreatedBy != user.Id && !user.Has("leave:approve"))
            return (null, "FORBIDDEN", "Only the requester or a leave:approve officer can withdraw.");

        var wasApproved = leaveRequest.Status == LeaveRequestStatus.Approved;
        SetRequestStatus(leaveRequest, LeaveRequestStatus.Withdrawn, user.Id, reason);
        if (wasApproved)
            AddEntry(leaveRequest.EmploymentId, leaveRequest.LeaveTypeId, DeferredUtil.PeriodOf(leaveRequest.FromDate),
                $"withdrawal:{leaveRequest.Id}", "AbsenceReversal", leaveRequest.Days, DateOnly.FromDateTime(DateTime.UtcNow),
                leaveRequest.Id, user.Id);
        audit.Log(user, "withdraw", "leave_request", requestId,
            $"Leave request {requestId} withdrawn — {reason}" + (wasApproved ? " (absence reversed via ledger entry)" : ""));
        SaveGuarded(requestId);
        return (leaveRequest, null, null);
    }

    public object ListLeaveAdjustments(CurrentUser user, string? employmentId) =>
        db.LeaveAdjustments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId)
            .Where(a => employmentId == null || a.EmploymentId == employmentId)
            .OrderBy(a => a.Id).ToList().Select(ProjectAdjustment).ToList();

    // 032 AC2: elevated rights are checked by the controller (leave:adjust); independence is
    // enforced at the decision — the requester can never approve their own manual entry
    public (LeaveAdjustment? adjustment, string? code, string? message) CreateLeaveAdjustment(CurrentUser user, Dtos.CreateLeaveAdjustmentRequest request)
    {
        var type = FindType(request.LeaveTypeId);
        if (type is null) return (null, "LEAVE_TYPE_NOT_FOUND", "Leave type not found.");
        if (!db.Employments.Any(w => w.Id == request.EmploymentId
                && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId))
            return (null, "WORKER_NOT_FOUND", "Worker not found.");
        if (request.Qty == 0) return (null, "VALIDATION_ERROR", "Qty must be a non-zero signed amount.");
        var adjustment = new LeaveAdjustment(numbers.Next("LAG"), ec.TenantId, ec.EntityId,
            request.EmploymentId, type.Id, request.Qty, request.Reason, LeaveAdjustmentStatus.Pending,
            null, user.Id, null, null, user.Id, DateTimeOffset.UtcNow);
        db.LeaveAdjustments.Add(adjustment);
        audit.Log(user, "create", "leave_adjustment", adjustment.Id,
            $"Manual leave adjustment {request.Qty:0.##} for {request.EmploymentId} ({type.Code}) — elevated, awaiting independent approval");
        db.SaveChanges();
        return (adjustment, null, null);
    }

    public (LeaveAdjustment? adjustment, string? code, string? message) DecideLeaveAdjustment(CurrentUser approver, string adjustmentId, Dtos.DecideLeaveAdjustmentRequest request)
    {
        var adjustment = db.LeaveAdjustments.FirstOrDefault(a => a.Id == adjustmentId
            && a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);
        if (adjustment is null) return (null, "LEAVE_ADJUSTMENT_NOT_FOUND", "Leave adjustment not found.");
        if (adjustment.Status != LeaveAdjustmentStatus.Pending)
            return (null, "LEAVE_ADJUSTMENT_STATE_ERROR", $"Adjustment {adjustmentId} is {adjustment.Status}; only Pending entries decide.");
        if (adjustment.RequestedBy == approver.Id)
            return (null, "LEAVE_ADJUSTMENT_STATE_ERROR",
                "Maker-checker: the requesting officer cannot approve their own manual entry (032 AC2).");

        if (!request.Approve)
        {
            db.Entry(adjustment).Property(nameof(LeaveAdjustment.Status)).CurrentValue = LeaveAdjustmentStatus.Rejected;
            db.Entry(adjustment).Property(nameof(LeaveAdjustment.DecisionReason)).CurrentValue = request.DecisionReason;
            db.Entry(adjustment).Property(nameof(LeaveAdjustment.DecidedBy)).CurrentValue = approver.Id;
            db.Entry(adjustment).Property(nameof(LeaveAdjustment.DecidedAt)).CurrentValue = DateTimeOffset.UtcNow;
            SaveGuarded(adjustmentId);
            return (adjustment, null, null);
        }

        db.Entry(adjustment).Property(nameof(LeaveAdjustment.Status)).CurrentValue = LeaveAdjustmentStatus.Applied;
        db.Entry(adjustment).Property(nameof(LeaveAdjustment.DecisionReason)).CurrentValue = request.DecisionReason;
        db.Entry(adjustment).Property(nameof(LeaveAdjustment.DecidedBy)).CurrentValue = approver.Id;
        db.Entry(adjustment).Property(nameof(LeaveAdjustment.DecidedAt)).CurrentValue = DateTimeOffset.UtcNow;
        // the applied entry is a signed ledger row with a reversible reference to the adjustment
        AddEntry(adjustment.EmploymentId, adjustment.LeaveTypeId, DeferredUtil.PeriodOf(DateOnly.FromDateTime(DateTime.UtcNow)),
            $"adj:{adjustment.Id}", "Adjustment", adjustment.Qty, DateOnly.FromDateTime(DateTime.UtcNow),
            adjustment.Id, approver.Id);
        audit.Log(approver, "approve", "leave_adjustment", adjustmentId,
            $"Manual adjustment {adjustment.Qty:0.##} applied for {adjustment.EmploymentId} (ledger ref {adjustment.Id})");
        SaveGuarded(adjustmentId);
        return (adjustment, null, null);
    }

    public object ListAttendanceExceptions(CurrentUser user, string? employmentId) =>
        db.AttendanceExceptions.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId)
            .Where(e => employmentId == null || e.EmploymentId == employmentId)
            .OrderBy(e => e.WorkDate).ThenBy(e => e.Id).ToList().Select(ProjectException).ToList();

    // 033 AC1: dedupe against schedule+entry — the natural key {worker}:{date}:{kind} IS the
    // dedupe, so re-running the window converges on the existing rows and adds nothing.
    public (object? data, string? code, string? message) GenerateAttendanceExceptions(CurrentUser user, Dtos.GenerateAttendanceExceptionsRequest request)
    {
        var schedule = db.WorkSchedules.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Active)
            .OrderBy(s => s.Code).FirstOrDefault();
        if (schedule is null)
            return (null, "SCHEDULE_NOT_FOUND",
                "No active work schedule exists for this entity; attendance exceptions need a schedule to dedupe against (033).");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var workers = db.Employments.Where(w => w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId
                && w.Status == EmploymentStatus.Active).OrderBy(w => w.Id).ToList();
        var version = db.ScheduleVersions.Where(v => v.ScheduleId == schedule.Id).OrderBy(v => v.EffectiveFrom).ToList();

        var created = 0;
        foreach (var worker in workers)
        {
            for (var date = request.From; date <= request.To && date <= today; date = date.AddDays(1))
            {
                if (date < worker.HireDate) continue;
                var dayVersion = version.LastOrDefault(v => v.EffectiveFrom <= date);
                var plannedHours = dayVersion is null || date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    ? 0m
                    : decimal.Round(dayVersion.WeeklyPlannedHours / 5m, 2);
                var entry = db.TimeEntries.FirstOrDefault(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId
                    && t.EmploymentId == worker.Id && t.WorkDate == date && t.Status == TimeEntryStatus.Approved);
                var workedHours = entry is null ? 0m : entry.Hours + entry.OvertimeHours;

                string? kind = plannedHours > 0 && entry is null ? "Absent"
                    : plannedHours == 0 && entry is not null ? "UnscheduledWork" : null;
                if (kind is null) continue;
                var id = $"{worker.Id}:{date:yyyyMMdd}:{kind}";
                if (db.AttendanceExceptions.Any(e => e.Id == id)) continue;
                db.AttendanceExceptions.Add(new AttendanceException(id, ec.TenantId, ec.EntityId, worker.Id,
                    date, kind,
                    plannedHours > 0 ? $"scheduled {plannedHours:0.##}h, no entry" : $"entry {workedHours:0.##}h outside schedule",
                    null, null, null, null, user.Id, DateTimeOffset.UtcNow));
                created++;
            }
        }
        audit.Log(user, "generate", "attendance_exceptions", schedule.Id,
            $"Attendance exception generation {request.From:yyyy-MM-dd}..{request.To:yyyy-MM-dd}: {created} new (deduped against schedule+entry)");
        db.SaveChanges();
        return (new { from = request.From, to = request.To, created }, null, null);
    }

    // 033 AC2: the disposition rides SEPARATE columns — the source fact stays byte-identical
    public (AttendanceException? exception, string? code, string? message) DisposeAttendanceException(
        CurrentUser approver, string exceptionId, Dtos.DisposeAttendanceExceptionRequest request)
    {
        var attendanceException = db.AttendanceExceptions.FirstOrDefault(e => e.Id == exceptionId
            && e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);
        if (attendanceException is null) return (null, "ATTENDANCE_EXCEPTION_NOT_FOUND", "Attendance exception not found.");
        if (attendanceException.DispositionedAt is not null)
            return (null, "ATTENDANCE_EXCEPTION_STATE_ERROR", $"Exception {exceptionId} is already dispositioned.");
        db.Entry(attendanceException).Property(nameof(AttendanceException.Disposition)).CurrentValue = request.Disposition;
        db.Entry(attendanceException).Property(nameof(AttendanceException.DispositionReason)).CurrentValue = request.Reason;
        db.Entry(attendanceException).Property(nameof(AttendanceException.DispositionedBy)).CurrentValue = approver.Id;
        db.Entry(attendanceException).Property(nameof(AttendanceException.DispositionedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(approver, "dispose", "attendance_exception", exceptionId,
            $"Attendance exception {exceptionId} dispositioned '{request.Disposition}' — {request.Reason} (source fact untouched)");
        db.SaveChanges();
        return (attendanceException, null, null);
    }

    // ---- internals ------------------------------------------------------------------

    private LeaveType? FindType(string id) => db.LeaveTypes.FirstOrDefault(t => t.Id == id
        && t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId);

    private LeaveRequest? FindRequest(string id) => db.LeaveRequests.FirstOrDefault(r => r.Id == id
        && r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    private decimal BalanceFor(string workerId, string leaveTypeId, DateOnly asOf) =>
        db.LeaveLedgerEntries.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId
                && e.EmploymentId == workerId && e.LeaveTypeId == leaveTypeId && e.EntryDate <= asOf)
            .Sum(e => (decimal?)e.Qty) ?? 0m;

    private bool LedgerExists(string workerId, string leaveTypeId, string period, string sourceKey) =>
        db.LeaveLedgerEntries.Any(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId
            && e.EmploymentId == workerId && e.LeaveTypeId == leaveTypeId
            && e.Period == period && e.SourceKey == sourceKey);

    private LeaveLedgerEntry AddEntry(string workerId, string leaveTypeId, string period, string sourceKey,
        string kind, decimal qty, DateOnly entryDate, string? referenceId, string by)
    {
        var entry = new LeaveLedgerEntry(numbers.Next("LLE"), ec.TenantId, ec.EntityId, workerId, leaveTypeId,
            period, sourceKey, kind, qty, entryDate, referenceId, by, DateTimeOffset.UtcNow);
        db.LeaveLedgerEntries.Add(entry);
        return entry;
    }

    private static DateOnly PeriodEnd(string period)
    {
        var year = int.Parse(period[..4]);
        var month = int.Parse(period[5..7]);
        return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
    }

    private void SetRequestStatus(LeaveRequest leaveRequest, LeaveRequestStatus status, string by, string? reason)
    {
        db.Entry(leaveRequest).Property(nameof(LeaveRequest.Status)).CurrentValue = status;
        db.Entry(leaveRequest).Property(nameof(LeaveRequest.DecidedBy)).CurrentValue = by;
        db.Entry(leaveRequest).Property(nameof(LeaveRequest.DecidedAt)).CurrentValue = DateTimeOffset.UtcNow;
        if (reason is not null)
            db.Entry(leaveRequest).Property(nameof(LeaveRequest.DecisionReason)).CurrentValue = reason;
    }

    private void SaveGuarded(string id)
    {
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException($"Leave request {id} was decided concurrently; retry with a fresh read.");
        }
    }

    private static object ProjectType(LeaveType t) => new
    {
        t.Id, t.Code, t.Name, t.Units, accrualMethod = t.AccrualMethod, t.AccrualRatePerMonth,
        t.AnnualEntitlementDays, t.CarryoverLimitDays, t.RequiresApproval, t.Active,
        t.EffectiveFrom, t.EffectiveTo,
    };

    private static object ProjectEntry(LeaveLedgerEntry e) => new
    {
        e.Id, e.EmploymentId, e.LeaveTypeId, e.Period, e.SourceKey, kind = e.Kind, e.Qty,
        e.EntryDate, e.ReferenceId,
    };

    private static object ProjectRequest(LeaveRequest r) => new
    {
        r.Id, r.EmploymentId, r.LeaveTypeId, r.FromDate, r.ToDate, r.Days, status = r.Status.ToString(),
        r.Reason, r.DecisionReason, r.RequestedBy, r.DecidedBy, r.DecidedAt, r.CreatedBy,
    };

    private static object ProjectAdjustment(LeaveAdjustment a) => new
    {
        a.Id, a.EmploymentId, a.LeaveTypeId, a.Qty, a.Reason, status = a.Status.ToString(),
        a.DecisionReason, a.RequestedBy, a.DecidedBy, a.DecidedAt,
    };

    private static object ProjectException(AttendanceException e) => new
    {
        e.Id, e.EmploymentId, e.WorkDate, kind = e.Kind, e.SourceFact, e.Disposition,
        e.DispositionReason, e.DispositionedBy, e.DispositionedAt,
    };
}
