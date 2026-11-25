namespace Erp.Api;

// --- MFG deferred screens (MF1-MF10, ERP-MFG-024..070) — tenant_id + legal_entity_id on every
// table from day one, ids from the shared doc_ sequences (partition prefixes: rot, wkc, srn, scn,
// cfl, mrp, lbt, cme, dwn, wtr, mcr, sub, mav, qpl, qin, qhd, ncr, cpa, txp, rwk, vln). Enums are
// EF-mapped by int: APPEND-ONLY, never insert a value mid-list (same rule as BomStatus).
// Rulings applied: Q-MF1 FULL MRP pipeline (netting vs availability, planned orders, conversion —
// buy-side targets the IRequisitionSink stub until PROC binds); Q-MF2 maintenance = manual windows
// + idempotent ingestion per the 053 contract shape; Q-MF3 sampling = full-inspection + fixed
// sample; Q-MF4 genealogy capture = forward-only from the landed completion path; Q-MF5 WIP
// projection computed-on-read, no projection table.

// ---- MF1: routings / work centers / operation time model (024/025/026) ----

public enum RoutingStatus { Draft, Approved, Retired }

public enum OperationKind { Internal, Subcontract }

// approved revisions are immutable — a change is a NEW revision (024, BomRevision pattern).
// Released orders freeze RoutingRevisionId + the operation sequence (024 AC3).
public record RoutingRevision(string Id, string TenantId, string LegalEntityId, string OutputSku, int Revision,
    RoutingStatus Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? ApprovedBy, DateTimeOffset? ApprovedAt, string CreatedBy, DateTimeOffset CreatedAt);

// ordered operations of a revision — natural key {routingRevisionId}:{seq}. Time parameters are
// typed on the operation (026): setup = per-lot fixed, run = per-unit; both carry the time unit.
// Zero/negative durations are rejected at approval EXCEPT ops that declare IsZeroDuration
// (permitted zero-duration operations, e.g. milestones). SubRoutingId is the delegation edge the
// approval cycle check walks (024 AC2).
public record RoutingOperation(string Id, string TenantId, string LegalEntityId, string RoutingRevisionId,
    int Seq, string OperationCode, string WorkCenterId, string RequiredCapabilities, string TimeUnit,
    decimal SetupMin, decimal PerUnitMin, bool IsZeroDuration, OperationKind Kind,
    string? SupplierId, string? ServiceDescription, string? SubRoutingId);

public record WorkCenter(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    string Capabilities, bool Active, string CreatedBy, DateTimeOffset CreatedAt);

// availability timeline — one interval row per window; outages carry IsAvailable=false. Maintenance
// ingestion (MF7) materializes accepted outage windows here with the source event id attached.
public record ResourceCalendar(string Id, string TenantId, string LegalEntityId, string WorkCenterId,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, bool IsAvailable, string Source,
    string? SourceEventId, string CreatedBy, DateTimeOffset CreatedAt);

// ---- MF2: scheduling (027/028/029/030) ----

public enum ScheduleRunMode { Finite, Infinite }

public enum ScheduleRunStatus { Completed, Failed }

// the run record (027): horizon + calendar/algorithm versions + mode; the mode rides every output
// so an infinite plan can never masquerade as finite (028). Runs schedule ONE order per run
// (pilot trim): the order's released routing drives the operation list.
public record ScheduleRun(string Id, string TenantId, string LegalEntityId, string OrderId,
    ScheduleRunMode Mode, ScheduleRunStatus Status, DateOnly HorizonFrom, DateOnly HorizonTo,
    string CalendarVersion, string AlgorithmVersion, string CreatedBy, DateTimeOffset CreatedAt);

// placements are the run's output — unscheduled ops carry a reason code and are never silently
// dropped (027 AC3). FrozenInside marks placements overlapping a scenario freeze interval.
public record OperationPlacement(string Id, string TenantId, string LegalEntityId, string ScheduleRunId,
    string OrderId, string RoutingRevisionId, int Seq, string WorkCenterId, string OperationCode,
    DateTimeOffset? StartAt, DateTimeOffset? EndAt, bool Scheduled, string? UnscheduledReason,
    string CreatedBy, DateTimeOffset CreatedAt);

// scenarios (029): a frozen interval over a base run; changes inside the interval need an
// override (reason + mfg:schedule actor who is not the scenario maker).
public record ScheduleScenario(string Id, string TenantId, string LegalEntityId, string Name,
    string BaseRunId, DateTimeOffset? FrozenFrom, DateTimeOffset? FrozenTo, string CreatedBy, DateTimeOffset CreatedAt);

