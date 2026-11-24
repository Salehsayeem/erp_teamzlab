namespace Erp.Api.Dtos;

// ASSETS request DTOs — validated in the controllers via Guards before the services run
public record CreateAssetRequest(string Tag, string Serial, string Name, string Category, decimal Cost, string LocationId);
public record CapitalizeAssetRequest(string? SourceProjectId, decimal Amount);
public record RunDepreciationRequest(string PeriodId);
public record TransferAssetRequest(string ToLocationId, DateTimeOffset EffectiveAt);
public record ImpairAssetRequest(decimal Amount, string Reason);
public record DisposeAssetRequest(decimal Proceeds, string Reason);

public record CreateProjectRequest(string Name, decimal Budget, string ManagerId, string? CustomerId);
public record CreateTaskRequest(string Name, decimal HoursEstimate);
public record CompleteTaskRequest(decimal HoursActual);

public record CreateCaseRequest(string Title, string Channel, string RequesterId, CasePriority Priority);
public record AssignCaseRequest(string AssigneeId);
public record TransitionCaseRequest(CaseStatus Status, string? Reason);
public record ResolveCaseRequest(string ResolutionCode);
