namespace Erp.Api.Interfaces;

// MFG deferred screens — service contracts. Tuple convention matches the landed slices:
// (data, code, message) with one envelope code family (404 *_NOT_FOUND, 400 VALIDATION_ERROR,
// 409 state conflicts).

// Q-MF1 binding: MF4 buy-side recommendations convert into PROCUREMENT requisitions through this
// sink — the interface is the deliverable; the stub below keeps the wire live until the
// PROCUREMENT slice binds the real requisition creation (POST /api/v1/requisitions with
// source=mrp:{runId} idempotency). When bound, the sink implementation carries the PROC guards.
public interface IRequisitionSink
{
    (string Ref, string? Error) CreateForMrpLine(CurrentUser user, MrpLine line, string note);
}

public interface IRoutingService
{
    object List(string? outputSku, RoutingStatus? status);
    (RoutingRevision routing, string? code, string? message) Create(CurrentUser user, Dtos.CreateRoutingRequest request);
    (object data, string? code, string? message) Approve(CurrentUser approver, string routingId, string? reason);
    (object data, string? code, string? message) AddOperation(CurrentUser user, string routingId, Dtos.AddRoutingOperationRequest request);
    (object data, string? code, string? message) MoveOperation(CurrentUser user, string routingId, string opId, Dtos.MoveRoutingOperationRequest request);
    object Operations(string routingId);
    (WorkCenter workCenter, string? code, string? message) CreateWorkCenter(CurrentUser user, Dtos.CreateWorkCenterRequest request);
    object ListWorkCenters();
    (ResourceCalendar window, string? code, string? message) AddCalendarWindow(CurrentUser user, string workCenterId, Dtos.AddCalendarWindowRequest request);
    object Calendar(string workCenterId);
}

public interface ISchedulingService
{
    object RunsForOrder(string? orderId);
    (object data, string? code, string? message) CreateRun(CurrentUser user, Dtos.CreateScheduleRunRequest request);
    object Placements(string runId);
    object Conflicts(string runId);
    (object data, string? code, string? message) Publish(CurrentUser reviewer, string runId, Dtos.SchedulePublicationRequest request);
    (object data, string? code, string? message) CreateScenario(CurrentUser user, Dtos.CreateScenarioRequest request);
    (object data, string? code, string? message) MovePlacement(CurrentUser user, string scenarioId, Dtos.PlacementMoveRequest request);
    object Overrides(string scenarioId);
    object DispatchList(CurrentUser user, string? workCenterId);
    object CapacityLoad(string bucket);
}

public interface IMrpService
{
    (object data, string? code, string? message) CreateRun(CurrentUser user, Dtos.CreateMrpRunRequest request);
    object Runs();
    object Lines(string? runId);
    object Recommendations(string runId);
    object ActionMessages(string runId);
    (object data, string? code, string? message) Convert(CurrentUser user, string lineId, Dtos.MrpConversionRequest request);
    (object data, string? code, string? message) DecideLine(CurrentUser user, string lineId, Dtos.MrpLineDecisionRequest request);
    (object data, string? code, string? message) Publish(CurrentUser reviewer, string runId, Dtos.MrpPublicationRequest request);
}

public interface IShopFloorService
{
    object LaborEntries(string orderId);
    object CounterEvents(string orderId);
    object DowntimeEvents(string orderId);
    object WipTransfers(string orderId);
    object Corrections();
    (object data, string? code, string? message) ReportLabor(CurrentUser user, string orderId, Dtos.LaborTimeRequest request);
    (object data, string? code, string? message) ApproveLabor(CurrentUser approver, string orderId, string entryId);
    (object data, string? code, string? message) ReportCounter(CurrentUser user, string orderId, Dtos.CounterEventRequest request);
    (object data, string? code, string? message) ReportDowntime(CurrentUser user, string orderId, Dtos.DowntimeEventRequest request);
    (object data, string? code, string? message) WipProjection(string orderId);
    (object data, string? code, string? message) TransferWip(CurrentUser user, string orderId, Dtos.WipTransferRequest request);
    (object data, string? code, string? message) ProposeCorrection(CurrentUser user, Dtos.CorrectionRequest request);
    (object data, string? code, string? message) ApproveCorrection(CurrentUser approver, string correctionId, Dtos.CorrectionApprovalRequest request);
}