public record ScheduleOverride(string Id, string TenantId, string LegalEntityId, string ScenarioId,
    string Reason, string ApprovedBy, DateTimeOffset At);

// hard conflicts (030): a conflicted schedule cannot publish without an approved override.
public record ConflictReport(string Id, string TenantId, string LegalEntityId, string ScheduleRunId,
    string Severity, string Code, string Detail, bool Resolved, string? ResolvedBy, DateTimeOffset? ResolvedAt);

// publication is unique per run (published events idempotent on run id, 040) — the reviewer is
// NOT the run maker (maker-checker).
public record SchedulePublication(string Id, string TenantId, string LegalEntityId, string ScheduleRunId,
    string ReviewerId, string? OverrideReason, DateTimeOffset At);

// ---- MF4: MRP run → netting → recommendations → conversion → publication (033..040) ----

public enum MrpRunStatus { InProgress, Completed, Failed }

// the run is immutable once terminal (033): inputs (demands, parameters) + the stock/BOM snapshot
// are frozen in SnapshotJson so the same snapshot+params replay identically.
public record MrpRun(string Id, string TenantId, string LegalEntityId, MrpRunStatus Status,
    DateOnly HorizonFrom, DateOnly HorizonTo, string ParametersJson, string SnapshotJson,
    string? FailureDetail, string CreatedBy, DateTimeOffset CreatedAt);

// one recommendation line per (exploded requirement, level): netting result + applied modifiers
// with their resulting quantity (039) + requiredBy dating + the parent path (035). Conversion
// status rides the line (037): Pending → Converted (exactly one linked order) / Rejected /
// Deferred, idempotent on retry via the unique (run, item, path) key.
public record MrpLine(string Id, string TenantId, string LegalEntityId, string RunId, int LineNo,
    string ItemSku, int ExplosionLevel, string ParentPath, decimal GrossQty, decimal SupplyQty,
    decimal NetQty, string ModifiersJson, DateOnly? RequiredBy, int? LeadTimeDays, string? ExceptionCode,
    string ConversionStatus, string? ConvertedOrderId, string? ConvertedRequisitionRef,
    string? DecisionReason, string CreatedBy, DateTimeOffset CreatedAt);

// action messages (038): open / acknowledged / resolved / dismissed-with-reason.
public enum ActionMessageState { Open, Acknowledged, Resolved, Dismissed }

public record ActionMessage(string Id, string TenantId, string LegalEntityId, string RunId,
    string MrpLineId, string Code, string Message, ActionMessageState State,
    string? DismissReason, string? AcknowledgedBy, DateTimeOffset? AcknowledgedAt, string CreatedBy, DateTimeOffset CreatedAt);

// publication (040): reviewer + snapshot (run) id + exception disposition; stale critical
// sources (snapshot drifted from live stock) block unless an override reason is supplied.
public record MrpPublication(string Id, string TenantId, string LegalEntityId, string RunId,
    string ReviewerId, bool OverrodeStaleSources, string? OverrideReason, string ExceptionDisposition, DateTimeOffset At);

// ---- MF5: shop-floor reporting extensions (044/045/046/047/048/049) ----

public enum LaborEntryStatus { Reported, Approved, Rejected }

// workers report time on their order; a supervisor (≠ reporter) approves; APPROVED rows are
// immutable — corrections are NEW linked reversing entries (049). Overlapping intervals are
// rejected at report time (044 AC3).
public record LaborTimeEntry(string Id, string TenantId, string LegalEntityId, string OrderId,
    string WorkerEmploymentId, DateTimeOffset StartAt, DateTimeOffset EndAt, decimal Minutes,
    LaborEntryStatus Status, string? ApprovedBy, DateTimeOffset? ApprovedAt,
    string? CorrectionOfId, string ReportedBy, DateTimeOffset ReportedAt);

// machine counters: duplicate source event ids are ignored idempotently (045 AC1); a value below
// the previous reading is a flagged rollover whose delta is the new reading (never negative).
public record CounterEvent(string Id, string TenantId, string LegalEntityId, string OrderId,
    string SourceEventId, decimal CounterValue, decimal DeltaQty, bool Rollover,
    string? CorrectionOfId, string ReportedBy, DateTimeOffset At);

// downtime with reason codes validated against the effective profile (046); overlapping
// intervals rejected; corrections link to the original and preserve both rows.
public record DowntimeEvent(string Id, string TenantId, string LegalEntityId, string OrderId,
    string WorkCenterId, DateTimeOffset StartAt, DateTimeOffset EndAt, string ReasonCode,
    string? CorrectionOfId, string ReportedBy, DateTimeOffset At);

