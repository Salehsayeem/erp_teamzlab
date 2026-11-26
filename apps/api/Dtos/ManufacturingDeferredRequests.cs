using Erp.Api;

namespace Erp.Api.Dtos;

// MFG deferred screens (MF1-MF10) request DTOs — validated Guards-style in controllers BEFORE the
// service runs. Enum values stay locale-neutral; free-text reasons accept Unicode.

// ---- MF1 ----
public record CreateRoutingRequest(string OutputSku, int Revision, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public record RoutingDecisionRequest(string? Reason);
public record AddRoutingOperationRequest(string OperationCode, string WorkCenterId, string RequiredCapabilities,
    string TimeUnit, decimal SetupMin, decimal PerUnitMin, bool IsZeroDuration, int Seq, string? SubRoutingId = null);
public record MoveRoutingOperationRequest(string? SubRoutingId, int Seq);
public record CreateWorkCenterRequest(string Code, string Name, string Capabilities, bool Active = true);
public record AddCalendarWindowRequest(DateTimeOffset WindowStart, DateTimeOffset WindowEnd, bool IsAvailable, string Source);

// ---- MF2 / MF3 ----
public record CreateScheduleRunRequest(string OrderId, ScheduleRunMode Mode, DateOnly HorizonFrom, DateOnly HorizonTo,
    string CalendarVersion, string AlgorithmVersion);
public record SchedulePublicationRequest(string? OverrideReason);
public record CreateScenarioRequest(string Name, string BaseRunId, DateTimeOffset? FrozenFrom, DateTimeOffset? FrozenTo);
public record PlacementMoveRequest(string PlacementId, DateTimeOffset NewStartAt, string? OverrideReason, string? OverrideApprover);
public record ConflictResolutionRequest(string Resolution, string? Reason);

// ---- MF4 ----
public record MrpDemandItem(string ItemSku, decimal Qty, string Unit, DateOnly? By);
public record CreateMrpRunRequest(IReadOnlyList<MrpDemandItem> Demands, DateOnly HorizonFrom, DateOnly HorizonTo,
    decimal? SafetyStockPct, decimal? MinLotQty, decimal? MultipleQty);
public record MrpConversionRequest(string? Note);
public record MrpLineDecisionRequest(string Action, string Reason); // reject | defer
public record MrpPublicationRequest(string? OverrideReason, string? ExceptionDisposition);
public record ActionMessageUpdateRequest(string Action, string? Reason); // acknowledge | resolve | dismiss

// ---- MF5 ----
public record LaborTimeRequest(string WorkerEmploymentId, DateTimeOffset StartAt, DateTimeOffset EndAt);
public record CounterEventRequest(string SourceEventId, decimal CounterValue, string? WorkCenterId);
public record DowntimeEventRequest(string WorkCenterId, DateTimeOffset StartAt, DateTimeOffset EndAt, string ReasonCode, string? Note);
public record WipTransferRequest(int FromSeq, int ToSeq, decimal Qty, string? DeviationReason);
public record CorrectionRequest(string TargetType, string TargetId, string Reason, string CompensatingJson);
public record CorrectionApprovalRequest(string? Note);

// ---- MF6 ----
public record AddSubcontractOperationRequest(string OperationCode, string WorkCenterId, string SupplierId,
    string ServiceDescription, decimal SetupMin, decimal PerUnitMin, int Seq);
public record CustodyRequest(string OrderId, int RoutingOperationSeq, CustodyKind Kind, decimal Qty, string InventoryMovementId);
public record SubcontractReceiptRequest(int RoutingOperationSeq, decimal Qty, string? Note);

// ---- MF7 ----
public record MaintenanceEventRequest(string SourceEventId, int Revision, string ResourceCode, string? AssetId,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, string? Reason);
public record MaintenanceResolutionRequest(string OutageEventId, string ScheduleRunId, string OrderId, string WorkCenterId,
    string Disposition, string? Reason); // rescheduled | planner_exception

// ---- MF8 ----
public record CreateQualityPlanRequest(string ItemSku, string? SupplierId, string? ProcessStage, int Revision,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public record AddCharacteristicRequest(string Code, string Name, CharacteristicType DataType,
    decimal? MinValue, decimal? MaxValue, decimal? TargetValue, string? Unit, bool Mandatory);
public record CreateSamplingPlanRequest(SamplingKind Kind, int? SampleSize);
public record CreateQualityInspectionRequest(string QualityPlanId, string OrderId, string ItemSku, string? LotSerial, decimal PopulationQty);
public record AddInspectionResultRequest(string CharacteristicCode, string RawValue);
public record AmendResultRequest(string RawValue, string Reason);
public record FinalizeInspectionRequest(string? Note);

// ---- MF9 ----
public record CreateQualityHoldRequest(string ItemSku, string? LotSerial, decimal Qty, string? OrderId,
    string? InspectionId, string? NonconformanceId);
public record HoldDispositionRequest(HoldDisposition Disposition, decimal Qty, string? Reason);
public record CreateNcRequest(string SourceEventId, string Title, string? ItemSku, string? OrderId,
    decimal AffectedQty, IReadOnlyList<NcObjectRequest> Objects);
public record NcObjectRequest(string ObjectType, string ObjectRef, decimal Qty);
public record CreateContainmentRequest(string Kind);
public record ReleaseContainmentRequest(string Reason);
public record CreateCapaRequest(string Title, string? NonconformanceId, string EffectivenessCriteria);
public record AddCapaActionRequest(string Description, DateTimeOffset? DueBy);
public record CompleteCapaActionRequest(string? Note);
public record CapaVerificationRequest(string CriteriaResult, bool Satisfied);

// ---- MF10 ----
public record CreateReworkRequest(string SourceOrderId, string ItemSku, decimal Qty,
    string? NonconformanceId, string? HoldId, string? AdjustmentReason);
public record ReworkApprovalRequest(string? Note);
public record ReworkReinspectionRequest(string InspectionId);
public record VarianceDispositionRequest(string BucketCode, decimal QtyVariance, bool IsCritical, string? Owner);
public record VarianceDecisionRequest(string Disposition, string Reason); // accepted | overridden
