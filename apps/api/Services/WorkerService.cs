using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Worker register (ERP-PEOPLE-004/005/006/007/024/035, rulings R-PEO3/4). One-step register: the
// person-identity fields ride the employment row. Lifecycle Draft→Prehire→Active→{OnLeave,
// Suspended, Terminated} is an audited state machine (007); compensation is effective-dated and
// future-dated events never apply before their date (024); pay-group membership is one-active-
// per-worker (035 AC2). Terminations close future terms but never delete history.
public sealed class WorkerService(
    IPeopleRepository people, IAccountRepository accounts, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec) : IWorkerService
{
    // legal lifecycle map (007 AC1); Terminated is terminal — rescission would be a new event
    // (deferred, R-PEO10 spirit) and is not modeled in the pilot
    private static readonly Dictionary<EmploymentStatus, EmploymentStatus[]> Transitions = new()
    {
        [EmploymentStatus.Draft] = [EmploymentStatus.Prehire],
        [EmploymentStatus.Prehire] = [EmploymentStatus.Active],
        [EmploymentStatus.Active] = [EmploymentStatus.OnLeave, EmploymentStatus.Suspended, EmploymentStatus.Terminated],
        [EmploymentStatus.OnLeave] = [EmploymentStatus.Active, EmploymentStatus.Suspended, EmploymentStatus.Terminated],
        [EmploymentStatus.Suspended] = [EmploymentStatus.Active, EmploymentStatus.Terminated],
        [EmploymentStatus.Terminated] = [],
    };

    public object List(CurrentUser user, EmploymentStatus? status, string? search) =>
        people.AllWorkers(status, search).Select(Project).ToList();

    // 004: create with a rehire duplicate check — normalized NationalId/email exact match on an
    // existing worker returns 409 PERSON_DUPLICATE with the candidate id; never auto-merge
    public (Employment? worker, string? code, string? message) Create(CurrentUser user, Dtos.CreateWorkerRequest request)
    {
        var nationalId = Normalize(request.NationalId);
        var email = Normalize(request.Email);
        var existing = (nationalId is not null ? people.FindByNationalId(nationalId) : null)
            ?? (email is not null ? people.FindByEmail(email) : null);
        if (existing is not null)
            return (null, "PERSON_DUPLICATE",
                $"A worker with the same identity already exists: {existing.Id} ({existing.FullName}, WorkerNo {existing.WorkerNo}). " +
                "Rehire duplicates must be resolved manually — records are never merged.");

        var workerId = numbers.Next("WKR");
        var worker = new Employment(workerId, ec.TenantId, ec.EntityId,
            request.FullName.Trim(), request.PreferredName?.Trim(), nationalId, email, request.Phone?.Trim(),
            workerId, // the WKR doc number IS the worker number
            EmploymentStatus.Draft, request.CategoryCode.Trim(), request.ManagerUserId, request.LocationId,
            request.WeeklyPlannedHours, request.HireDate, null, null, user.Id, DateTimeOffset.UtcNow);
        people.AddWorker(worker);
        audit.Log(user, "create", "worker", worker.Id,
            $"Worker {worker.Id} '{worker.FullName}' registered ({request.CategoryCode}, hire {request.HireDate:yyyy-MM-dd}, draft)");
        people.Save();
        return (worker, null, null);
    }

    public (Employment? worker, string? code, string? message) Transition(CurrentUser user, string workerId, Dtos.TransitionEmploymentRequest request)
    {
        var worker = people.FindWorker(workerId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");
        if (!Transitions.TryGetValue(worker.Status, out var allowed) || !allowed.Contains(request.Status))
            return (null, "WORKER_TRANSITION_ERROR",
                $"Illegal transition {worker.Status} → {request.Status} (legal map: Draft→Prehire→Active→{{OnLeave, Suspended, Terminated}}).");
        if (request.Status == EmploymentStatus.Active && people.ApplicableCompensation(workerId, request.EffectiveDate) is null)
            return (null, "WORKER_NO_COMPENSATION",
                $"Worker {workerId} has no compensation event effective on {request.EffectiveDate:yyyy-MM-dd}; Active requires effective terms (007 AC1).");

        var isTermination = request.Status == EmploymentStatus.Terminated;
        people.SetWorkerStatus(worker, request.Status,
            isTermination ? request.EffectiveDate : null,
            isTermination ? request.Reason : null);

        // 007 AC2: termination closes FUTURE terms (never deletes) and TimeService rejects new time
        if (isTermination)
            foreach (var evt in people.CompensationsFor(workerId).Where(c => c.EffectiveFrom > request.EffectiveDate && c.EffectiveTo == null))
                people.CloseCompensation(evt, request.EffectiveDate);

        audit.Log(user, "transition", "worker", workerId,
            $"Worker {workerId} {worker.Status} → {request.Status} effective {request.EffectiveDate:yyyy-MM-dd}" +
            (request.Reason is null ? "" : $" — {request.Reason}"));
        audit.Emit(user, "people.worker.transitioned", new
        {
            workerId, from = worker.Status.ToString(), to = request.Status.ToString(),
            effectiveDate = request.EffectiveDate, reason = request.Reason,
        });
        people.Save();
        return (worker, null, null);
    }

    // 024: a new event closes the previously OPEN range at EffectiveFrom-1; an already-scheduled
    // future event (EffectiveFrom > the new start) blocks with COMPENSATION_OVERLAP — one pending
    // future change at a time in the pilot (R-PEO4-style simplification)
    public (CompensationEvent? evt, string? code, string? message) AddCompensation(CurrentUser user, string workerId, Dtos.AddCompensationRequest request)
    {
        var worker = people.FindWorker(workerId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");
        var currency = EntityContext.SeedEntity.Currency;
        if (request.Currency != currency)
            return (null, "VALIDATION_ERROR", $"Compensation currency must be {currency} in this pilot (R-PEO7 single-currency).");

        var scheduled = people.CompensationsFor(workerId)
            .FirstOrDefault(c => c.EffectiveFrom > request.EffectiveFrom && c.EffectiveTo == null);
        if (scheduled is not null)
            return (null, "COMPENSATION_OVERLAP",
                $"A compensation event effective {scheduled.EffectiveFrom:yyyy-MM-dd} is already scheduled for {workerId}; " +
                "a new range cannot overlap an active one (024).");

        foreach (var open in people.CompensationsFor(workerId)
                     .Where(c => c.EffectiveTo == null && c.EffectiveFrom < request.EffectiveFrom))
            people.CloseCompensation(open, request.EffectiveFrom.AddDays(-1));

        var evt = new CompensationEvent(numbers.Next("CMP"), ec.TenantId, ec.EntityId, workerId,
            request.Amount, request.Currency, request.PayBasis, request.EffectiveFrom, null,
            request.Reason, user.Id, user.Id, DateTimeOffset.UtcNow);
        people.AddCompensation(evt);
        audit.Log(user, "compensation", "worker", workerId,
            $"Compensation {evt.Id} for {workerId}: {request.Amount:0.00} {request.Currency} {request.PayBasis} " +
            $"from {request.EffectiveFrom:yyyy-MM-dd} — {request.Reason}");
        people.Save();
        return (evt, null, null);
    }

    // 024 AC1: as-of query returns the applicable terms (asOf defaults to today)
    public object Compensations(CurrentUser user, string workerId, DateOnly? asOf)
    {
        var events = people.CompensationsFor(workerId);
        var at = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var applicable = people.ApplicableCompensation(workerId, at);
        return new
        {
            workerId,
            asOf = new { date = at, amount = applicable?.Amount, payBasis = applicable?.PayBasis, currency = applicable?.Currency },
            events = events.Select(e => new
            {
                e.Id, e.Amount, e.Currency, e.PayBasis, e.EffectiveFrom, e.EffectiveTo, e.Reason, e.ApprovedBy,
            }),
        };
    }

    // 035 AC2: a worker holds at most one ACTIVE membership per overlapping period (split-pay
    // deferred) — the overlapping-range check is the AC, the unique key is the backstop
    public (WorkerPayGroup? membership, string? code, string? message) AddMembership(CurrentUser user, string workerId, Dtos.AddPayGroupMembershipRequest request)
    {
        var worker = people.FindWorker(workerId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");
        var group = people.FindPayGroup(request.PayGroupId);
        if (group is null) return (null, "PAY_GROUP_NOT_FOUND", $"Pay group {request.PayGroupId} not found.");
        if (!group.Active)
            return (null, "PAY_GROUP_INACTIVE", $"Pay group {group.Code} is inactive.");

        var overlapping = people.MembershipsFor(workerId).FirstOrDefault(m =>
            (m.EffectiveTo == null || m.EffectiveTo >= request.EffectiveFrom) &&
            (request.EffectiveTo == null || m.EffectiveFrom <= request.EffectiveTo));
        if (overlapping is not null)
            return (null, "PAY_GROUP_OVERLAP",
                $"Worker {workerId} is already an active member of pay group {overlapping.PayGroupId} " +
                $"from {overlapping.EffectiveFrom:yyyy-MM-dd}; a worker joins at most one active pay group (035 AC2, split-pay deferred).");

        var membership = new WorkerPayGroup($"{workerId}:{request.PayGroupId}:{request.EffectiveFrom:yyyyMMdd}",
            ec.TenantId, ec.EntityId, workerId, request.PayGroupId, request.EffectiveFrom, request.EffectiveTo);
        people.AddMembership(membership);
        audit.Log(user, "assign", "worker_pay_group", membership.Id,
            $"Worker {workerId} assigned to pay group {group.Code} from {request.EffectiveFrom:yyyy-MM-dd}");
        people.Save();
        return (membership, null, null);
    }

    public (PayGroup? group, string? code, string? message) CreatePayGroup(CurrentUser user, Dtos.CreatePayGroupRequest request)
    {
        if (people.FindPayGroup(request.Code) is not null)
            return (null, "PAY_GROUP_EXISTS", $"Pay group {request.Code} already exists.");
        var group = new PayGroup(request.Code, ec.TenantId, ec.EntityId, request.Code, request.Name,
            request.Frequency, true);
        people.AddPayGroup(group);
        audit.Log(user, "create", "pay_group", group.Id, $"Pay group {group.Code} ({request.Frequency}) created");
        people.Save();
        return (group, null, null);
    }

    public object ListPayGroups(CurrentUser user) => people.AllPayGroups().Select(g => new
    {
        g.Id, g.Code, g.Name, g.Frequency, g.Active,
        members = people.WorkersInGroup(g.Id).Select(w => w.Id).ToList(),
    }).ToList();

    // 056: approved (posted) results reproduced as issued; reversal runs listed separately
    public (object? data, string? code, string? message) PayHistory(CurrentUser user, string workerId)
    {
        var worker = people.FindWorker(workerId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");

        var runs = people.AllPayRuns(null, null)
            .Where(r => r.Status is PayRunStatus.Posted or PayRunStatus.Reversed)
            .OrderBy(r => r.PayDate).ThenBy(r => r.Id);
        var rows = new List<object>();
        foreach (var run in runs)
        {
            var lines = run.Lines.Where(l => l.EmploymentId == workerId).ToList();
            if (lines.Count == 0) continue;
            var gross = lines.Where(l => l.Kind == PayCodeKind.Earning).Sum(l => l.Amount);
            var ded = lines.Where(l => l.Kind == PayCodeKind.Deduction).Sum(l => l.Amount);
            rows.Add(new
            {
                runId = run.Id, periodId = run.PeriodId, payDate = run.PayDate, currency = run.Currency,
                status = run.Status.ToString(),
                kind = run.ReversalOfRunId is null ? "original" : "reversal",
                reversalOfRunId = run.ReversalOfRunId, gross, deductions = ded, net = gross - ded,
                run.ReversalReason, run.FundsReleased,
                lines,
            });
        }
        return (rows, null, null);
    }

    // 004 edge: identity matching normalizes NationalId/email to Trim + lowercase
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static object Project(Employment w) => new
    {
        w.Id, w.FullName, w.PreferredName, w.NationalId, w.Email, w.Phone, w.WorkerNo,
        w.Status, w.CategoryCode, w.ManagerUserId, w.LocationId, w.WeeklyPlannedHours,
        w.HireDate, w.TerminationDate, w.TerminationReason, w.CreatedBy, w.CreatedAt,
    };
}