// WIP transfers between routing steps (048): bounded by the transferable open qty, destination
// must follow routing precedence unless an approved deviation (reason + approver) is recorded.
public record WipTransfer(string Id, string TenantId, string LegalEntityId, string OrderId,
    int FromSeq, int ToSeq, decimal Qty, string? DeviationReason, string? DeviationApprovedBy,
    string CreatedBy, DateTimeOffset CreatedAt);

// linked corrections (049): a correction NEVER edits the original — it proposes a compensating
// event that an independent approver releases; the pair stays linked on both rows.
public record CorrectionRecord(string Id, string TenantId, string LegalEntityId, string TargetType,
    string TargetId, string Reason, string CompensatingJson, string Status, string? ApprovedBy,
    DateTimeOffset? ApprovedAt, string ProposedBy, DateTimeOffset ProposedAt);

// ---- MF6: subcontract operations (050/051/052) ----

public enum CustodyKind { Send, Return }

// custody ledger: send/return rows MUST reference an accepted inventory movement id (issue,
// transfer, or receipt) — the reconciliation (sent vs returned vs outstanding) reads from here.
public record SubcontractCustody(string Id, string TenantId, string LegalEntityId, string OrderId,
    int RoutingOperationSeq, CustodyKind Kind, decimal Qty, string InventoryMovementId,
    string CreatedBy, DateTimeOffset CreatedAt);

// receipts are quality-gated (052): when the subcontract output requires inspection the receipt
// stays PendingQuality until an accepted disposition lands; completion can never exceed the
// accepted returned quantity.
public record SubcontractReceipt(string Id, string TenantId, string LegalEntityId, string OrderId,
    int RoutingOperationSeq, decimal Qty, decimal AcceptedQty, string Status,
    string? QualityHoldId, string CreatedBy, DateTimeOffset CreatedAt);

// ---- MF7: maintenance interface (053/054) ----

public enum MaintenanceEventStatus { Accepted, Exception }

// idempotent ingestion per the 053 contract shape (Q-MF2 — ASSETS emits nothing today, so the
// same endpoint serves manual windows): (sourceEventId, revision) — duplicates return the stored
// window, out-of-order revisions never regress availability, unmapped resources land as
// integration exceptions instead of crashing the feed.
public record MaintenanceAvailabilityEvent(string Id, string TenantId, string LegalEntityId,
    string SourceEventId, int Revision, string ResourceCode, string? AssetId,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, string? Reason,
    MaintenanceEventStatus Status, string? ExceptionReason, string IngestedBy, DateTimeOffset IngestedAt);

// planner review (054): an accepted outage either reschedules the intersecting placements or
// becomes a documented planner exception — both dispositions live here.
public record MaintenanceImpact(string Id, string TenantId, string LegalEntityId, string OutageEventId,
    string ScheduleRunId, string OrderId, string WorkCenterId, int OverlapMinutes, string Disposition,
    string? Reason, string? DecidedBy, DateTimeOffset? DecidedAt);

// ---- MF8: quality plans, sampling, inspections (055/056/057/058) ----

public enum QualityPlanStatus { Draft, Approved, Retired }

public enum CharacteristicType { Numeric, Text, Boolean }

