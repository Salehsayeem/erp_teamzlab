using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// 062/063 — close checklists. Tasks complete only with an evidence ref or an authorized waiver
// (close:approve), N/A carries a reason, and NO row is ever removed: terminal states keep their
// provenance so the close evidence retains exactly what a waiver let through. Unresolved
// mandatory tasks are the 063 money gate FiscalPeriodService.Close consults.
public sealed class CloseService(ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : ICloseService
{
    private IQueryable<CloseTask> Tasks => db.CloseTasks
        .Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId);

    private IQueryable<FiscalPeriod> Periods => db.FiscalPeriods
        .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);

    public object List(string? periodId) =>
        (string.IsNullOrWhiteSpace(periodId)
            ? Tasks.OrderBy(t => t.Id).ToList()
            : Tasks.Where(t => t.PeriodId == periodId).OrderBy(t => t.Id).ToList());

    public (List<CloseTask>? tasks, string? code, string? message) CreateChecklist(CurrentUser user, CreateCloseChecklistRequest request)
    {
        if (Periods.FirstOrDefault(p => p.Id == request.PeriodId) is null)
            return (null, "PERIOD_NOT_FOUND", "Fiscal period not found.");
        if (Tasks.Any(t => t.PeriodId == request.PeriodId))
            return (null, "CLOSE_CHECKLIST_EXISTS", $"Period {request.PeriodId} already has a close checklist; add tasks to it instead.");
        if (request.Tasks.Count == 0)
            return (null, "VALIDATION_ERROR", "At least one close task is required.");

        var batchIds = new List<string>();
        foreach (var input in request.Tasks)
        {
            if (Guards.Check(input.Title, "Title", 200) is { } titleError) return (null, "VALIDATION_ERROR", titleError);
            // dependencies inside the creation batch resolve only within that batch
            if (input.DependsOnTaskId is not null && !batchIds.Contains(input.DependsOnTaskId))
                return (null, "VALIDATION_ERROR",
                    $"DependsOnTaskId '{input.DependsOnTaskId}' must reference a task created in the same checklist.");
            batchIds.Add(numbers.Next("CTK"));
        }

        var created = request.Tasks.Select((input, i) => new CloseTask(batchIds[i], ec.TenantId, ec.EntityId,
            request.PeriodId, input.Title.Trim(), input.Mandatory, CloseTaskStatus.Pending,
            input.DueDate, input.DependsOnTaskId)).ToList();
        db.CloseTasks.AddRange(created);
        audit.Log(user, "create", "close_checklist", request.PeriodId,
            $"Close checklist created for period {request.PeriodId}: {created.Count} task(s), {created.Count(t => t.Mandatory)} mandatory ({string.Join(", ", created.Select(t => t.Id))}).");
        audit.Emit(user, "finance.closechecklist.created",
            new { periodId = request.PeriodId, tasks = created.Select(t => t.Id).ToArray(), createdBy = user.Id });
        db.SaveChanges();
        return (created, null, null);
    }

    public (CloseTask? task, string? code, string? message) AddTask(CurrentUser user, string periodId, AddCloseTaskRequest request)
    {
        if (Periods.FirstOrDefault(p => p.Id == periodId) is null)
            return (null, "PERIOD_NOT_FOUND", "Fiscal period not found.");
        if (Guards.Check(request.Title, "Title", 200) is { } titleError) return (null, "VALIDATION_ERROR", titleError);
        if (request.DependsOnTaskId is not null
            && !Tasks.Any(t => t.PeriodId == periodId && t.Id == request.DependsOnTaskId))
            return (null, "VALIDATION_ERROR",
                $"DependsOnTaskId '{request.DependsOnTaskId}' does not reference a task of this period's checklist.");

        var task = new CloseTask(numbers.Next("CTK"), ec.TenantId, ec.EntityId, periodId,
            request.Title.Trim(), request.Mandatory, CloseTaskStatus.Pending, request.DueDate, request.DependsOnTaskId);
        db.CloseTasks.Add(task);
        audit.Log(user, "create", "close_task", task.Id,
            $"Close task {task.Id} added to period {periodId}: {task.Title} ({(task.Mandatory ? "mandatory" : "optional")}{(task.DependsOnTaskId is not null ? $", depends on {task.DependsOnTaskId}" : "")}).");
        db.SaveChanges();
        return (task, null, null);
    }

    public (CloseTask? task, string? code, string? message) Complete(CurrentUser user, string periodId, string taskId, CompleteCloseTaskRequest request)
    {
        var task = Tasks.FirstOrDefault(t => t.PeriodId == periodId && t.Id == taskId);
        if (task is null) return (null, "CLOSE_TASK_NOT_FOUND", "Close task not found.");
        if (task.Status != CloseTaskStatus.Pending)
            return (null, "CLOSE_TASK_STATE_ERROR", $"Close task {task.Id} is already {task.Status}; terminal tasks are immutable evidence.");

        var hasEvidence = !string.IsNullOrWhiteSpace(request.EvidenceRef);
        var hasWaiver = !string.IsNullOrWhiteSpace(request.WaiverReason);
        if (hasEvidence && hasWaiver)
            return (null, "VALIDATION_ERROR", "Provide either an evidenceRef or a waiverReason — not both.");
        if (!hasEvidence && !hasWaiver)
            return (null, "CLOSE_TASK_EVIDENCE_REQUIRED",
                $"Close task {task.Id} needs an evidenceRef or an authorized waiverReason; it cannot complete empty-handed (062 AC1).");

        CloseTask resolved;
        if (hasWaiver)
        {
            resolved = task with { Status = CloseTaskStatus.Waived, WaiverReason = request.WaiverReason!.Trim(), CompletedBy = user.Id, CompletedAt = DateTimeOffset.UtcNow };
            db.CloseTasks.Entry(task).CurrentValues.SetValues(resolved);
            audit.Log(user, "waive", "close_task", task.Id,
                $"Close task {task.Id} on period {periodId} waived by {user.Email}: {resolved.WaiverReason} (retained in close evidence, 063 AC2).");
            audit.Emit(user, "finance.closetask.waived",
                new { taskId = task.Id, periodId, reason = resolved.WaiverReason, waivedBy = user.Id });
        }
        else
        {
            resolved = task with { Status = CloseTaskStatus.Completed, EvidenceRef = request.EvidenceRef!.Trim(), CompletedBy = user.Id, CompletedAt = DateTimeOffset.UtcNow };
            db.CloseTasks.Entry(task).CurrentValues.SetValues(resolved);
            audit.Log(user, "complete", "close_task", task.Id,
                $"Close task {task.Id} on period {periodId} completed by {user.Email} with evidence '{resolved.EvidenceRef}'.");
            audit.Emit(user, "finance.closetask.completed",
                new { taskId = task.Id, periodId, evidenceRef = resolved.EvidenceRef, completedBy = user.Id });
        }
        db.SaveChanges();
        return (resolved, null, null);
    }

    public (CloseTask? task, string? code, string? message) MarkNotApplicable(CurrentUser user, string periodId, string taskId, NaCloseTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (null, "CLOSE_TASK_REASON_REQUIRED", "A N/A decision requires a reason; the row is retained with it (062 AC3).");
        var task = Tasks.FirstOrDefault(t => t.PeriodId == periodId && t.Id == taskId);
        if (task is null) return (null, "CLOSE_TASK_NOT_FOUND", "Close task not found.");
        if (task.Status != CloseTaskStatus.Pending)
            return (null, "CLOSE_TASK_STATE_ERROR", $"Close task {task.Id} is already {task.Status}; terminal tasks are immutable evidence.");

        var resolved = task with { Status = CloseTaskStatus.NotApplicable, NaReason = request.Reason.Trim(), CompletedBy = user.Id, CompletedAt = DateTimeOffset.UtcNow };
        db.CloseTasks.Entry(task).CurrentValues.SetValues(resolved);
        audit.Log(user, "na", "close_task", task.Id,
            $"Close task {task.Id} on period {periodId} marked N/A by {user.Email}: {resolved.NaReason} (row retained, never removed).");
        audit.Emit(user, "finance.closetask.na",
            new { taskId = task.Id, periodId, reason = resolved.NaReason, decidedBy = user.Id });
        db.SaveChanges();
        return (resolved, null, null);
    }

    // 063 AC1 — the money gate: unresolved MANDATORY tasks block the close; the message names them
    public List<CloseTask> UnresolvedMandatory(string periodId) =>
        Tasks.Where(t => t.PeriodId == periodId && t.Mandatory && t.Status == CloseTaskStatus.Pending)
            .OrderBy(t => t.Id).ToList();

    // 063 AC2 — the waiver variant: each unresolved mandatory task is marked Waived with the
    // reason/user/time recorded ON the retained row, so the close evidence keeps the item.
    public void WaiveAllForClose(CurrentUser user, string periodId, string reason)
    {
        foreach (var task in UnresolvedMandatory(periodId))
        {
            var waived = task with { Status = CloseTaskStatus.Waived, WaiverReason = reason, CompletedBy = user.Id, CompletedAt = DateTimeOffset.UtcNow };
            db.CloseTasks.Entry(task).CurrentValues.SetValues(waived);
            audit.Log(user, "waive", "close_task", task.Id,
                $"Close task {task.Id} waived at gate-close of period {periodId} by {user.Email}: {reason} (unresolved item retained in close evidence).");
            audit.Emit(user, "finance.close.gate.waived",
                new { taskId = task.Id, periodId, reason, waivedBy = user.Id, waivedAt = waived.CompletedAt });
        }
    }
}
