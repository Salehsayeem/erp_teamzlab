namespace Erp.Api.Interfaces;

// Fixed assets / asset events / depreciation runs / projects / tasks / service cases — every
// lookup is tenant+entity scoped (CLAUDE.md); mutations stage into the caller's SaveChanges.
public interface IAssetsRepository
{
    // fixed assets
    List<FixedAsset> AllAssets(AssetStatus? status);
    FixedAsset? FindAsset(string assetId);
    bool AssetTagOrSerialInActiveUse(string tag, string serial); // any asset not Disposed (041 AC1)
    void AddAsset(FixedAsset asset);
    void SetAssetStatus(object asset, AssetStatus status);
    void SetAssetLocation(object asset, string locationId);
    void SetAssetAccumulated(object asset, decimal accumulatedDepreciation);
    void SetAssetSourceProject(object asset, string? sourceProjectId);
    void SetAssetInServiceDate(object asset, DateTimeOffset inServiceDate);
    List<FixedAsset> DepreciableAssets(); // Active with remaining depreciable basis

    // asset events (proposals are events with JournalId ""; approval stamps the journal id)
    void AddEvent(AssetEvent assetEvent);
    AssetEvent? FindEvent(string eventId);
    AssetEvent? FindPendingEvent(string assetId, string eventType);
    void SetEventJournal(object assetEvent, string journalId);

    // depreciation runs
    void AddRun(DepreciationRun run);

    // locations (shared inventory table) — entity-scoped plus tenant-wide for the cross-entity ruling
    StockLocation? FindLocationInEntity(string locationId);
    StockLocation? FindLocationInTenant(string locationId);

    // projects + tasks
    List<Project> AllProjects(ProjectStatus? status);
    Project? FindProject(string projectId);
    void AddProject(Project project);
    void SetProjectStatus(object project, ProjectStatus status);
    List<ProjectTask> TasksForProject(string projectId);
    ProjectTask? FindTask(string projectId, string taskId);
    void AddTask(ProjectTask task);
    void SetTaskDone(object task, decimal hoursActual);

    // service cases
    List<ServiceCase> AllCases();
    ServiceCase? FindCase(string caseId);
    void AddCase(ServiceCase serviceCase);
    void SetCaseAssigned(object serviceCase, string assignedTo);
    void SetCaseStatus(object serviceCase, CaseStatus status, string? resolutionCode, DateTimeOffset? dueAt);

    int Save();
}
