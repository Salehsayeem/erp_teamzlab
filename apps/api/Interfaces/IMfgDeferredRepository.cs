using Erp.Api.Data;

namespace Erp.Api.Interfaces;

// MFG deferred screens (MF1-MF10) — every query tenant+entity scoped from EntityContext
// (R14/defense-in-depth), ids from the shared doc_ sequences, guarded state flips through
// set-helpers so concurrency tokens stay effective.
public interface IMfgDeferredRepository
{
    // MF1
    List<RoutingRevision> AllRoutings(string? outputSku, RoutingStatus? status);
    RoutingRevision? FindRouting(string routingId);
    List<RoutingRevision> LiveRoutingsFor(string outputSku);
    List<RoutingRevision> EffectiveRoutingsFor(string outputSku, DateOnly date);
    List<RoutingOperation> RoutingOperationsFor(string routingRevisionId);
    RoutingOperation? FindRoutingOperation(string routingRevisionId, int seq);
    RoutingOperation? FindRoutingOperationById(string opId);
    List<RoutingOperation> OperationsOfSubRouting(string subRoutingId);
    void AddRouting(RoutingRevision routing);
    void AddRoutingOperation(RoutingOperation op);
    void SetRoutingApproved(RoutingRevision routing, string approvedBy, DateTimeOffset approvedAt);
    void SetRoutingOperationSubRouting(RoutingOperation op, string? subRoutingId, int seq);
    WorkCenter? FindWorkCenter(string workCenterId);
    WorkCenter? FindWorkCenterByCode(string code);
    List<WorkCenter> AllWorkCenters();
    void AddWorkCenter(WorkCenter wc);
    List<ResourceCalendar> CalendarFor(string workCenterId);
    List<ResourceCalendar> AvailableWindowsFor(string workCenterId, DateOnly from, DateOnly to);
    List<ResourceCalendar> WindowsOverlapping(string workCenterId, DateTimeOffset start, DateTimeOffset end);
    void AddCalendarWindow(ResourceCalendar window);
    ResourceCalendar? FindCalendarBySourceEvent(string sourceEventId);

    // MF2 / MF3
    void AddScheduleRun(ScheduleRun run);
    ScheduleRun? FindScheduleRun(string runId);
    ScheduleRun? LatestPublishedRunForOrder(string orderId);
    List<ScheduleRun> RunsForOrder(string orderId);
    void AddPlacement(OperationPlacement placement);
    void UpdatePlacement(OperationPlacement placement, DateTimeOffset? startAt, DateTimeOffset? endAt, bool scheduled, string? reason);
    List<OperationPlacement> PlacementsFor(string scheduleRunId);
    List<OperationPlacement> PlacementsForOrder(string orderId);
    OperationPlacement? FindPlacement(string placementId);
    void AddScenario(ScheduleScenario scenario);
    ScheduleScenario? FindScenario(string scenarioId);
    void AddOverride(ScheduleOverride ovr);
    List<ScheduleOverride> OverridesFor(string scenarioId);
    void AddConflict(ConflictReport conflict);
    List<ConflictReport> ConflictsFor(string scheduleRunId);
    void ResolveConflict(ConflictReport conflict, string resolvedBy, DateTimeOffset at);
    bool AddPublication(SchedulePublication publication); // false when the run is already published
    SchedulePublication? FindPublication(string scheduleRunId);
    List<SchedulePublication> AllPublications();

    // MF4
    void AddMrpRun(MrpRun run);
    List<MrpRun> MrpRuns();
    List<MrpLine> MrpLines(string? runId);
    void SetMrpRunSnapshot(MrpRun run, string snapshotJson);
    MrpRun? FindMrpRun(string runId);
    void SetMrpRunStatus(MrpRun run, MrpRunStatus status, string? failureDetail);
    void AddMrpLine(MrpLine line);
    List<MrpLine> MrpLinesFor(string runId);
    MrpLine? FindMrpLine(string lineId);
    void SetMrpLineConversion(MrpLine line, string status, string? orderId, string? requisitionRef, string? reason);
    void AddActionMessage(ActionMessage message);
    List<ActionMessage> ActionMessagesFor(string runId);
    ActionMessage? FindActionMessage(string messageId);
    void SetActionMessageState(ActionMessage message, ActionMessageState state, string? reason, string actor);
    bool AddMrpPublication(MrpPublication publication); // false when already published (idempotent)
    MrpPublication? FindMrpPublication(string runId);

    // MF5
    void AddLaborEntry(LaborTimeEntry entry);
    LaborTimeEntry? FindLaborEntry(string entryId);
    List<LaborTimeEntry> LaborEntriesForOrder(string orderId);
    List<LaborTimeEntry> LaborEntriesForWorker(string workerEmploymentId);
    bool SetLaborApproved(LaborTimeEntry entry, string approvedBy, DateTimeOffset at); // token-guarded flip
    void AddCounterEvent(CounterEvent evt);
    CounterEvent? FindCounterEventBySource(string sourceEventId);
    CounterEvent? LatestCounterForOrder(string orderId);
    List<CounterEvent> CounterEventsForOrder(string orderId);
    void AddDowntimeEvent(DowntimeEvent evt);
    List<DowntimeEvent> DowntimeEventsForOrder(string orderId);
    List<DowntimeEvent> DowntimeForWorkCenterOverlapping(string workCenterId, DateTimeOffset start, DateTimeOffset end);
    void AddWipTransfer(WipTransfer transfer);
    List<WipTransfer> WipTransfersFor(string orderId);
    void AddCorrection(CorrectionRecord correction);
    CorrectionRecord? FindCorrection(string correctionId);
    bool SetCorrectionApproved(CorrectionRecord correction, string approvedBy, DateTimeOffset at);
    List<LaborTimeEntry> LaborCorrectionsFor(string targetEntryId);

