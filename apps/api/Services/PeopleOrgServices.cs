using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Shared helpers for the deferred-screens services (PM1–PM10). Deliberately tiny: interval
// coverage, SHA-256 hex, and UTC ISO instants.
public static class DeferredUtil
{
    public static bool Covers(DateOnly? from, DateOnly? to, DateOnly date) =>
        (from ?? DateOnly.MinValue) <= date && date <= (to ?? DateOnly.MaxValue);

    public static bool Overlaps(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo) =>
        aFrom <= (bTo ?? DateOnly.MaxValue) && bFrom <= (aTo ?? DateOnly.MaxValue);

    public static string Sha256Hex(string content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));

    public static string UtcIso(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    public static string PeriodOf(DateOnly date) => $"{date:yyyy-MM}";

    public static void PinOwned(ErpDbContext db, EntityContext ec, IEnumerable<object> rows)
    {
        foreach (var row in rows)
        {
            db.Entry(row).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(row).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }
}

// PM1 — org hierarchy, locations, positions, assignments (ERP-PEOPLE-002/003/005/008).
// As-of queries return interval-valid rows; overlapping intervals on one code are refused;
// over-limit / out-of-employment / missing-credential assignments route to the explicit
// PendingException state and activate only through the R-FIN exception decision (008 AC2).
public sealed class PeopleOrgService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit,
    IIdentityRepository identities) : IPeopleOrgService
{
    public object ListOrgUnits(CurrentUser user, DateOnly? asOf)
    {
        var at = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return db.OrgUnits.Where(u => u.TenantId == ec.TenantId && u.LegalEntityId == ec.EntityId)
            .OrderBy(u => u.Code).ThenBy(u => u.EffectiveFrom).ToList()
            .Where(u => DeferredUtil.Covers(u.EffectiveFrom, u.EffectiveTo, at))
            .Select(u => new { u.Id, u.Code, u.Name, u.ParentOrgUnitId, u.LocationId, u.EffectiveFrom, u.EffectiveTo })
            .ToList();
    }

    public (OrgUnit? unit, string? code, string? message) CreateOrgUnit(CurrentUser user, Dtos.CreateOrgUnitRequest request) =>
        CreateInterval(user, request.Code, request.Name, request.ParentOrgUnitId, request.LocationId,
            request.EffectiveFrom, request.EffectiveTo);

    public (OrgUnit? unit, string? code, string? message) CreateOrgUnitVersion(CurrentUser user, string unitId, Dtos.CreateOrgUnitVersionRequest request)
    {
        var prior = db.OrgUnits.FirstOrDefault(u => u.Id == unitId
            && u.TenantId == ec.TenantId && u.LegalEntityId == ec.EntityId);
        return prior is null
            ? (null, "ORG_UNIT_NOT_FOUND", "Org unit not found.")
            : CreateInterval(user, prior.Code, request.Name, request.ParentOrgUnitId, request.LocationId,
                request.EffectiveFrom, request.EffectiveTo);
    }

    private (OrgUnit? unit, string? code, string? message) CreateInterval(
        CurrentUser user, string code, string name, string? parentId, string? locationId,
        DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (parentId is { Length: > 0 })
        {
            var parentExists = db.OrgUnits.Any(u => u.TenantId == ec.TenantId && u.LegalEntityId == ec.EntityId
                && u.Code == parentId && u.EffectiveFrom <= effectiveFrom
                && (u.EffectiveTo == null || u.EffectiveTo >= effectiveFrom));
            if (!parentExists) return (null, "ORG_UNIT_NOT_FOUND", $"Parent org unit {parentId} does not cover {effectiveFrom:yyyy-MM-dd}.");
        }
        if (locationId is { Length: > 0 }
            && !db.WorkLocations.Any(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId && l.Id == locationId))
            return (null, "ORG_LOCATION_NOT_FOUND", $"Work location {locationId} not found.");

        // 002 AC1: exclusive parent, NO overlapping intervals on one unit code
        var overlapping = db.OrgUnits.Where(u => u.TenantId == ec.TenantId && u.LegalEntityId == ec.EntityId
                && u.Code == code).ToList()
            .FirstOrDefault(u => DeferredUtil.Overlaps(effectiveFrom, effectiveTo, u.EffectiveFrom, u.EffectiveTo));
        if (overlapping is not null)
            return (null, "ORG_INTERVAL_OVERLAP",
                $"Org unit {code} already has an interval {overlapping.EffectiveFrom:yyyy-MM-dd}..{overlapping.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"}; intervals may not overlap (002 AC1).");

        var unit = new OrgUnit(numbers.Next("ORG"), ec.TenantId, ec.EntityId, code, name, parentId,
            locationId, effectiveFrom, effectiveTo, user.Id, DateTimeOffset.UtcNow);
        db.OrgUnits.Add(unit);
        audit.Log(user, "create", "org_unit", unit.Id, $"Org unit {code} '{name}' effective {effectiveFrom:yyyy-MM-dd}");
        db.SaveChanges();
        return (unit, null, null);
    }

    public object ListWorkLocations(CurrentUser user) =>
        db.WorkLocations.Where(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId)
            .OrderBy(l => l.Code).ToList();

    public (WorkLocation? location, string? code, string? message) CreateWorkLocation(CurrentUser user, Dtos.CreateWorkLocationRequest request)
    {
        if (!IsValidTimezone(request.Timezone))
            return (null, "VALIDATION_ERROR", $"Timezone '{request.Timezone}' is not a known IANA/Windows zone (025 AC2 needs named zones).");
        if (db.WorkLocations.Any(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId && l.Code == request.Code))
            return (null, "WORK_LOCATION_EXISTS", $"Work location {request.Code} already exists.");
        var location = new WorkLocation(numbers.Next("ORG"), ec.TenantId, ec.EntityId, request.Code,
            request.Name, request.Timezone, request.Address, true);
        db.WorkLocations.Add(location);
        audit.Log(user, "create", "work_location", location.Id, $"Work location {request.Code} ({request.Timezone}) created");
        db.SaveChanges();
        return (location, null, null);
    }

    private static bool IsValidTimezone(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    public object ListPositions(CurrentUser user)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var assignments = db.PositionAssignments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId).ToList();
        return db.Positions.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId)
            .OrderBy(p => p.Code).ToList()
            .Select(p => new
            {
                p.Id, p.Code, p.Title, p.OrgUnitId, p.GradeCode, p.RequiredCredentialCode,
                p.BudgetedFte, p.BudgetedHeadcount, status = p.Status.ToString(),
                p.SourcePlanId, p.SourcePlanLineCode,
                assignedFte = assignments.Where(a => a.PositionId == p.Id && a.Status == AssignmentStatus.Active
                    && DeferredUtil.Covers(a.EffectiveFrom, a.EffectiveTo, today)).Sum(a => a.Fte),
                incumbents = assignments.Count(a => a.PositionId == p.Id && a.Status == AssignmentStatus.Active
                    && DeferredUtil.Covers(a.EffectiveFrom, a.EffectiveTo, today)),
            }).ToList();
    }

    public (Position? position, string? code, string? message) CreatePosition(CurrentUser user, Dtos.CreatePositionRequest request)
    {
        if (db.Positions.Any(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId && p.Code == request.Code))
            return (null, "POSITION_EXISTS", $"Position {request.Code} already exists.");
        if (request.BudgetedFte < 0 || request.BudgetedHeadcount < 0)
            return (null, "VALIDATION_ERROR", "Budgeted FTE/headcount must be non-negative.");
        var position = new Position(numbers.Next("POS"), ec.TenantId, ec.EntityId, request.Code, request.Title,
            request.OrgUnitId, request.GradeCode, request.RequiredCredentialCode,
            request.BudgetedFte, request.BudgetedHeadcount, PositionStatus.Open, null, null,
            user.Id, DateTimeOffset.UtcNow);
        db.Positions.Add(position);
        audit.Log(user, "create", "position", position.Id,
            $"Position {request.Code} '{request.Title}' authorized for {request.BudgetedFte} FTE / {request.BudgetedHeadcount} headcount");
        db.SaveChanges();
        return (position, null, null);
    }

    public object ListAssignments(CurrentUser user, string? employmentId) =>
        db.PositionAssignments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId)
            .Where(a => employmentId == null || a.EmploymentId == employmentId)
            .OrderBy(a => a.PositionId).ThenBy(a => a.EffectiveFrom).ToList()
            .Select(ProjectAssignment).ToList();

    // 005/008/058: the gate order is deliberate — worker-state first (an out-of-employment
    // assignee is never silently staffed), then credential, then headcount/FTE budget. Any hit
    // WITHOUT a stated exception reason is a 409; WITH one the row lands PendingException.
    public (PositionAssignment? assignment, string? code, string? message) CreateAssignment(
        CurrentUser user, string positionId, Dtos.CreatePositionAssignmentRequest request)
    {
        var position = db.Positions.FirstOrDefault(p => p.Id == positionId
            && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        if (position is null) return (null, "POSITION_NOT_FOUND", "Position not found.");
        if (position.Status != PositionStatus.Open)
            return (null, "POSITION_CLOSED", $"Position {position.Code} is closed; reopen before staffing.");
        var worker = db.Employments.FirstOrDefault(w => w.Id == request.EmploymentId
            && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");

        var gateFailures = new List<string>();
        if (worker.Status != EmploymentStatus.Active)
            gateFailures.Add($"worker {worker.Id} is {worker.Status}, not Active (005 AC2)");

        if (position.RequiredCredentialCode is { Length: > 0 } required)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var holds = db.Credentials.Any(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId
                && c.EmploymentId == worker.Id && c.Code == required
                && (c.Status == CredentialStatus.Current || c.Status == CredentialStatus.Unverified)
                && (c.ExpiresOn == null || c.ExpiresOn >= today));
            if (!holds)
                gateFailures.Add($"required credential '{required}' is missing or expired (058 AC2)");
        }

        var at = request.EffectiveFrom;
        var concurrent = db.PositionAssignments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId
                && a.PositionId == position.Id
                && (a.Status == AssignmentStatus.Active || a.Status == AssignmentStatus.PendingException)
                && a.EffectiveFrom <= at && (a.EffectiveTo == null || a.EffectiveTo >= at)).ToList();
        var fteAfter = concurrent.Sum(a => a.Fte) + request.Fte;
        if (fteAfter > position.BudgetedFte)
            gateFailures.Add($"FTE budget exceeded: {concurrent.Sum(a => a.Fte):0.##} + {request.Fte:0.##} > {position.BudgetedFte:0.##} (008 AC2)");
        if (concurrent.Count + 1 > position.BudgetedHeadcount)
            gateFailures.Add($"headcount budget exceeded: {concurrent.Count} + 1 > {position.BudgetedHeadcount} (008 AC2)");

        var wantsException = request.ExceptionReason is { Length: > 0 };
        if (gateFailures.Count > 0 && !wantsException)
            return (null, GateCode(gateFailures), $"{gateFailures[0]} — route through an approved exception to proceed.");

        var variance = gateFailures.Count > 0 ? Variance(concurrent, request.Fte, position) : 0m;
        var assignment = new PositionAssignment(numbers.Next("POS"), ec.TenantId, ec.EntityId, position.Id,
            worker.Id, request.Fte, request.CostCenter, request.ManagerUserId, request.LocationId,
            request.EffectiveFrom, request.EffectiveTo,
            gateFailures.Count > 0 ? AssignmentStatus.PendingException : AssignmentStatus.Active,
            ExceptionApprovedBy: null, ApprovedVariance: null, ExceptionReason: request.ExceptionReason,
            user.Id, DateTimeOffset.UtcNow);
        db.PositionAssignments.Add(assignment);
        audit.Log(user, "assign", "position_assignment", assignment.Id,
            gateFailures.Count > 0
                ? $"Assignment {worker.Id} → {position.Code} PENDING EXCEPTION: {string.Join("; ", gateFailures)}; variance {variance:0.##}"
                : $"Assignment {worker.Id} → {position.Code} active from {request.EffectiveFrom:yyyy-MM-dd}");
        db.SaveChanges();
        return (assignment, null, null);
    }

    // the exception decision is R-FIN's act (brief §3: exception approvals = R-FIN), never the maker's
    public (PositionAssignment? assignment, string? code, string? message) DecideAssignmentException(
        CurrentUser approver, string assignmentId, Dtos.DecideAssignmentExceptionRequest request)
    {
        if (!approver.Has("payroll:approve")) return (null, "FORBIDDEN", "payroll:approve permission required.");
        var assignment = db.PositionAssignments.FirstOrDefault(a => a.Id == assignmentId
            && a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);
        if (assignment is null) return (null, "ASSIGNMENT_NOT_FOUND", "Position assignment not found.");
        if (assignment.Status != AssignmentStatus.PendingException)
            return (null, "ASSIGNMENT_STATE_ERROR", $"Assignment {assignmentId} is {assignment.Status}; only pending exceptions are decidable.");
        if (assignment.CreatedBy == approver.Id)
            return (null, "ASSIGNMENT_STATE_ERROR", "Maker-checker: the assignment maker cannot decide their own exception.");

        var position = db.Positions.First(p => p.Id == assignment.PositionId);
        var concurrent = db.PositionAssignments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId
                && a.PositionId == assignment.PositionId && a.Id != assignment.Id
                && (a.Status == AssignmentStatus.Active || a.Status == AssignmentStatus.PendingException)
                && a.EffectiveFrom <= assignment.EffectiveFrom
                && (a.EffectiveTo == null || a.EffectiveTo >= assignment.EffectiveFrom)).ToList();
        var variance = Variance(concurrent, assignment.Fte, position);

        SetAssignmentStatus(assignment, request.Approve ? AssignmentStatus.Active : AssignmentStatus.Ended,
            approver.Id, variance);
        audit.Log(approver, "exception_decision", "position_assignment", assignment.Id,
            $"{(request.Approve ? "Approved" : "Rejected")} exception for {assignment.EmploymentId} → {position.Code}: variance {variance:0.##} — {request.Reason}");
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, "ASSIGNMENT_STATE_ERROR",
                $"Assignment {assignmentId} was decided concurrently; the retry carried a stale snapshot and wrote nothing.");
        }
        return (assignment, null, null);
    }

    private static string GateCode(List<string> failures)
    {
        if (failures.Any(f => f.Contains("credential"))) return "CREDENTIAL_REQUIRED";
        if (failures.Any(f => f.Contains("worker"))) return "WORKER_NOT_ACTIVE";
        return "HEADCOUNT_EXCEEDED";
    }

    // the logged variance: headcount overshoot when the count is over, else the FTE deficit
    private static decimal Variance(List<PositionAssignment> concurrent, decimal fte, Position position)
    {
        var headcountOver = concurrent.Count + 1 - position.BudgetedHeadcount;
        if (headcountOver > 0) return headcountOver;
        return Math.Max(concurrent.Sum(a => a.Fte) + fte - position.BudgetedFte, 0m);
    }

    private void SetAssignmentStatus(PositionAssignment assignment, AssignmentStatus status,
        string approvedBy, decimal variance)
    {
        db.Entry(assignment).Property(nameof(PositionAssignment.Status)).CurrentValue = status;
        db.Entry(assignment).Property(nameof(PositionAssignment.ExceptionApprovedBy)).CurrentValue = approvedBy;
        db.Entry(assignment).Property(nameof(PositionAssignment.ApprovedVariance)).CurrentValue = variance;
    }

    private static object ProjectAssignment(PositionAssignment a) => new
    {
        a.Id, a.PositionId, a.EmploymentId, a.Fte, a.CostCenter, a.ManagerUserId, a.LocationId,
        a.EffectiveFrom, a.EffectiveTo, status = a.Status.ToString(), a.ExceptionApprovedBy,
        a.ApprovedVariance, a.ExceptionReason, a.CreatedBy, a.CreatedAt,
    };
}

