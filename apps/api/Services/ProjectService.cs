using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Projects with a minimal WBS (tasks) and the ERP-ASSETS-019 closure gate — closure is
// maker-checked and rejected while any task is open, listing the offending task ids.
public sealed class ProjectService(IAssetsRepository projects, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IProjectService
{
    public object List(ProjectStatus? status) => projects.AllProjects(status).Select(Proj).ToList();

    public (object? data, string? code, string? message) Create(CurrentUser user, CreateProjectRequest request)
    {
        // ERP-ASSETS-001 AC2: owner (manager) and currency are identity fields — currency defaults
        // to the entity currency (BDT) since the pilot exposes no per-project currency input
        var project = new Project(numbers.Next("PRJ"), ec.TenantId, ec.EntityId, request.Name, ProjectStatus.Planned,
            request.Budget, request.ManagerId, EntityContext.SeedEntity.Currency, request.CustomerId,
            user.Id, DateTimeOffset.UtcNow);
        projects.AddProject(project);
        audit.Log(user, "create", "project", project.Id, $"Project '{request.Name}' created with budget {request.Budget:0.00} {project.Currency}, manager {request.ManagerId}");
        projects.Save();
        return (Proj(project), null, null);
    }

    public (object? data, string? code, string? message) AddTask(CurrentUser user, string projectId, CreateTaskRequest request)
    {
        var project = projects.FindProject(projectId);
        if (project is null) return (null, "PROJECT_NOT_FOUND", "Project not found.");
        // ERP-ASSETS-002 AC3: a closed project rejects new postings
        if (project.Status == ProjectStatus.Closed)
            return (null, "PROJECT_STATE_ERROR", "A closed project rejects new postings.");

        var task = new ProjectTask(numbers.Next("TSK"), ec.TenantId, ec.EntityId, projectId, request.Name,
            ProjectTaskStatus.Open, request.HoursEstimate, 0m);
        projects.AddTask(task);
        audit.Log(user, "create", "project_task", task.Id, $"Task '{request.Name}' added to project {projectId} (estimate {request.HoursEstimate}h)");
        projects.Save();
        return (ProjTask(task), null, null);
    }

    public (object? data, string? code, string? message) CompleteTask(CurrentUser user, string projectId, string taskId, decimal hoursActual)
    {
        var task = projects.FindTask(projectId, taskId);
        if (task is null) return (null, "TASK_NOT_FOUND", "Project task not found.");
        if (task.Status == ProjectTaskStatus.Done)
            return (null, "TASK_STATE_ERROR", "Task is already done.");

        projects.SetTaskDone(task, hoursActual);
        audit.Log(user, "complete", "project_task", taskId, $"Task {taskId} done with {hoursActual}h actual (estimate {task.HoursEstimate}h)");
        projects.Save();
        return (ProjTask(task), null, null);
    }

    public (object? data, string? code, string? message) Close(CurrentUser closer, string projectId)
    {
        var project = projects.FindProject(projectId);
        if (project is null) return (null, "PROJECT_NOT_FOUND", "Project not found.");
        if (project.Status == ProjectStatus.Closed)
            return (null, "PROJECT_STATE_ERROR", "Project is already closed.");
        if (project.CreatedBy == closer.Id)
            return (null, "APPROVAL_MAKER_CHECKER", "Maker-checker: the creator cannot close their own project.");
        if (!closer.Has("projects:approve"))
            return (null, "FORBIDDEN", "projects:approve permission required.");

        // ERP-ASSETS-019 AC1: closure gate returns gate results with source references (task ids)
        var open = projects.TasksForProject(projectId).Where(t => t.Status != ProjectTaskStatus.Done).ToList();
        if (open.Count > 0)
            return (null, "PROJECT_CLOSE_ERROR",
                $"All tasks must be Done before closing; {open.Count} open task(s) remain: {string.Join(", ", open.Select(t => t.Id))}.");

        projects.SetProjectStatus(project, ProjectStatus.Closed);
        audit.Log(closer, "close", "project", projectId, $"Project {projectId} closed by {closer.Email}");
        audit.Emit(closer, "assets.project.closed", new { projectId, closedBy = closer.Id });
        projects.Save();
        return (Proj(project), null, null);
    }

    private static object Proj(Project p) => new
    {
        p.Id, p.Name, p.Status, p.Budget, p.ManagerId, p.Currency, p.CustomerId, p.CreatedBy, p.CreatedAt,
    };

    private static object ProjTask(ProjectTask t) => new
    {
        t.Id, t.ProjectId, t.Name, t.Status, t.HoursEstimate, t.HoursActual,
    };
}