    // MF6
    void AddCustody(SubcontractCustody custody);
    List<SubcontractCustody> CustodiesFor(string orderId);
    void AddSubcontractReceipt(SubcontractReceipt receipt);
    List<SubcontractReceipt> SubcontractReceiptsFor(string orderId);

    // MF7
    MaintenanceAvailabilityEvent? FindMaintenanceEventBySource(string sourceEventId);
    void AddMaintenanceEvent(MaintenanceAvailabilityEvent evt);
    void UpdateMaintenanceEventWindow(MaintenanceAvailabilityEvent evt, DateTimeOffset start, DateTimeOffset end, MaintenanceEventStatus status, string? exceptionReason, int revision);
    MaintenanceAvailabilityEvent? FindMaintenanceEvent(string eventId);
    List<MaintenanceAvailabilityEvent> AllMaintenanceEvents();
    void AddMaintenanceImpact(MaintenanceImpact impact);
    List<MaintenanceImpact> ImpactsFor(string outageEventId);

    // MF8
    List<QualityPlan> AllQualityPlans(string? itemSku, QualityPlanStatus? status);
    QualityPlan? FindQualityPlan(string planId);
    QualityPlan? ApprovedQualityPlanFor(string itemSku, DateOnly date);
    void AddQualityPlan(QualityPlan plan);
    void SetQualityPlanApproved(QualityPlan plan, string approvedBy, DateTimeOffset at);
    List<QualityCharacteristic> CharacteristicsFor(string planId);
    QualityCharacteristic? FindCharacteristic(string planId, string code);
    void AddCharacteristic(QualityCharacteristic characteristic);
    void AddSamplingPlan(SamplingPlan plan);
    SamplingPlan? SamplingFor(string qualityPlanId);
    void AddInspection(Inspection inspection);
    List<Inspection> Inspections();
    Inspection? FindInspection(string inspectionId);
    bool SetInspectionFinalized(Inspection inspection, string finalizedBy, DateTimeOffset at);
    void AddInspectionResult(InspectionResult result);
    List<InspectionResult> ResultsFor(string inspectionId);
    InspectionResult? FindInspectionResult(string resultId);

    // MF9
    void AddQualityHold(QualityHold hold);
    QualityHold? FindQualityHold(string holdId);
    List<QualityHold> OpenHoldsForItem(string itemSku);
    List<QualityHold> OpenHoldsForOrder(string orderId);
    List<QualityHold> AllHolds();
    bool SetHoldDispositioned(QualityHold hold, HoldDisposition disposition, decimal qty, string by, DateTimeOffset at, string? journalId);
    void AddNonconformance(Nonconformance nc);
    Nonconformance? FindNonconformance(string ncId);
    List<Nonconformance> AllNonconformances();
    bool SetNcClosed(Nonconformance nc, string closedBy, DateTimeOffset at);
    void AddNcObject(NcObject obj);
    List<NcObject> NcObjectsFor(string ncId);
    void AddContainment(ContainmentAction containment);
    ContainmentAction? FindContainment(string containmentId);
    List<ContainmentAction> ContainmentsFor(string ncId);
    bool SetContainmentReleased(ContainmentAction containment, string releasedBy, DateTimeOffset at);
    void AddCapaCase(CapaCase capa);
    CapaCase? FindCapaCase(string capaId);
    List<CapaCase> AllCapaCases();
    bool SetCapaClosed(CapaCase capa, string closedBy, DateTimeOffset at);
    void AddCapaAction(CapaAction action);
    CapaAction? FindCapaAction(string actionId);
    List<CapaAction> CapaActionsFor(string capaId);
    void SetCapaActionDone(CapaAction action, string by, DateTimeOffset at);
    void AddCapaVerification(CapaVerification verification);
    List<CapaVerification> CapaVerificationsFor(string capaId);

    // MF10
    void AddTransformationEvent(TransformationEvent evt);
    TransformationEvent? FindTransformationEventByCompletion(string completionId);
    List<TransformationEvent> TransformationEventsFor(string? lotSerial, string? outputSku);
    List<TransformationEvent> AllTransformationEvents();
    void AddReworkOrder(ReworkOrder rework);
    ReworkOrder? FindReworkOrder(string reworkId);
    List<ReworkOrder> AllReworkOrders();
    bool SetReworkStatus(ReworkOrder rework, ReworkStatus status, string by, DateTimeOffset at);
    void SetReworkReinspection(ReworkOrder rework, string inspectionId);
    void SetReworkAdjustment(ReworkOrder rework, string reason, string approvedBy);
    void SetReworkCompleted(ReworkOrder rework, string by, DateTimeOffset at);
    void AddVariance(VarianceLedger variance);
    VarianceLedger? FindVariance(string varianceId);
    List<VarianceLedger> VariancesFor(string orderId);
    void SetVarianceDisposition(VarianceLedger variance, string disposition, string reason, string by, DateTimeOffset at);

    // shared configuration (FinanceSetting-backed profiles)
    string? Setting(string key);
    int Save();
}