// versioned plans scoped by item/supplier/process/site (055); a change is a NEW revision. The
// inspection stores the EXACT plan revision it executed against.
public record QualityPlan(string Id, string TenantId, string LegalEntityId, string ItemSku,
    string? SupplierId, string? ProcessStage, int Revision, QualityPlanStatus Status,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? ApprovedBy, DateTimeOffset? ApprovedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

// typed characteristics (056): numeric ranges / text / boolean with the evaluation rule version.
public record QualityCharacteristic(string Id, string TenantId, string LegalEntityId, string PlanId,
    string Code, string Name, CharacteristicType DataType, decimal? MinValue, decimal? MaxValue,
    decimal? TargetValue, string? Unit, bool Mandatory, int RuleVersion);

public enum SamplingKind { FullInspection, FixedSample }

// sampling plans per quality plan revision (057): full inspection selects every unit (trivial
// default), fixed-sample records the sample size. AQL-style plans deferred (Q-MF3).
public record SamplingPlan(string Id, string TenantId, string LegalEntityId, string QualityPlanId,
    SamplingKind Kind, int? SampleSize, string CreatedBy, DateTimeOffset CreatedAt);

public enum InspectionStatus { InProgress, Finalized }

public record Inspection(string Id, string TenantId, string LegalEntityId, string QualityPlanId,
    int PlanRevision, string OrderId, string ItemSku, string? LotSerial, int SampleSize,
    InspectionStatus Status, string InspectorId, string? FinalizedBy, DateTimeOffset? FinalizedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

// results are append-only (058): finalized inspections are immutable; a correction is a NEW
// linked amended result row that preserves both (AmendsId). Evaluation stores the rule version
// that judged the raw value (056).
public record InspectionResult(string Id, string TenantId, string LegalEntityId, string InspectionId,
    string CharacteristicCode, string RawValue, bool? EvaluatedPass, int RuleVersion, int AmendmentNo,
    string? AmendsId, string EnteredBy, DateTimeOffset EnteredAt);

// ---- MF9: quality holds, NCR, CAPA, dashboards (059..063) ----

public enum QualityHoldStatus { Open, Dispositioned }

public enum HoldDisposition { ReleaseToStock, Scrap, Rework, Return }

// the hold is the availability authority (059): open hold qty is subtracted from availability
// (the single flagged INVENTORY edit); only a disposition by a quality:dispose officer who is
// NOT the holder restores/retires the quantity per the decision.
public record QualityHold(string Id, string TenantId, string LegalEntityId, string ItemSku,
    string? LotSerial, decimal Qty, string? OrderId, string? InspectionId, string? NonconformanceId,
    QualityHoldStatus Status, HoldDisposition? Disposition, decimal? DispositionedQty,
    string? JournalId, string HeldBy, DateTimeOffset HeldAt, string? DispositionedBy, DateTimeOffset? DispositionedAt);

public enum NcStatus { Open, Closed }

// nonconformance (060): unique NC id referencing at least one affected object + the source
// event; closure is blocked while mandatory containment is open (061).
public record Nonconformance(string Id, string TenantId, string LegalEntityId, string SourceEventId,
    string Title, string? ItemSku, string? OrderId, decimal AffectedQty, NcStatus Status,
    string? ClosedBy, DateTimeOffset? ClosedAt, string RaisedBy, DateTimeOffset RaisedAt);

public record NcObject(string Id, string TenantId, string LegalEntityId, string NonconformanceId,
    string ObjectType, string ObjectRef, decimal Qty);

public record ContainmentAction(string Id, string TenantId, string LegalEntityId, string NonconformanceId,
    string Kind, string Status, string OpenedBy, string? ReleasedBy, DateTimeOffset? ReleasedAt,
    string? ReleaseReason);

public enum CapaStatus { Open, Closed }

public record CapaCase(string Id, string TenantId, string LegalEntityId, string Title,
    string? NonconformanceId, string EffectivenessCriteria, CapaStatus Status,
    string? ClosedBy, DateTimeOffset? ClosedAt, string CreatedBy, DateTimeOffset CreatedAt);

public record CapaAction(string Id, string TenantId, string LegalEntityId, string CapaCaseId,
    string Description, DateTimeOffset? DueBy, bool Done, string? CompletedBy, DateTimeOffset? CompletedAt);

public record CapaVerification(string Id, string TenantId, string LegalEntityId, string CapaCaseId,
    string CriteriaResult, bool Satisfied, string VerifiedBy, DateTimeOffset At);

// ---- MF10: genealogy, rework, close reconciliation (064/065/066/068/070) ----

// immutable transformation events (064) — emitted forward-only by the landed completion path
// (Q-MF4): inputs (consumed issues) → output qty (+ lot identity when the item is controlled).
public record TransformationEvent(string Id, string TenantId, string LegalEntityId, string OrderId,
    string CompletionId, string OutputSku, decimal OutputQty, string? LotSerial,
    string InputsJson, string EmittedBy, DateTimeOffset At);

public enum ReworkStatus { Draft, Approved, Completed }

// rework orders (068): planned qty ≤ identified affected qty absent an approved adjustment
// (reason + approver); release/completion is gated on a linked reinspection being finalized
// and passing (068 AC3).
public record ReworkOrder(string Id, string TenantId, string LegalEntityId, string? NonconformanceId,
    string? HoldId, string SourceOrderId, string ItemSku, decimal Qty, ReworkStatus Status,
    string? AdjustmentReason, string? AdjustmentApprovedBy, string? ReinspectionInspectionId,
    string? CompletedBy, DateTimeOffset? CompletedAt, string CreatedBy, DateTimeOffset CreatedAt);

// variance ledger (070): every unresolved close-reconciliation variance carries an owner, an
// open timestamp (age) and a disposition; close policy blocks on critical unresolved variances
// absent an approved override.
public record VarianceLedger(string Id, string TenantId, string LegalEntityId, string OrderId,
    string BucketCode, decimal QtyVariance, string Owner, string Disposition, string? Reason,
    bool IsCritical, string? DecidedBy, DateTimeOffset? DecidedAt, string CreatedBy, DateTimeOffset CreatedAt);