// PM2 — onboarding task orchestration (ERP-PEOPLE-011). Tasks derive from category templates;
// generation is idempotent per (worker, taskKey); every waiver names its approver + reason and
// the generator can never waive their own task (011 AC2 maker-checker).
public sealed class OnboardingService(
    ErpDbContext db, EntityContext ec, IAuditRepository audit) : IOnboardingService
{
    public object ListTemplates(CurrentUser user) =>
        db.OnboardingTemplates.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId)
            .OrderBy(t => t.CategoryCode).ThenBy(t => t.TaskKey).ToList();

    public (OnboardingTemplate? template, string? code, string? message) CreateTemplate(CurrentUser user, Dtos.CreateOnboardingTemplateRequest request)
    {
        var id = $"{request.CategoryCode}:{request.TaskKey}";
        if (db.OnboardingTemplates.Any(t => t.Id == id
                && t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId))
            return (null, "ONBOARDING_TEMPLATE_EXISTS", $"Template {id} already exists.");
        if (request.DueOffsetDays < 0)
            return (null, "VALIDATION_ERROR", "DueOffsetDays must be non-negative.");
        var template = new OnboardingTemplate(id, ec.TenantId, ec.EntityId, request.CategoryCode,
            request.TaskKey, request.Name, request.DueOffsetDays, request.OwnerRole, request.Required,
            true, user.Id, DateTimeOffset.UtcNow);
        db.OnboardingTemplates.Add(template);
        audit.Log(user, "create", "onboarding_template", id, $"Onboarding template {id} ({request.DueOffsetDays}d, {request.OwnerRole})");
        db.SaveChanges();
        return (template, null, null);
    }

    public (object? data, string? code, string? message) GenerateForWorker(CurrentUser user, string workerId)
    {
        var worker = db.Employments.FirstOrDefault(w => w.Id == workerId
            && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");

        var templates = db.OnboardingTemplates.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId
                && t.CategoryCode == worker.CategoryCode && t.Active).OrderBy(t => t.TaskKey).ToList();
        var existingKeys = db.OnboardingTasks.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId
                && t.EmploymentId == workerId).Select(t => t.TaskKey).ToHashSet();
        var created = new List<OnboardingTask>();
        foreach (var template in templates.Where(t => !existingKeys.Contains(t.TaskKey)))
        {
            var task = new OnboardingTask($"{workerId}:{template.TaskKey}", ec.TenantId, ec.EntityId,
                workerId, template.Id, template.TaskKey, template.Name,
                worker.HireDate.AddDays(template.DueOffsetDays), template.OwnerRole, template.Required,
                OnboardingTaskStatus.Pending, null, null, null, user.Id, DateTimeOffset.UtcNow);
            db.OnboardingTasks.Add(task);
            created.Add(task);
        }
        audit.Log(user, "generate", "onboarding_tasks", workerId,
            $"Onboarding generation for {workerId}: {created.Count} task(s) from {templates.Count} template(s)");
        db.SaveChanges();
        return (new { created = created.Count, tasks = created.Select(Project) }, null, null);
    }

    public object TasksFor(CurrentUser user, string workerId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return db.OnboardingTasks.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId
                && t.EmploymentId == workerId).OrderBy(t => t.DueDate).ToList()
            .Select(t => new
            {
                task = Project(t),
                overdue = t.Status == OnboardingTaskStatus.Pending && t.DueDate < today,
            }).ToList();
    }

    public (OnboardingTask? task, string? code, string? message) Complete(CurrentUser user, string taskId)
    {
        var task = FindTask(taskId);
        if (task is null) return (null, "ONBOARDING_TASK_NOT_FOUND", "Onboarding task not found.");
        if (task.Status != OnboardingTaskStatus.Pending)
            return (null, "ONBOARDING_TASK_STATE_ERROR", $"Task {taskId} is {task.Status}; only Pending tasks complete.");
        db.Entry(task).Property(nameof(OnboardingTask.Status)).CurrentValue = OnboardingTaskStatus.Completed;
        db.Entry(task).Property(nameof(OnboardingTask.DecidedBy)).CurrentValue = user.Id;
        db.Entry(task).Property(nameof(OnboardingTask.DecidedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(user, "complete", "onboarding_task", taskId, $"Onboarding task {task.TaskKey} completed for {task.EmploymentId}");
        SaveGuarded(taskId);
        return (task, null, null);
    }

    public (OnboardingTask? task, string? code, string? message) Waive(CurrentUser approver, string taskId, string reason)
    {
        var task = FindTask(taskId);
        if (task is null) return (null, "ONBOARDING_TASK_NOT_FOUND", "Onboarding task not found.");
        if (task.Status != OnboardingTaskStatus.Pending)
            return (null, "ONBOARDING_TASK_STATE_ERROR", $"Task {taskId} is {task.Status}; only Pending tasks waive.");
        if (task.CreatedBy == approver.Id)
            return (null, "ONBOARDING_WAIVER_ERROR",
                "Maker-checker: the task generator cannot waive their own task — a second officer must approve the waiver (011 AC2).");
        db.Entry(task).Property(nameof(OnboardingTask.Status)).CurrentValue = OnboardingTaskStatus.Waived;
        db.Entry(task).Property(nameof(OnboardingTask.DecisionReason)).CurrentValue = reason;
        db.Entry(task).Property(nameof(OnboardingTask.DecidedBy)).CurrentValue = approver.Id;
        db.Entry(task).Property(nameof(OnboardingTask.DecidedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(approver, "waive", "onboarding_task", taskId,
            $"Onboarding task {task.TaskKey} WAIVED for {task.EmploymentId} by {approver.Id} — {reason}");
        SaveGuarded(taskId);
        return (task, null, null);
    }

    private OnboardingTask? FindTask(string taskId) =>
        db.OnboardingTasks.FirstOrDefault(t => t.Id == taskId
            && t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId);

    private void SaveGuarded(string taskId)
    {
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException($"Onboarding task {taskId} was decided concurrently; retry with a fresh read.");
        }
    }

    private static object Project(OnboardingTask t) => new
    {
        t.Id, t.EmploymentId, t.TemplateId, t.TaskKey, t.Name, t.DueDate, t.OwnerRole, t.Required,
        status = t.Status.ToString(), t.DecisionReason, t.DecidedBy, t.DecidedAt, t.CreatedBy,
    };
}

// PM3 — job catalog & compensation structure (ERP-PEOPLE-022/023). Versions are new rows so a
// catalog change NEVER rewrites the version a historic assignment references (022 AC1); grade
// ranges validate min ≤ mid ≤ max and gate comp changes with the explicit R-FIN exception.
public sealed class JobCatalogService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : IJobCatalogService
{
    public object ListProfiles(CurrentUser user) =>
        db.JobProfiles.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId)
            .OrderBy(p => p.Code).ToList()
            .Select(p => new
            {
                p.Id, p.Code, p.Name, p.Department, p.Active,
                versions = db.JobProfileVersions.Where(v => v.ProfileId == p.Id).OrderBy(v => v.Version).ToList()
                    .Select(v => new
                    {
                        v.Id, v.Version, v.Title, v.EffectiveFrom, v.EffectiveTo,
                        competencies = db.Competencies.Where(c => c.ProfileVersionId == v.Id).ToList()
                            .Select(c => new { c.Name, c.Level, c.Required }),
                    }),
            }).ToList();

    public (JobProfile? profile, string? code, string? message) CreateProfile(CurrentUser user, Dtos.CreateJobProfileRequest request)
    {
        if (db.JobProfiles.Any(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId && p.Code == request.Code))
            return (null, "JOB_PROFILE_EXISTS", $"Job profile {request.Code} already exists.");
        var profile = new JobProfile(numbers.Next("JOB"), ec.TenantId, ec.EntityId, request.Code,
            request.Name, request.Department, true, user.Id, DateTimeOffset.UtcNow);
        db.JobProfiles.Add(profile);
        var (version, error) = AddVersion(profile, 1, request.Title, request.EffectiveFrom, null,
            request.Competencies, user.Id);
        if (error is not null) return (null, error, "Profile version failed.");
        audit.Log(user, "create", "job_profile", profile.Code, $"Job profile {request.Code} v1 '{request.Title}' created");
        db.SaveChanges();
        return (profile, null, null);
    }

    public (JobProfileVersion? version, string? code, string? message) CreateProfileVersion(CurrentUser user, string profileId, Dtos.CreateJobProfileVersionRequest request)
    {
        var profile = db.JobProfiles.FirstOrDefault(p => p.Id == profileId
            && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        if (profile is null) return (null, "JOB_PROFILE_NOT_FOUND", "Job profile not found.");
        var number = (db.JobProfileVersions.Where(v => v.ProfileId == profileId).Max(v => (int?)v.Version) ?? 0) + 1;
        var (version, error) = AddVersion(profile, number, request.Title, request.EffectiveFrom,
            request.EffectiveTo, request.Competencies, user.Id);
        if (error is not null) return (null, error, "Version failed.");
        audit.Log(user, "version", "job_profile", profile.Code, $"Job profile {profile.Code} v{request.Title} versioned — historic references keep their rows (022 AC1)");
        db.SaveChanges();
        return (version, null, null);
    }

    private (JobProfileVersion? version, string? code) AddVersion(JobProfile profile, int number, string title,
        DateOnly effectiveFrom, DateOnly? effectiveTo, IReadOnlyList<Dtos.CompetencyItem>? competencies, string userId)
    {
        var version = new JobProfileVersion($"{profile.Id}:v{number}", ec.TenantId, ec.EntityId,
            profile.Id, number, title, effectiveFrom, effectiveTo, userId, DateTimeOffset.UtcNow);
        db.JobProfileVersions.Add(version);
        foreach (var competency in competencies ?? [])
            db.Competencies.Add(new Competency($"{version.Id}:{competency.Name}", ec.TenantId, ec.EntityId,
                version.Id, competency.Name, competency.Level, competency.Required));
        return (version, null);
    }

    public object ListGradeRanges(CurrentUser user) =>
        db.GradeRanges.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId)
            .OrderBy(r => r.GradeCode).ThenBy(r => r.EffectiveFrom).ToList()
            .Select(ProjectRange).ToList();

    public (GradeRange? range, string? code, string? message) CreateGradeRange(CurrentUser user, Dtos.CreateGradeRangeRequest request)
    {
        if (request.MinAmount > request.MidAmount || request.MidAmount > request.MaxAmount)
            return (null, "VALIDATION_ERROR", "Grade range must satisfy min ≤ mid ≤ max (023 AC1).");
        var overlapping = db.GradeRanges.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId
                && r.GradeCode == request.GradeCode && r.Active).ToList()
            .FirstOrDefault(r => DeferredUtil.Overlaps(request.EffectiveFrom, request.EffectiveTo, r.EffectiveFrom, r.EffectiveTo));
        if (overlapping is not null)
            return (null, "GRADE_RANGE_OVERLAP",
                $"Grade {request.GradeCode} already has an active range from {overlapping.EffectiveFrom:yyyy-MM-dd}; intervals may not overlap.");
        var range = new GradeRange(numbers.Next("GRD"), ec.TenantId, ec.EntityId, request.GradeCode,
            request.Currency, request.Frequency, request.MinAmount, request.MidAmount, request.MaxAmount,
            request.EffectiveFrom, request.EffectiveTo, 1, true, user.Id, DateTimeOffset.UtcNow);
        db.GradeRanges.Add(range);
        audit.Log(user, "create", "grade_range", range.Id,
            $"Grade {request.GradeCode} range {request.MinAmount:0.##}/{request.MidAmount:0.##}/{request.MaxAmount:0.##} {request.Currency} from {request.EffectiveFrom:yyyy-MM-dd}");
        db.SaveChanges();
        return (range, null, null);
    }

    public (object? data, string? code, string? message) CompPolicyCheck(CurrentUser user, string workerId, Dtos.CompPolicyCheckRequest request)
    {
        var worker = db.Employments.FirstOrDefault(w => w.Id == workerId
            && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");

        var assignment = db.PositionAssignments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId
                && a.EmploymentId == workerId && a.Status == AssignmentStatus.Active
                && a.EffectiveFrom <= request.EffectiveFrom
                && (a.EffectiveTo == null || a.EffectiveTo >= request.EffectiveFrom))
            .OrderByDescending(a => a.EffectiveFrom).FirstOrDefault();
        var position = assignment is null
            ? null
            : db.Positions.FirstOrDefault(p => p.Id == assignment.PositionId
                && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        if (position is null || position.GradeCode is null)
            return (new
            {
                workerId, gate = "none", inPolicy = true, approvalRequired = false,
                message = "No graded position covers this date — no grade policy applies.",
            }, null, null);

        var range = db.GradeRanges.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId
                && r.GradeCode == position.GradeCode && r.Active
                && r.EffectiveFrom <= request.EffectiveFrom
                && (r.EffectiveTo == null || r.EffectiveTo >= request.EffectiveFrom))
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
        if (range is null)
            return (null, "COMP_POLICY_CONFIG_MISSING",
                $"Grade {position.GradeCode} has no active range covering {request.EffectiveFrom:yyyy-MM-dd} — missing config is an explicit error, never substituted (023 AC2).");

        var inPolicy = request.Amount >= range.MinAmount && request.Amount <= range.MaxAmount;
        return (new
        {
            workerId, gate = "grade", gradeCode = position.GradeCode,
            range = (object?)new { range.MinAmount, range.MidAmount, range.MaxAmount, range.Currency, range.Frequency },
            inPolicy, approvalRequired = !inPolicy,
            message = inPolicy ? "Within grade policy." : $"Outside grade {position.GradeCode} policy — authorized exception approval (R-FIN) required.",
        }, null, null);
    }

    public static object ProjectRange(GradeRange r) => new
    {
        r.Id, r.GradeCode, r.Currency, r.Frequency, r.MinAmount, r.MidAmount, r.MaxAmount,
        r.EffectiveFrom, r.EffectiveTo, r.Version, r.Active,
    };
}

