using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// MFG deferred screens — tenant+entity scope on every query via the EntityContext-filtered
// helpers; set-helpers carry the guarded state flips so EF concurrency tokens do the race
// backstop (a stale second actor updates ZERO rows).
public sealed class MfgDeferredRepository(ErpDbContext db, EntityContext ec) : IMfgDeferredRepository
{
    private IQueryable<T> Sc<T>(System.Linq.Expressions.Expression<Func<T, bool>>? filter = null) where T : class
    {
        var q = db.Set<T>().Where(e => EF.Property<string>(e, "TenantId") == ec.TenantId
            && EF.Property<string>(e, "LegalEntityId") == ec.EntityId);
        return filter is null ? q : q.Where(filter);
    }

    private void Set<T>(T entity, string prop, object? value) where T : class =>
        db.Entry(entity).Property(prop).CurrentValue = value;

    public int Save() => db.SaveChanges();
    public string? Setting(string key) =>
        db.FinanceSettings.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Key == key)
            .OrderBy(s => s.Id).FirstOrDefault()?.Value;

    // ---- MF1 ----
    public List<RoutingRevision> AllRoutings(string? outputSku, RoutingStatus? status)
    {
        var q = Sc<RoutingRevision>();
        if (outputSku is not null) q = q.Where(r => r.OutputSku == outputSku);
        if (status is not null) q = q.Where(r => r.Status == status);
        return q.OrderBy(r => r.OutputSku).ThenBy(r => r.Revision).ToList();
    }
    public RoutingRevision? FindRouting(string routingId) => Sc<RoutingRevision>().FirstOrDefault(r => r.Id == routingId);
    public List<RoutingRevision> LiveRoutingsFor(string outputSku) =>
        Sc<RoutingRevision>().Where(r => r.OutputSku == outputSku && r.Status != RoutingStatus.Retired).ToList();
    public List<RoutingRevision> EffectiveRoutingsFor(string outputSku, DateOnly date) =>
        Sc<RoutingRevision>().Where(r => r.OutputSku == outputSku && r.Status == RoutingStatus.Approved
                && r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date))
            .OrderBy(r => r.Revision).ToList();
    public List<RoutingOperation> RoutingOperationsFor(string routingRevisionId) =>
        Sc<RoutingOperation>().Where(o => o.RoutingRevisionId == routingRevisionId).OrderBy(o => o.Seq).ToList();
    public RoutingOperation? FindRoutingOperation(string routingRevisionId, int seq) =>
        Sc<RoutingOperation>().FirstOrDefault(o => o.RoutingRevisionId == routingRevisionId && o.Seq == seq);
    public RoutingOperation? FindRoutingOperationById(string opId) => Sc<RoutingOperation>().FirstOrDefault(o => o.Id == opId);
    public List<RoutingOperation> OperationsOfSubRouting(string subRoutingId) =>
        Sc<RoutingOperation>().Where(o => o.SubRoutingId == subRoutingId).ToList();
    public void AddRouting(RoutingRevision routing) => db.RoutingRevisions.Add(routing);
    public void AddRoutingOperation(RoutingOperation op) => db.RoutingOperations.Add(op);
    public void SetRoutingApproved(RoutingRevision routing, string approvedBy, DateTimeOffset approvedAt)
    {
        Set(routing, nameof(RoutingRevision.Status), RoutingStatus.Approved);
        Set(routing, nameof(RoutingRevision.ApprovedBy), approvedBy);
        Set(routing, nameof(RoutingRevision.ApprovedAt), approvedAt);
    }
    public void SetRoutingOperationSubRouting(RoutingOperation op, string? subRoutingId, int seq)
    {
        Set(op, nameof(RoutingOperation.SubRoutingId), subRoutingId);
        Set(op, nameof(RoutingOperation.Seq), seq);
    }

    public WorkCenter? FindWorkCenter(string workCenterId) => Sc<WorkCenter>().FirstOrDefault(w => w.Id == workCenterId);
    public WorkCenter? FindWorkCenterByCode(string code) => Sc<WorkCenter>().FirstOrDefault(w => w.Code == code);
    public List<WorkCenter> AllWorkCenters() => Sc<WorkCenter>().OrderBy(w => w.Code).ToList();
    public void AddWorkCenter(WorkCenter wc) => db.WorkCenters.Add(wc);
    public List<ResourceCalendar> CalendarFor(string workCenterId) =>
        Sc<ResourceCalendar>().Where(c => c.WorkCenterId == workCenterId).OrderBy(c => c.WindowStart).ToList();
    public List<ResourceCalendar> AvailableWindowsFor(string workCenterId, DateOnly from, DateOnly to) =>
        Sc<ResourceCalendar>().Where(c => c.WorkCenterId == workCenterId && c.IsAvailable
                && c.WindowStart >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero)
                && c.WindowStart <= new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero))
            .OrderBy(c => c.WindowStart).ToList();
    public List<ResourceCalendar> WindowsOverlapping(string workCenterId, DateTimeOffset start, DateTimeOffset end) =>
        Sc<ResourceCalendar>().Where(c => c.WorkCenterId == workCenterId
                && c.WindowStart < end && c.WindowEnd > start).ToList();
    public void AddCalendarWindow(ResourceCalendar window) => db.ResourceCalendars.Add(window);
    public ResourceCalendar? FindCalendarBySourceEvent(string sourceEventId) =>
        Sc<ResourceCalendar>().FirstOrDefault(c => c.SourceEventId == sourceEventId);

    // ---- MF2 / MF3 ----
    public void AddScheduleRun(ScheduleRun run) => db.ScheduleRuns.Add(run);
    public ScheduleRun? FindScheduleRun(string runId) => Sc<ScheduleRun>().FirstOrDefault(r => r.Id == runId);
    public ScheduleRun? LatestPublishedRunForOrder(string orderId) =>
        Sc<SchedulePublication>().OrderByDescending(p => p.At).ToList()
            .Select(p => FindScheduleRun(p.ScheduleRunId))
            .FirstOrDefault(r => r is not null && r.OrderId == orderId);
    public List<ScheduleRun> RunsForOrder(string orderId) =>
        Sc<ScheduleRun>().Where(r => r.OrderId == orderId).OrderBy(r => r.CreatedAt).ToList();
    public void AddPlacement(OperationPlacement placement) => db.OperationPlacements.Add(placement);
    public void UpdatePlacement(OperationPlacement placement, DateTimeOffset? startAt, DateTimeOffset? endAt, bool scheduled, string? reason)
    {
        Set(placement, nameof(OperationPlacement.StartAt), startAt);
        Set(placement, nameof(OperationPlacement.EndAt), endAt);
        Set(placement, nameof(OperationPlacement.Scheduled), scheduled);
        Set(placement, nameof(OperationPlacement.UnscheduledReason), reason);
    }
    public List<OperationPlacement> PlacementsFor(string scheduleRunId) =>
        Sc<OperationPlacement>().Where(p => p.ScheduleRunId == scheduleRunId).OrderBy(p => p.Seq).ToList();
    public List<OperationPlacement> PlacementsForOrder(string orderId) =>
        Sc<OperationPlacement>().Where(p => p.OrderId == orderId).ToList();
    public OperationPlacement? FindPlacement(string placementId) => Sc<OperationPlacement>().FirstOrDefault(p => p.Id == placementId);
    public void AddScenario(ScheduleScenario scenario) => db.ScheduleScenarios.Add(scenario);
    public ScheduleScenario? FindScenario(string scenarioId) => Sc<ScheduleScenario>().FirstOrDefault(s => s.Id == scenarioId);
    public void AddOverride(ScheduleOverride ovr) => db.ScheduleOverrides.Add(ovr);
    public List<ScheduleOverride> OverridesFor(string scenarioId) =>
        Sc<ScheduleOverride>().Where(o => o.ScenarioId == scenarioId).OrderBy(o => o.At).ToList();
    public void AddConflict(ConflictReport conflict) => db.ConflictReports.Add(conflict);
    public List<ConflictReport> ConflictsFor(string scheduleRunId) =>
        Sc<ConflictReport>().Where(c => c.ScheduleRunId == scheduleRunId).OrderBy(c => c.Code).ToList();
    public void ResolveConflict(ConflictReport conflict, string resolvedBy, DateTimeOffset at)
    {
        Set(conflict, nameof(ConflictReport.Resolved), true);
        Set(conflict, nameof(ConflictReport.ResolvedBy), resolvedBy);
        Set(conflict, nameof(ConflictReport.ResolvedAt), at);
    }
    public bool AddPublication(SchedulePublication publication)
    {
        if (FindPublication(publication.ScheduleRunId) is not null) return false;
        db.SchedulePublications.Add(publication);
        return true;
    }
    public SchedulePublication? FindPublication(string scheduleRunId) =>
        Sc<SchedulePublication>().FirstOrDefault(p => p.ScheduleRunId == scheduleRunId);
    public List<SchedulePublication> AllPublications() =>
        Sc<SchedulePublication>().OrderByDescending(p => p.At).ToList();

    // ---- MF4 ----
    public void AddMrpRun(MrpRun run) => db.MrpRuns.Add(run);
    public void SetMrpRunSnapshot(MrpRun run, string snapshotJson) =>
        Set(run, nameof(MrpRun.SnapshotJson), snapshotJson);
    public MrpRun? FindMrpRun(string runId) => Sc<MrpRun>().FirstOrDefault(r => r.Id == runId);
    public void SetMrpRunStatus(MrpRun run, MrpRunStatus status, string? failureDetail)
    {
        Set(run, nameof(MrpRun.Status), status);
        Set(run, nameof(MrpRun.FailureDetail), failureDetail);
    }
    public void AddMrpLine(MrpLine line) => db.MrpLines.Add(line);
    public List<MrpLine> MrpLinesFor(string runId) =>
        Sc<MrpLine>().Where(l => l.RunId == runId).OrderBy(l => l.LineNo).ToList();
    public MrpLine? FindMrpLine(string lineId) => Sc<MrpLine>().FirstOrDefault(l => l.Id == lineId);
    public void SetMrpLineConversion(MrpLine line, string status, string? orderId, string? requisitionRef, string? reason)
    {
        Set(line, nameof(MrpLine.ConversionStatus), status);
        Set(line, nameof(MrpLine.ConvertedOrderId), orderId);
        Set(line, nameof(MrpLine.ConvertedRequisitionRef), requisitionRef);
        Set(line, nameof(MrpLine.DecisionReason), reason);
    }
    public void AddActionMessage(ActionMessage message) => db.ActionMessages.Add(message);
    public List<ActionMessage> ActionMessagesFor(string runId) =>
        Sc<ActionMessage>().Where(m => m.RunId == runId).OrderBy(m => m.Code).ToList();
    public ActionMessage? FindActionMessage(string messageId) => Sc<ActionMessage>().FirstOrDefault(m => m.Id == messageId);
    public void SetActionMessageState(ActionMessage message, ActionMessageState state, string? reason, string actor)
    {
        Set(message, nameof(ActionMessage.State), state);
        Set(message, nameof(ActionMessage.DismissReason), reason);
        if (state == ActionMessageState.Acknowledged)
        {
            Set(message, nameof(ActionMessage.AcknowledgedBy), actor);
            Set(message, nameof(ActionMessage.AcknowledgedAt), DateTimeOffset.UtcNow);
        }
    }
    public bool AddMrpPublication(MrpPublication publication)
    {
        if (FindMrpPublication(publication.RunId) is not null) return false;
        db.MrpPublications.Add(publication);
        return true;
    }
    public MrpPublication? FindMrpPublication(string runId) => Sc<MrpPublication>().FirstOrDefault(p => p.RunId == runId);

    // ---- MF5 ----
    public void AddLaborEntry(LaborTimeEntry entry) => db.LaborTimeEntries.Add(entry);
    public LaborTimeEntry? FindLaborEntry(string entryId) => Sc<LaborTimeEntry>().FirstOrDefault(e => e.Id == entryId);
    public List<LaborTimeEntry> LaborEntriesForOrder(string orderId) =>
        Sc<LaborTimeEntry>().Where(e => e.OrderId == orderId).OrderBy(e => e.StartAt).ToList();
    public List<LaborTimeEntry> LaborEntriesForWorker(string workerEmploymentId) =>
        Sc<LaborTimeEntry>().Where(e => e.WorkerEmploymentId == workerEmploymentId).ToList();
    public bool SetLaborApproved(LaborTimeEntry entry, string approvedBy, DateTimeOffset at)
    {
        if (entry.Status != LaborEntryStatus.Reported) return false;
        Set(entry, nameof(LaborTimeEntry.Status), LaborEntryStatus.Approved);
        Set(entry, nameof(LaborTimeEntry.ApprovedBy), approvedBy);
        Set(entry, nameof(LaborTimeEntry.ApprovedAt), at);
        return true;
    }
    public void AddCounterEvent(CounterEvent evt) => db.CounterEvents.Add(evt);
    public CounterEvent? FindCounterEventBySource(string sourceEventId) =>
        Sc<CounterEvent>().FirstOrDefault(e => e.SourceEventId == sourceEventId);
    public CounterEvent? LatestCounterForOrder(string orderId) =>
        Sc<CounterEvent>().Where(e => e.OrderId == orderId && e.CorrectionOfId == null)
            .OrderByDescending(e => e.At).FirstOrDefault();
    public List<CounterEvent> CounterEventsForOrder(string orderId) =>
        Sc<CounterEvent>().Where(e => e.OrderId == orderId).OrderBy(e => e.At).ToList();
    public void AddDowntimeEvent(DowntimeEvent evt) => db.DowntimeEvents.Add(evt);
    public List<DowntimeEvent> DowntimeEventsForOrder(string orderId) =>
        Sc<DowntimeEvent>().Where(e => e.OrderId == orderId).OrderBy(e => e.StartAt).ToList();
    public List<DowntimeEvent> DowntimeForWorkCenterOverlapping(string workCenterId, DateTimeOffset start, DateTimeOffset end) =>
        Sc<DowntimeEvent>().Where(e => e.WorkCenterId == workCenterId && e.StartAt < end && e.EndAt > start).ToList();
    public void AddWipTransfer(WipTransfer transfer) => db.WipTransfers.Add(transfer);
    public List<WipTransfer> WipTransfersFor(string orderId) =>
        Sc<WipTransfer>().Where(t => t.OrderId == orderId).OrderBy(t => t.CreatedAt).ToList();
    public void AddCorrection(CorrectionRecord correction) => db.CorrectionRecords.Add(correction);
    public CorrectionRecord? FindCorrection(string correctionId) => Sc<CorrectionRecord>().FirstOrDefault(c => c.Id == correctionId);
    public bool SetCorrectionApproved(CorrectionRecord correction, string approvedBy, DateTimeOffset at)
    {
        if (correction.Status != "Proposed") return false;
        Set(correction, nameof(CorrectionRecord.Status), "Approved");
        Set(correction, nameof(CorrectionRecord.ApprovedBy), approvedBy);
        Set(correction, nameof(CorrectionRecord.ApprovedAt), at);
        return true;
    }
    public List<LaborTimeEntry> LaborCorrectionsFor(string targetEntryId) =>
        Sc<LaborTimeEntry>().Where(e => e.CorrectionOfId == targetEntryId).ToList();

    // ---- MF6 ----
    public void AddCustody(SubcontractCustody custody) => db.SubcontractCustodies.Add(custody);
    public List<SubcontractCustody> CustodiesFor(string orderId) =>
        Sc<SubcontractCustody>().Where(c => c.OrderId == orderId).OrderBy(c => c.CreatedAt).ToList();
    public void AddSubcontractReceipt(SubcontractReceipt receipt) => db.SubcontractReceipts.Add(receipt);
    public List<SubcontractReceipt> SubcontractReceiptsFor(string orderId) =>
        Sc<SubcontractReceipt>().Where(r => r.OrderId == orderId).OrderBy(r => r.CreatedAt).ToList();

    // ---- MF7 ----
    public MaintenanceAvailabilityEvent? FindMaintenanceEventBySource(string sourceEventId) =>
        Sc<MaintenanceAvailabilityEvent>().FirstOrDefault(e => e.SourceEventId == sourceEventId);
    public void AddMaintenanceEvent(MaintenanceAvailabilityEvent evt) => db.MaintenanceAvailabilityEvents.Add(evt);
    public void UpdateMaintenanceEventWindow(MaintenanceAvailabilityEvent evt, DateTimeOffset start, DateTimeOffset end,
        MaintenanceEventStatus status, string? exceptionReason, int revision)
    {
        Set(evt, nameof(MaintenanceAvailabilityEvent.WindowStart), start);
        Set(evt, nameof(MaintenanceAvailabilityEvent.WindowEnd), end);
        Set(evt, nameof(MaintenanceAvailabilityEvent.Status), status);
        Set(evt, nameof(MaintenanceAvailabilityEvent.ExceptionReason), exceptionReason);
        Set(evt, nameof(MaintenanceAvailabilityEvent.Revision), revision);
    }
    public MaintenanceAvailabilityEvent? FindMaintenanceEvent(string eventId) =>
        Sc<MaintenanceAvailabilityEvent>().FirstOrDefault(e => e.Id == eventId);
    public List<MaintenanceAvailabilityEvent> AllMaintenanceEvents() =>
        Sc<MaintenanceAvailabilityEvent>().OrderBy(e => e.WindowStart).ToList();
    public void AddMaintenanceImpact(MaintenanceImpact impact) => db.MaintenanceImpacts.Add(impact);
    public List<MaintenanceImpact> ImpactsFor(string outageEventId) =>
        Sc<MaintenanceImpact>().Where(i => i.OutageEventId == outageEventId).OrderBy(i => i.Id).ToList();

    // ---- MF8 ----
    public List<QualityPlan> AllQualityPlans(string? itemSku, QualityPlanStatus? status)
    {
        var q = Sc<QualityPlan>();
        if (itemSku is not null) q = q.Where(p => p.ItemSku == itemSku);
        if (status is not null) q = q.Where(p => p.Status == status);
        return q.OrderBy(p => p.ItemSku).ThenBy(p => p.Revision).ToList();
    }
    public QualityPlan? FindQualityPlan(string planId) => Sc<QualityPlan>().FirstOrDefault(p => p.Id == planId);
    public QualityPlan? ApprovedQualityPlanFor(string itemSku, DateOnly date) =>
        Sc<QualityPlan>().FirstOrDefault(p => p.ItemSku == itemSku && p.Status == QualityPlanStatus.Approved
                && p.EffectiveFrom <= date && (p.EffectiveTo == null || p.EffectiveTo >= date));
    public void AddQualityPlan(QualityPlan plan) => db.QualityPlans.Add(plan);
    public void SetQualityPlanApproved(QualityPlan plan, string approvedBy, DateTimeOffset at)
    {
        Set(plan, nameof(QualityPlan.Status), QualityPlanStatus.Approved);
        Set(plan, nameof(QualityPlan.ApprovedBy), approvedBy);
        Set(plan, nameof(QualityPlan.ApprovedAt), at);
    }
    public List<QualityCharacteristic> CharacteristicsFor(string planId) =>
        Sc<QualityCharacteristic>().Where(c => c.PlanId == planId).OrderBy(c => c.Code).ToList();
    public QualityCharacteristic? FindCharacteristic(string planId, string code) =>
        Sc<QualityCharacteristic>().FirstOrDefault(c => c.PlanId == planId && c.Code == code);
    public void AddCharacteristic(QualityCharacteristic characteristic) => db.QualityCharacteristics.Add(characteristic);
    public void AddSamplingPlan(SamplingPlan plan) => db.SamplingPlans.Add(plan);
    public SamplingPlan? SamplingFor(string qualityPlanId) => Sc<SamplingPlan>().FirstOrDefault(s => s.QualityPlanId == qualityPlanId);
    public void AddInspection(Inspection inspection) => db.Inspections.Add(inspection);
    public Inspection? FindInspection(string inspectionId) => Sc<Inspection>().FirstOrDefault(i => i.Id == inspectionId);
    public bool SetInspectionFinalized(Inspection inspection, string finalizedBy, DateTimeOffset at)
    {
        if (inspection.Status != QualityInspectionStatus.InProgress) return false;
        Set(inspection, nameof(Inspection.Status), QualityInspectionStatus.Finalized);
        Set(inspection, nameof(Inspection.FinalizedBy), finalizedBy);
        Set(inspection, nameof(Inspection.FinalizedAt), at);
        return true;
    }
    public void AddInspectionResult(InspectionResult result) => db.InspectionResults.Add(result);
    public List<InspectionResult> ResultsFor(string inspectionId) =>
        Sc<InspectionResult>().Where(r => r.InspectionId == inspectionId).OrderBy(r => r.AmendmentNo).ToList();
    public InspectionResult? FindInspectionResult(string resultId) => Sc<InspectionResult>().FirstOrDefault(r => r.Id == resultId);

    // ---- MF9 ----
    public void AddQualityHold(QualityHold hold) => db.QualityHolds.Add(hold);
    public QualityHold? FindQualityHold(string holdId) => Sc<QualityHold>().FirstOrDefault(h => h.Id == holdId);
    public List<QualityHold> OpenHoldsForItem(string itemSku) =>
        Sc<QualityHold>().Where(h => h.ItemSku == itemSku && h.Status == QualityHoldStatus.Open).ToList();
    public List<QualityHold> OpenHoldsForOrder(string orderId) =>
        Sc<QualityHold>().Where(h => h.OrderId == orderId && h.Status == QualityHoldStatus.Open).ToList();
    public List<QualityHold> AllHolds() => Sc<QualityHold>().OrderByDescending(h => h.HeldAt).ToList();
    public bool SetHoldDispositioned(QualityHold hold, HoldDisposition disposition, decimal qty, string by, DateTimeOffset at, string? journalId)
    {
        if (hold.Status != QualityHoldStatus.Open) return false;
        Set(hold, nameof(QualityHold.Status), QualityHoldStatus.Dispositioned);
        Set(hold, nameof(QualityHold.Disposition), disposition);
        Set(hold, nameof(QualityHold.DispositionedQty), qty);
        Set(hold, nameof(QualityHold.DispositionedBy), by);
        Set(hold, nameof(QualityHold.DispositionedAt), at);
        Set(hold, nameof(QualityHold.JournalId), journalId);
        return true;
    }
    public void AddNonconformance(Nonconformance nc) => db.Nonconformances.Add(nc);
    public Nonconformance? FindNonconformance(string ncId) => Sc<Nonconformance>().FirstOrDefault(n => n.Id == ncId);
    public List<Nonconformance> AllNonconformances() => Sc<Nonconformance>().OrderByDescending(n => n.RaisedAt).ToList();
    public bool SetNcClosed(Nonconformance nc, string closedBy, DateTimeOffset at)
    {
        if (nc.Status != NcStatus.Open) return false;
        Set(nc, nameof(Nonconformance.Status), NcStatus.Closed);
        Set(nc, nameof(Nonconformance.ClosedBy), closedBy);
        Set(nc, nameof(Nonconformance.ClosedAt), at);
        return true;
    }
    public void AddNcObject(NcObject obj) => db.NcObjects.Add(obj);
    public List<NcObject> NcObjectsFor(string ncId) => Sc<NcObject>().Where(o => o.NonconformanceId == ncId).ToList();
    public void AddContainment(ContainmentAction containment) => db.ContainmentActions.Add(containment);
    public ContainmentAction? FindContainment(string containmentId) => Sc<ContainmentAction>().FirstOrDefault(c => c.Id == containmentId);
    public List<ContainmentAction> ContainmentsFor(string ncId) =>
        Sc<ContainmentAction>().Where(c => c.NonconformanceId == ncId).OrderBy(c => c.Id).ToList();
    public bool SetContainmentReleased(ContainmentAction containment, string releasedBy, DateTimeOffset at)
    {
        if (containment.Status != "Open") return false;
        Set(containment, nameof(ContainmentAction.Status), "Released");
        Set(containment, nameof(ContainmentAction.ReleasedBy), releasedBy);
        Set(containment, nameof(ContainmentAction.ReleasedAt), at);
        return true;
    }
    public void AddCapaCase(CapaCase capa) => db.CapaCases.Add(capa);
    public CapaCase? FindCapaCase(string capaId) => Sc<CapaCase>().FirstOrDefault(c => c.Id == capaId);
    public List<CapaCase> AllCapaCases() => Sc<CapaCase>().OrderByDescending(c => c.CreatedAt).ToList();
    public bool SetCapaClosed(CapaCase capa, string closedBy, DateTimeOffset at)
    {
        if (capa.Status != CapaStatus.Open) return false;
        Set(capa, nameof(CapaCase.Status), CapaStatus.Closed);
        Set(capa, nameof(CapaCase.ClosedBy), closedBy);
        Set(capa, nameof(CapaCase.ClosedAt), at);
        return true;
    }
    public void AddCapaAction(CapaAction action) => db.CapaActions.Add(action);
    public CapaAction? FindCapaAction(string actionId) => Sc<CapaAction>().FirstOrDefault(a => a.Id == actionId);
    public List<CapaAction> CapaActionsFor(string capaId) =>
        Sc<CapaAction>().Where(a => a.CapaCaseId == capaId).OrderBy(a => a.Id).ToList();
    public void SetCapaActionDone(CapaAction action, string by, DateTimeOffset at)
    {
        Set(action, nameof(CapaAction.Done), true);
        Set(action, nameof(CapaAction.CompletedBy), by);
        Set(action, nameof(CapaAction.CompletedAt), at);
    }
    public void AddCapaVerification(CapaVerification verification) => db.CapaVerifications.Add(verification);
    public List<CapaVerification> CapaVerificationsFor(string capaId) =>
        Sc<CapaVerification>().Where(v => v.CapaCaseId == capaId).OrderBy(v => v.At).ToList();

    // ---- MF10 ----
    public void AddTransformationEvent(TransformationEvent evt) => db.TransformationEvents.Add(evt);
    public TransformationEvent? FindTransformationEventByCompletion(string completionId) =>
        Sc<TransformationEvent>().FirstOrDefault(e => e.CompletionId == completionId);
    public List<TransformationEvent> TransformationEventsFor(string? lotSerial, string? outputSku) =>
        Sc<TransformationEvent>().Where(e =>
                (lotSerial != null && e.LotSerial == lotSerial)
                || (outputSku != null && e.OutputSku == outputSku))
            .OrderBy(e => e.At).ToList();
    public List<TransformationEvent> AllTransformationEvents() => Sc<TransformationEvent>().OrderBy(e => e.At).ToList();
    public void AddReworkOrder(ReworkOrder rework) => db.ReworkOrders.Add(rework);
    public ReworkOrder? FindReworkOrder(string reworkId) => Sc<ReworkOrder>().FirstOrDefault(r => r.Id == reworkId);
    public List<ReworkOrder> AllReworkOrders() => Sc<ReworkOrder>().OrderByDescending(r => r.CreatedAt).ToList();
    public bool SetReworkStatus(ReworkOrder rework, ReworkStatus status, string by, DateTimeOffset at)
    {
        if ((int)status != (int)rework.Status + 1) return false; // Draft→Approved→Completed only
        Set(rework, nameof(ReworkOrder.Status), status);
        if (status == ReworkStatus.Completed)
        {
            Set(rework, nameof(ReworkOrder.CompletedBy), by);
            Set(rework, nameof(ReworkOrder.CompletedAt), at);
        }
        return true;
    }
    public void SetReworkReinspection(ReworkOrder rework, string inspectionId) =>
        Set(rework, nameof(ReworkOrder.ReinspectionInspectionId), inspectionId);
    public void SetReworkAdjustment(ReworkOrder rework, string reason, string approvedBy)
    {
        Set(rework, nameof(ReworkOrder.AdjustmentReason), reason);
        Set(rework, nameof(ReworkOrder.AdjustmentApprovedBy), approvedBy);
    }
    public void SetReworkCompleted(ReworkOrder rework, string by, DateTimeOffset at)
    {
        Set(rework, nameof(ReworkOrder.Status), ReworkStatus.Completed);
        Set(rework, nameof(ReworkOrder.CompletedBy), by);
        Set(rework, nameof(ReworkOrder.CompletedAt), at);
    }
    public void AddVariance(VarianceLedger variance) => db.VarianceLedger.Add(variance);
    public VarianceLedger? FindVariance(string varianceId) => Sc<VarianceLedger>().FirstOrDefault(v => v.Id == varianceId);
    public List<VarianceLedger> VariancesFor(string orderId) =>
        Sc<VarianceLedger>().Where(v => v.OrderId == orderId).OrderBy(v => v.BucketCode).ToList();
    public void SetVarianceDisposition(VarianceLedger variance, string disposition, string reason, string by, DateTimeOffset at)
    {
        Set(variance, nameof(VarianceLedger.Disposition), disposition);
        Set(variance, nameof(VarianceLedger.Reason), reason);
        Set(variance, nameof(VarianceLedger.DecidedBy), by);
        Set(variance, nameof(VarianceLedger.DecidedAt), at);
    }
}
