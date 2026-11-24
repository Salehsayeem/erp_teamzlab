namespace Erp.Api;

// ASSETS domain records + enums (ERP-ASSETS-001/003/019/023/024/026/037/041/043/044/046-048/051/053/055/056)
// — EF-mappable: every table carries tenant_id + legal_entity_id, records stay positional.

public enum AssetStatus { Cip, Draft, Active, Disposed }

// AccumulatedDepreciation carries depreciation AND approved impairment (R-T4k: impairment posts
// Cr 1500 Accumulated Depreciation, not Cr 1600) so bookValue = Cost − AccumulatedDepreciation
// and the disposal's Dr 1500 write-off cancels both exactly
public record FixedAsset(string Id, string TenantId, string LegalEntityId, string Tag, string Serial,
    string Name, string Category, decimal Cost, decimal AccumulatedDepreciation, string LocationId,
    AssetStatus Status, string? SourceProjectId, DateTimeOffset? InServiceDate, string CreatedBy, DateTimeOffset CreatedAt);

// one history row per asset state change (051 AC1/AC3, 053 AC3); JournalId "" = proposal not yet
// approved — the linked journal id is stamped on approval, which is what makes the event "Posted"
public record AssetEvent(string Id, string TenantId, string LegalEntityId, string AssetId, string EventType,
    decimal Amount, string JournalId, string ActorId, DateTimeOffset At,
    string? FromLocationId = null, string? ToLocationId = null);

// monthly straight-line run over the whole register (048): the Idempotency-Key of the posting
// request IS the run id — a retried run id cannot post duplicate journals
public record DepreciationRun(string Id, string TenantId, string LegalEntityId, string PeriodId,
    decimal TotalAmount, string JournalId, string CreatedBy, DateTimeOffset CreatedAt);

public record Project(string Id, string TenantId, string LegalEntityId, string Name, ProjectStatus Status,
    decimal Budget, string ManagerId, string Currency, string? CustomerId, string CreatedBy, DateTimeOffset CreatedAt);

public enum ProjectStatus { Planned, Active, OnHold, Closed }
public enum ProjectTaskStatus { Open, Done }

// unchanged from the wip slice: minimal WBS — Open/Done with hours estimate/actual
public record ProjectTask(string Id, string TenantId, string LegalEntityId, string ProjectId, string Name,
    ProjectTaskStatus Status, decimal HoursEstimate, decimal HoursActual);

public enum CasePriority { Low, Medium, High }
public enum CaseStatus { Open, InProgress, Waiting, Resolved, Closed }

// DueAt computed at intake from priority (026, R8d flat hours); BreachState derived on read (037)
public record ServiceCase(string Id, string TenantId, string LegalEntityId, string Title, string Channel,
    string RequesterId, CasePriority Priority, CaseStatus Status, string? AssignedTo, DateTimeOffset DueAt,
    string? ResolutionCode, string? BreachState, string CreatedBy, DateTimeOffset CreatedAt);