public interface ISubcontractService
{
    (object data, string? code, string? message) AddOperation(CurrentUser user, string routingId, Dtos.AddSubcontractOperationRequest request);
    (object data, string? code, string? message) RecordCustody(CurrentUser user, Dtos.CustodyRequest request);
    object Custodies(string orderId);
    object Receipts(string orderId);
    (object data, string? code, string? message) Receive(CurrentUser user, string orderId, Dtos.SubcontractReceiptRequest request);
}

public interface IMaintenanceService
{
    (object data, string? code, string? message) Ingest(CurrentUser user, Dtos.MaintenanceEventRequest request);
    (object data, string? code, string? message) Impacts(string outageEventId);
    (object data, string? code, string? message) ResolveImpact(CurrentUser user, Dtos.MaintenanceResolutionRequest request);
}

public interface IQualityService
{
    // MF8
    object ListPlans(string? itemSku, QualityPlanStatus? status);
    (QualityPlan plan, string? code, string? message) CreatePlan(CurrentUser user, Dtos.CreateQualityPlanRequest request);
    (object data, string? code, string? message) ApprovePlan(CurrentUser approver, string planId, string? reason);
    (object data, string? code, string? message) AddCharacteristic(CurrentUser user, string planId, Dtos.AddCharacteristicRequest request);
    (object data, string? code, string? message) AddSamplingPlan(CurrentUser user, string planId, Dtos.CreateSamplingPlanRequest request);
    (object data, string? code, string? message) CreateInspection(CurrentUser inspector, Dtos.CreateQualityInspectionRequest request);
    (object data, string? code, string? message) AddResult(CurrentUser inspector, string inspectionId, Dtos.AddInspectionResultRequest request);
    (object data, string? code, string? message) AmendResult(CurrentUser inspector, string inspectionId, string resultId, Dtos.AmendResultRequest request);
    (object data, string? code, string? message) Finalize(CurrentUser inspector, string inspectionId, Dtos.FinalizeInspectionRequest request);
    object Results(string inspectionId);
    object Inspections();
    (object data, string? code, string? message) CapaActions(string capaId);
    // MF9
    (object data, string? code, string? message) Hold(CurrentUser user, Dtos.CreateQualityHoldRequest request);
    (object data, string? code, string? message) DisposeHold(CurrentUser disposer, string holdId, Dtos.HoldDispositionRequest request);
    object Holds();
    (object data, string? code, string? message) RaiseNc(CurrentUser user, Dtos.CreateNcRequest request);
    (object data, string? code, string? message) AddContainment(CurrentUser user, string ncId, Dtos.CreateContainmentRequest request);
    (object data, string? code, string? message) ReleaseContainment(CurrentUser reviewer, string containmentId, Dtos.ReleaseContainmentRequest request);
    (object data, string? code, string? message) CloseNc(CurrentUser reviewer, string ncId);
    object ListNcs();
    (object data, string? code, string? message) CreateCapa(CurrentUser user, Dtos.CreateCapaRequest request);
    (object data, string? code, string? message) AddCapaAction(CurrentUser user, string capaId, Dtos.AddCapaActionRequest request);
    (object data, string? code, string? message) CompleteCapaAction(CurrentUser user, string actionId, Dtos.CompleteCapaActionRequest request);
    (object data, string? code, string? message) VerifyCapa(CurrentUser user, string capaId, Dtos.CapaVerificationRequest request);
    (object data, string? code, string? message) CloseCapa(CurrentUser closer, string capaId);
    object ListCapas();
    object Dashboard();
}

public interface IGenealogyService
{
    object Trace(string? lotSerial, string? outputSku);
    (object data, string? code, string? message) CreateRework(CurrentUser user, Dtos.CreateReworkRequest request);
    (object data, string? code, string? message) ApproveRework(CurrentUser approver, string reworkId, Dtos.ReworkApprovalRequest request);
    (object data, string? code, string? message) LinkReinspection(CurrentUser user, string reworkId, Dtos.ReworkReinspectionRequest request);
    (object data, string? code, string? message) CompleteRework(CurrentUser user, string reworkId);
    object ListRework();
    (object data, string? code, string? message) CloseReconciliation(string orderId);
    (object data, string? code, string? message) DisposeVariance(CurrentUser user, string orderId, Dtos.VarianceDispositionRequest request);
    (object data, string? code, string? message) DecideVariance(CurrentUser user, string varianceId, Dtos.VarianceDecisionRequest request);
}
