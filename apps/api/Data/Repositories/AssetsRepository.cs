using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Fixed assets, asset events, depreciation runs, projects, project tasks, service cases. Every
// query filters tenant+entity from the scoped EntityContext; state flips go through the Set
// helper so EF writes positional-record properties via their backing fields (see InventoryRepository).
public sealed class AssetsRepository(ErpDbContext db, EntityContext ec) : IAssetsRepository
{
    private IQueryable<FixedAsset> AssetsScoped() =>
        db.Assets.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);

    private IQueryable<AssetEvent> EventsScoped() =>
        db.AssetEvents.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);

    private IQueryable<DepreciationRun> RunsScoped() =>
        db.DepreciationRuns.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    private IQueryable<Project> ProjectsScoped() =>
        db.Projects.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);

    private IQueryable<ProjectTask> TasksScoped() =>
        db.ProjectTasks.Where(t => t.TenantId == ec.TenantId && t.LegalEntityId == ec.EntityId);

    private IQueryable<ServiceCase> CasesScoped() =>
        db.ServiceCases.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public List<FixedAsset> AllAssets(AssetStatus? status) =>
        (status is null ? AssetsScoped() : AssetsScoped().Where(a => a.Status == status))
            .OrderBy(a => a.Id).ToList();

    public FixedAsset? FindAsset(string assetId) => AssetsScoped().FirstOrDefault(a => a.Id == assetId);

    public bool AssetTagOrSerialInActiveUse(string tag, string serial) =>
        AssetsScoped().Any(a => a.Status != AssetStatus.Disposed && (a.Tag == tag || a.Serial == serial));

    public void AddAsset(FixedAsset asset) => db.Assets.Add(asset);

    public List<FixedAsset> DepreciableAssets() =>
        AssetsScoped().Where(a => a.Status == AssetStatus.Active).OrderBy(a => a.Id).ToList()
            .Where(a => a.Cost - a.AccumulatedDepreciation > 0m).ToList();

    public void AddEvent(AssetEvent assetEvent) => db.AssetEvents.Add(assetEvent);

    public AssetEvent? FindEvent(string eventId) => EventsScoped().FirstOrDefault(e => e.Id == eventId);

    public AssetEvent? FindPendingEvent(string assetId, string eventType) =>
        EventsScoped().FirstOrDefault(e => e.AssetId == assetId && e.EventType == eventType && e.JournalId == "");

    public void AddRun(DepreciationRun run) => db.DepreciationRuns.Add(run);

    // locations live in the shared inventory table — the same entity-scoped rule, plus a
    // tenant-wide probe so a cross-entity destination can be answered 409, not 404 (R8b)
    public StockLocation? FindLocationInEntity(string locationId) =>
        db.Locations.FirstOrDefault(l => l.Id == locationId
            && l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId);

    public StockLocation? FindLocationInTenant(string locationId) =>
        db.Locations.FirstOrDefault(l => l.Id == locationId && l.TenantId == ec.TenantId);

    public List<Project> AllProjects(ProjectStatus? status) =>
        (status is null ? ProjectsScoped() : ProjectsScoped().Where(p => p.Status == status))
            .OrderBy(p => p.Id).ToList();

    public Project? FindProject(string projectId) => ProjectsScoped().FirstOrDefault(p => p.Id == projectId);

    public void AddProject(Project project) => db.Projects.Add(project);

    public List<ProjectTask> TasksForProject(string projectId) =>
        TasksScoped().Where(t => t.ProjectId == projectId).OrderBy(t => t.Id).ToList();

    public ProjectTask? FindTask(string projectId, string taskId) =>
        TasksScoped().FirstOrDefault(t => t.Id == taskId && t.ProjectId == projectId);

    public void AddTask(ProjectTask task) => db.ProjectTasks.Add(task);

    public List<ServiceCase> AllCases() => CasesScoped().OrderBy(c => c.Id).ToList();

    public ServiceCase? FindCase(string caseId) => CasesScoped().FirstOrDefault(c => c.Id == caseId);

    public void AddCase(ServiceCase serviceCase) => db.ServiceCases.Add(serviceCase);

    public int Save() => db.SaveChanges();

    public void SetAssetStatus(object asset, AssetStatus status) => Set(asset, nameof(FixedAsset.Status), status);
    public void SetAssetLocation(object asset, string locationId) => Set(asset, nameof(FixedAsset.LocationId), locationId);
    public void SetAssetAccumulated(object asset, decimal accumulated) => Set(asset, nameof(FixedAsset.AccumulatedDepreciation), accumulated);
    public void SetAssetSourceProject(object asset, string? sourceProjectId) => Set(asset, nameof(FixedAsset.SourceProjectId), sourceProjectId);
    public void SetAssetInServiceDate(object asset, DateTimeOffset inServiceDate) => Set(asset, nameof(FixedAsset.InServiceDate), inServiceDate);
    public void SetEventJournal(object assetEvent, string journalId) => Set(assetEvent, nameof(AssetEvent.JournalId), journalId);
    public void SetProjectStatus(object project, ProjectStatus status) => Set(project, nameof(Project.Status), status);
    public void SetTaskDone(object task, decimal hoursActual)
    {
        Set(task, nameof(ProjectTask.HoursActual), hoursActual);
        Set(task, nameof(ProjectTask.Status), ProjectTaskStatus.Done);
    }
    public void SetCaseAssigned(object serviceCase, string assignedTo) => Set(serviceCase, nameof(ServiceCase.AssignedTo), assignedTo);
    public void SetCaseStatus(object serviceCase, CaseStatus status, string? resolutionCode, DateTimeOffset? dueAt)
    {
        Set(serviceCase, nameof(ServiceCase.Status), status);
        Set(serviceCase, nameof(ServiceCase.ResolutionCode), resolutionCode);
        if (dueAt is not null) Set(serviceCase, nameof(ServiceCase.DueAt), dueAt.Value);
    }

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