// PM4 — work schedules & calendars (ERP-PEOPLE-025). Versions carry weekly planned hours; the
// expansion turns the named-timezone window into unambiguous UTC instants (DST-safe) and the
// UTC interval is attributed to its LOCAL start date (midnight-crossing rule).
public sealed class WorkScheduleService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : IWorkScheduleService
{
    public object ListSchedules(CurrentUser user) =>
        db.WorkSchedules.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId)
            .OrderBy(s => s.Code).ToList()
            .Select(s => new
            {
                s.Id, s.Code, s.Name, s.Timezone, s.StartTimeLocal, s.Active,
                versions = db.ScheduleVersions.Where(v => v.ScheduleId == s.Id).OrderBy(v => v.EffectiveFrom).ToList()
                    .Select(v => new { v.Id, v.Version, v.WeeklyPlannedHours, v.EffectiveFrom, v.EffectiveTo, v.HolidayCalendarId }),
            }).ToList();

    public (WorkSchedule? schedule, string? code, string? message) CreateSchedule(CurrentUser user, Dtos.CreateWorkScheduleRequest request)
    {
        if (!TimeOnly.TryParse(request.StartTimeLocal, out _))
            return (null, "VALIDATION_ERROR", "StartTimeLocal must be HH:mm.");
        try { TimeZoneInfo.FindSystemTimeZoneById(request.Timezone); }
        catch (TimeZoneNotFoundException) { return (null, "VALIDATION_ERROR", $"Timezone '{request.Timezone}' is not a known IANA/Windows zone."); }
        catch (InvalidTimeZoneException) { return (null, "VALIDATION_ERROR", $"Timezone '{request.Timezone}' is invalid."); }
        if (db.WorkSchedules.Any(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Code == request.Code))
            return (null, "SCHEDULE_EXISTS", $"Work schedule {request.Code} already exists.");
        var schedule = new WorkSchedule(numbers.Next("SCH"), ec.TenantId, ec.EntityId, request.Code,
            request.Name, request.Timezone, request.StartTimeLocal, true, user.Id, DateTimeOffset.UtcNow);
        db.WorkSchedules.Add(schedule);
        audit.Log(user, "create", "work_schedule", schedule.Id, $"Work schedule {request.Code} ({request.Timezone}, start {request.StartTimeLocal}) created");
        db.SaveChanges();
        return (schedule, null, null);
    }

    public (ScheduleVersion? version, string? code, string? message) CreateScheduleVersion(CurrentUser user, string scheduleId, Dtos.CreateScheduleVersionRequest request)
    {
        var schedule = db.WorkSchedules.FirstOrDefault(s => s.Id == scheduleId
            && s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
        if (schedule is null) return (null, "SCHEDULE_NOT_FOUND", "Work schedule not found.");
        if (request.WeeklyPlannedHours <= 0)
            return (null, "VALIDATION_ERROR", "WeeklyPlannedHours must be greater than 0.");
        if (request.HolidayCalendarId is { Length: > 0 }
            && !db.HolidayCalendars.Any(c => c.Id == request.HolidayCalendarId
                && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId))
            return (null, "HOLIDAY_CALENDAR_NOT_FOUND", "Holiday calendar not found.");
        var overlapping = db.ScheduleVersions.Where(v => v.TenantId == ec.TenantId && v.LegalEntityId == ec.EntityId
                && v.ScheduleId == scheduleId).ToList()
            .FirstOrDefault(v => DeferredUtil.Overlaps(request.EffectiveFrom, request.EffectiveTo, v.EffectiveFrom, v.EffectiveTo));
        if (overlapping is not null)
            return (null, "SCHEDULE_VERSION_OVERLAP",
                $"Schedule already has a version from {overlapping.EffectiveFrom:yyyy-MM-dd}; version intervals may not overlap.");
        var number = db.ScheduleVersions.Count(v => v.ScheduleId == scheduleId) + 1;
        var version = new ScheduleVersion($"{scheduleId}:v{number}", ec.TenantId, ec.EntityId, scheduleId,
            number, request.WeeklyPlannedHours, request.EffectiveFrom, request.EffectiveTo,
            request.HolidayCalendarId, user.Id, DateTimeOffset.UtcNow);
        db.ScheduleVersions.Add(version);
        audit.Log(user, "version", "work_schedule", scheduleId,
            $"Schedule {schedule.Code} v{number}: {request.WeeklyPlannedHours:0.##}h/week from {request.EffectiveFrom:yyyy-MM-dd}");
        db.SaveChanges();
        return (version, null, null);
    }

    // 025 AC2: the day's planned window (Mon–Fri carries weekly/5 hours) expands through the
    // named zone; a 22:00 +8h window crosses UTC midnight and stays attributed to the start date.
    public (object? data, string? code, string? message) Expand(CurrentUser user, string scheduleId, DateOnly from, DateOnly to)
    {
        var schedule = db.WorkSchedules.FirstOrDefault(s => s.Id == scheduleId
            && s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
        if (schedule is null) return (null, "SCHEDULE_NOT_FOUND", "Work schedule not found.");
        if (to < from) return (null, "VALIDATION_ERROR", "'to' must not precede 'from'.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone); }
        catch (TimeZoneNotFoundException) { return (null, "SCHEDULE_TIMEZONE_INVALID", $"Timezone '{schedule.Timezone}' is unknown on this host."); }
        catch (InvalidTimeZoneException) { return (null, "SCHEDULE_TIMEZONE_INVALID", $"Timezone '{schedule.Timezone}' is invalid."); }

        var start = TimeOnly.Parse(schedule.StartTimeLocal);
        var versions = db.ScheduleVersions.Where(v => v.TenantId == ec.TenantId && v.LegalEntityId == ec.EntityId
                && v.ScheduleId == scheduleId).OrderBy(v => v.EffectiveFrom).ToList();

        var holidayDates = LoadHolidayDates(versions);
        var days = new List<object>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var version = versions.LastOrDefault(v => v.EffectiveFrom <= date);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var holiday = holidayDates.TryGetValue(date, out var holidayName) ? holidayName : null;
            var isWorkday = version is not null && !isWeekend && holiday is null;
            var plannedHours = isWorkday ? decimal.Round(version!.WeeklyPlannedHours / 5m, 2) : 0m;
            var intervals = new List<object>();
            if (plannedHours > 0)
            {
                var localStart = new DateTime(date.Year, date.Month, date.Day, start.Hour, start.Minute, 0);
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
                var endUtc = startUtc.AddHours((double)plannedHours);
                intervals.Add(new { startUtc = DeferredUtil.UtcIso(startUtc), endUtc = DeferredUtil.UtcIso(endUtc) });
            }
            days.Add(new
            {
                date, isWorkday, plannedHours, holiday,
                intervals, attributedToLocalDate = date, timezone = schedule.Timezone,
                versionId = version?.Id,
            });
        }
        return (new { scheduleId, code = schedule.Code, timezone = schedule.Timezone, from, to, days }, null, null);
    }

    private Dictionary<DateOnly, string> LoadHolidayDates(List<ScheduleVersion> versions)
    {
        var calendarIds = versions.Where(v => v.HolidayCalendarId != null)
            .Select(v => v.HolidayCalendarId!).Distinct().ToList();
        if (calendarIds.Count == 0) return [];
        var rows = new Dictionary<DateOnly, string>();
        foreach (var calendar in db.HolidayCalendars.Where(c => calendarIds.Contains(c.Id)
                     && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId))
            foreach (var d in calendar.Dates)
                rows.TryAdd(d.Date, d.Name);
        return rows;
    }

    public object ListHolidayCalendars(CurrentUser user) =>
        db.HolidayCalendars.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId)
            .OrderBy(c => c.Code).ToList();

    public (HolidayCalendar? calendar, string? code, string? message) CreateHolidayCalendar(CurrentUser user, Dtos.CreateHolidayCalendarRequest request)
    {
        if (request.Dates.Count == 0)
            return (null, "VALIDATION_ERROR", "At least one holiday date is required.");
        if (db.HolidayCalendars.Any(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId && c.Code == request.Code))
            return (null, "HOLIDAY_CALENDAR_EXISTS", $"Holiday calendar {request.Code} already exists.");
        var calendar = new HolidayCalendar($"HCY:{request.Code}", ec.TenantId, ec.EntityId, request.Code,
            request.Name, true, user.Id, DateTimeOffset.UtcNow)
        {
            Dates = request.Dates.Select(d => new HolidayDate(d.Name, d.Date)).ToList(),
        };
        db.HolidayCalendars.Add(calendar);
        DeferredUtil.PinOwned(db, ec, calendar.Dates);
        audit.Log(user, "create", "holiday_calendar", calendar.Id, $"Holiday calendar {request.Code} with {request.Dates.Count} date(s)");
        db.SaveChanges();
        return (calendar, null, null);
    }
}
