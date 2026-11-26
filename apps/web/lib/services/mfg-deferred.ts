// MFG deferred screens (MF1-MF10) — the additive service surface for the eight new screens.
// Separate module from mfg.ts so the merge stays additive (components never call fetch
// directly, CLAUDE.md services rule); reuses the same apiFetch boundary and withQuery shape.

import { apiFetch } from "./api";

function withQuery(path: string, params: Record<string, string | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
  }
  const qs = search.toString();
  return qs ? `${path}?${qs}` : path;
}

// --- MF1: routings / work centers --------------------------------------------

export type RoutingRow = {
  id: string; outputSku: string; revision: number; status: string;
  effectiveFrom: string; effectiveTo: string | null; approvedBy: string | null;
  createdBy: string; operations: RoutingOperationRow[];
};
export type RoutingOperationRow = {
  id: string; seq: number; operationCode: string; workCenterId: string;
  requiredCapabilities: string; timeUnit: string; setupMin: number; perUnitMin: number;
  isZeroDuration: boolean; kind: string; supplierId: string | null; subRoutingId: string | null;
};
export type WorkCenterRow = { id: string; code: string; name: string; capabilities: string; active: boolean };
export type CalendarRow = { id: string; workCenterId: string; windowStart: string; windowEnd: string; isAvailable: boolean; source: string };

export function fetchRoutings(token: string, outputSku?: string, status?: string) {
  return apiFetch<RoutingRow[]>(withQuery("/api/v1/routings", { outputSku, status }), { token });
}
export function createRouting(token: string, body: { outputSku: string; revision: number; effectiveFrom: string; effectiveTo?: string | null }) {
  return apiFetch<{ id: string }>("/api/v1/routings", { method: "POST", body, token });
}
export function addRoutingOperation(token: string, routingId: string, body: {
  operationCode: string; workCenterId: string; requiredCapabilities: string; timeUnit: string;
  setupMin: number; perUnitMin: number; isZeroDuration: boolean; seq: number;
}) {
  return apiFetch<RoutingOperationRow>(`/api/v1/routings/${routingId}/operations`, { method: "POST", body, token });
}
export function approveRouting(token: string, routingId: string, reason: string | null) {
  return apiFetch<unknown>(`/api/v1/routings/${routingId}/approvals`, { method: "POST", body: { reason }, token });
}
export function fetchWorkCenters(token: string) {
  return apiFetch<WorkCenterRow[]>("/api/v1/work-centers", { token });
}
export function createWorkCenter(token: string, body: { code: string; name: string; capabilities: string; active: boolean }) {
  return apiFetch<WorkCenterRow>("/api/v1/work-centers", { method: "POST", body, token });
}
export function fetchCalendar(token: string, workCenterId: string) {
  return apiFetch<CalendarRow[]>(`/api/v1/work-centers/${workCenterId}/calendar`, { token });
}
export function addCalendarWindow(token: string, workCenterId: string, body: {
  windowStart: string; windowEnd: string; isAvailable: boolean; source: string;
}) {
  return apiFetch<CalendarRow>(`/api/v1/work-centers/${workCenterId}/calendar`, { method: "POST", body, token });
}

// --- MF2/MF3: scheduling / dispatch / capacity ---------------------------------

export type ScheduleRunRow = {
  id: string; orderId: string; mode: string; status: string;
  horizonFrom: string; horizonTo: string; calendarVersion: string; algorithmVersion: string;
  placements: PlacementRow[]; conflicts: { code: string; severity: string; detail: string; resolved: boolean }[];
};
/** Flat run row as returned by GET /api/v1/schedule-runs (entity projection, no placements). */
export type ScheduleRunListRow = Omit<ScheduleRunRow, "placements" | "conflicts"> & { createdBy: string; createdAt: string };
export type PlacementRow = {
  id: string; seq: number; operationCode: string; workCenterId: string;
  startAt: string | null; endAt: string | null; scheduled: boolean; unscheduledReason: string | null;
};
export function createScheduleRun(token: string, body: {
  orderId: string; mode: string; horizonFrom: string; horizonTo: string; calendarVersion: string; algorithmVersion: string;
}) {
  return apiFetch<ScheduleRunRow>("/api/v1/schedule-runs", { method: "POST", body, token });
}
export function fetchScheduleRuns(token: string, orderId?: string) {
  return apiFetch<ScheduleRunListRow[]>(withQuery("/api/v1/schedule-runs", { orderId }), { token });
}
export function fetchSchedulePlacements(token: string, runId: string) {
  return apiFetch<PlacementRow[]>(`/api/v1/schedule-runs/${runId}/placements`, { token });
}
export function fetchScheduleConflicts(token: string, runId: string) {
  return apiFetch<ScheduleRunRow["conflicts"]>(`/api/v1/schedule-runs/${runId}/conflicts`, { token });
}
export function publishScheduleRun(token: string, runId: string, overrideReason: string | null) {
  return apiFetch<{ id: string }>(`/api/v1/schedule-runs/${runId}/publications`, { method: "POST", body: { overrideReason }, token });
}
export function createScenario(token: string, body: {
  name: string; baseRunId: string; frozenFrom: string | null; frozenTo: string | null;
}) {
  return apiFetch<{ id: string }>("/api/v1/schedule-scenarios", { method: "POST", body, token });
}
export function movePlacement(token: string, scenarioId: string, body: {
  placementId: string; newStartAt: string; overrideReason?: string | null; overrideApprover?: string | null;
}) {
  return apiFetch<{ id: string }>(`/api/v1/schedule-scenarios/${scenarioId}/placement-moves`, { method: "POST", body, token });
}
export type DispatchRow = {
  scheduleRunId: string | null; publishedAt: string | null; refreshedAt: string;
  rows: { id: string; seq: number; operationCode: string; workCenterId: string; orderId: string; orderNo: string | null; scheduled: boolean; dispatchable: boolean; blockedReason: string | null }[];
  unscheduled: { id: string; operationCode: string; workCenterId: string; reason: string | null }[];
};
export function fetchDispatchList(token: string, workCenter?: string) {
  return apiFetch<DispatchRow>(withQuery("/api/v1/dispatch-list", { workCenter }), { token });
}
export type CapacityLoadRow = {
  bucket: string; durationBasis: string;
  rows: { workCenterId: string; workCenterCode: string | null; bucketStart: string; loadMinutes: number; capacityMinutes: number; utilizationPct: number }[];
  unscheduled: { id: string; operationCode: string; workCenterId: string; reason: string | null }[];
};
export function fetchCapacityLoad(token: string, bucket: string) {
  return apiFetch<CapacityLoadRow>(withQuery("/api/v1/capacity-load", { bucket }), { token });
}

// --- MF4: MRP -------------------------------------------------------------------

export type MrpRunRow = { id: string; status: string; horizonFrom: string; horizonTo: string; failureDetail: string | null };
export type MrpLineRow = {
  id: string; lineNo: number; itemSku: string; explosionLevel: number; parentPath: string;
  grossQty: number; supplyQty: number; netQty: number; modifiersJson: string;
  requiredBy: string | null; leadTimeDays: number | null; exceptionCode: string | null;
  conversionStatus: string; convertedOrderId: string | null; convertedRequisitionRef: string | null; decisionReason: string | null;
};
export type ActionMessageRow = { id: string; code: string; message: string; state: string };
export function createMrpRun(token: string, body: {
  demands: { itemSku: string; qty: number; unit: string; by?: string | null }[];
  horizonFrom: string; horizonTo: string;
  safetyStockPct?: number | null; minLotQty?: number | null; multipleQty?: number | null;
}) {
  return apiFetch<MrpRunRow & { lines: MrpLineRow[] }>("/api/v1/mrp-runs", { method: "POST", body, token });
}
export function fetchMrpRecommendations(token: string, runId: string) {
  return apiFetch<MrpLineRow[]>(`/api/v1/mrp-runs/${runId}/recommendations`, { token });
}
export function fetchMrpActionMessages(token: string, runId: string) {
  return apiFetch<ActionMessageRow[]>(`/api/v1/mrp-runs/${runId}/action-messages`, { token });
}
export function convertMrpLine(token: string, lineId: string) {
  return apiFetch<{ convertedOrderId: string | null; convertedRequisitionRef: string | null }>(`/api/v1/mrp-lines/${lineId}/conversions`, { method: "POST", body: {}, token });
}
export function decideMrpLine(token: string, lineId: string, body: { action: string; reason: string }) {
  return apiFetch<MrpLineRow>(`/api/v1/mrp-lines/${lineId}/decisions`, { method: "POST", body, token });
}
export function publishMrpRun(token: string, runId: string, overrideReason: string | null) {
  return apiFetch<{ id: string; overrodeStaleSources: boolean }>(`/api/v1/mrp-runs/${runId}/publications`, { method: "POST", body: { overrideReason }, token });
}

// --- MF5: shop floor -------------------------------------------------------------

export type LaborRow = { id: string; workerEmploymentId: string; startAt: string; endAt: string; minutes: number; status: string; approvedBy: string | null; correctionOfId: string | null };
export type CounterRow = { id: string; sourceEventId: string; counterValue: number; deltaQty: number; rollover: boolean };
export type DowntimeRow = { id: string; workCenterId: string; startAt: string; endAt: string; reasonCode: string };
export type WipProjectionRow = {
  orderId: string; orderNo: string; wipDebits: number; wipCredits: number; wipRemainder: number;
  watermark: string | null; computedAt: string;
};
export type CorrectionRow = { id: string; targetType: string; targetId: string; reason: string; status: string; approvedBy: string | null };
export function reportLaborTime(token: string, orderId: string, body: { workerEmploymentId: string; startAt: string; endAt: string }) {
  return apiFetch<LaborRow>(`/api/v1/production-orders/${orderId}/labor-time`, { method: "POST", body, token });
}
export function fetchLaborTime(token: string, orderId: string) {
  return apiFetch<LaborRow[]>(`/api/v1/production-orders/${orderId}/labor-time`, { token });
}
export function approveLaborTime(token: string, orderId: string, entryId: string) {
  return apiFetch<LaborRow>(`/api/v1/production-orders/${orderId}/labor-time/${entryId}/approvals`, { method: "POST", body: {}, token });
}
export function reportCounterEvent(token: string, orderId: string, body: { sourceEventId: string; counterValue: number; workCenterId?: string | null }) {
  return apiFetch<CounterRow>(`/api/v1/production-orders/${orderId}/counter-events`, { method: "POST", body, token });
}
export function fetchCounterEvents(token: string, orderId: string) {
  return apiFetch<CounterRow[]>(`/api/v1/production-orders/${orderId}/counter-events`, { token });
}
export function reportDowntimeEvent(token: string, orderId: string, body: { workCenterId: string; startAt: string; endAt: string; reasonCode: string; note?: string | null }) {
  return apiFetch<DowntimeRow>(`/api/v1/production-orders/${orderId}/downtime-events`, { method: "POST", body, token });
}
export function fetchDowntimeEvents(token: string, orderId: string) {
  return apiFetch<DowntimeRow[]>(`/api/v1/production-orders/${orderId}/downtime-events`, { token });
}
export function fetchWipProjection(token: string, orderId: string) {
  return apiFetch<WipProjectionRow>(`/api/v1/production-orders/${orderId}/wip-projection`, { token });
}
export function transferWip(token: string, orderId: string, body: { fromSeq: number; toSeq: number; qty: number; deviationReason?: string | null }) {
  return apiFetch<{ id: string }>(`/api/v1/production-orders/${orderId}/wip-transfers`, { method: "POST", body, token });
}
export function proposeCorrection(token: string, body: { targetType: string; targetId: string; reason: string; compensatingJson: string }) {
  return apiFetch<{ id: string }>("/api/v1/production-corrections", { method: "POST", body, token });
}
export function approveCorrection(token: string, correctionId: string) {
  return apiFetch<{ id: string; status: string }>(`/api/v1/production-corrections/${correctionId}/approvals`, { method: "POST", body: {}, token });
}
export function fetchCorrections(token: string) {
  return apiFetch<CorrectionRow[]>("/api/v1/production-corrections", { token });
}

// --- MF6: subcontract -------------------------------------------------------------

export type CustodyRow = { id: string; routingOperationSeq: number; kind: string; qty: number; inventoryMovementId: string };
export type SubcontractReceiptRow = { id: string; routingOperationSeq: number; qty: number; acceptedQty: number; status: string; qualityHoldId: string | null };
export function addSubcontractOperation(token: string, routingId: string, body: {
  operationCode: string; workCenterId: string; supplierId: string; serviceDescription: string;
  setupMin: number; perUnitMin: number; seq: number;
}) {
  return apiFetch<{ id: string }>(`/api/v1/routings/${routingId}/subcontract-operations`, { method: "POST", body, token });
}
export function recordCustody(token: string, body: {
  orderId: string; routingOperationSeq: number; kind: string; qty: number; inventoryMovementId: string;
}) {
  return apiFetch<CustodyRow>("/api/v1/subcontract-custodies", { method: "POST", body, token });
}
export function fetchCustodies(token: string, orderId: string) {
  return apiFetch<{ rows: CustodyRow[]; reconciliation: { sent: number; returned: number; outstanding: number } }>(`/api/v1/subcontract-orders/${orderId}/custodies`, { token });
}
export function receiveSubcontract(token: string, orderId: string, body: { routingOperationSeq: number; qty: number; note?: string | null }) {
  return apiFetch<SubcontractReceiptRow>(`/api/v1/subcontract-orders/${orderId}/receipts`, { method: "POST", body, token });
}
export function fetchSubcontractReceipts(token: string, orderId: string) {
  return apiFetch<SubcontractReceiptRow[]>(`/api/v1/subcontract-orders/${orderId}/receipts`, { token });
}

// --- MF7: maintenance ----------------------------------------------------------------

export type MaintenanceEventRow = {
  id: string; sourceEventId: string; revision: number; resourceCode: string;
  windowStart: string; windowEnd: string; status: string; exceptionReason: string | null;
};
export function ingestMaintenanceEvent(token: string, body: {
  sourceEventId: string; revision: number; resourceCode: string; assetId?: string | null;
  windowStart: string; windowEnd: string; reason?: string | null;
}) {
  return apiFetch<MaintenanceEventRow>("/api/v1/maintenance-availability-events", { method: "POST", body, token });
}
export function fetchScheduleImpacts(token: string, outage: string) {
  return apiFetch<{ rows: { placementId: string; orderId: string; workCenterId: string; overlapMinutes: number; disposition: string }[] }>(withQuery("/api/v1/schedule-impacts", { outage }), { token });
}
export function resolveScheduleImpact(token: string, body: {
  outageEventId: string; scheduleRunId: string; orderId: string; workCenterId: string; disposition: string; reason?: string | null;
}) {
  return apiFetch<{ id: string }>("/api/v1/schedule-impacts/resolutions", { method: "POST", body, token });
}

// --- MF8/MF9: quality ------------------------------------------------------------------

export type QualityPlanRow = {
  id: string; itemSku: string; revision: number; status: string; effectiveFrom: string;
  approvedBy: string | null; characteristics: { code: string; name: string; dataType: string; mandatory: boolean; minValue: number | null; maxValue: number | null }[];
};
export type InspectionRow = { id: string; qualityPlanId: string; planRevision: number; itemSku: string; sampleSize: number; status: string };
export type InspectionResultRow = { id: string; characteristicCode: string; rawValue: string; evaluatedPass: boolean | null; amendmentNo: number; amendsId: string | null };
export type QualityHoldRow = {
  id: string; itemSku: string; qty: number; orderId: string | null; status: string;
  disposition: string | null; heldBy: string; dispositionedBy: string | null;
};
export type NonconformanceRow = {
  id: string; title: string; affectedQty: number; status: string;
  objects: { objectType: string; objectRef: string; qty: number }[];
  containments: { id: string; kind: string; status: string; openedBy: string; releasedBy: string | null }[];
};
export type CapaRow = { id: string; title: string; status: string; effectivenessCriteria: string };
export type QualityDashboardRow = { asOf: string; openHolds: number; heldQty: number; nonconformancesOpen: number; capaOpen: number; inspectionsInProgress: number };
export function fetchQualityPlans(token: string, itemSku?: string) {
  return apiFetch<QualityPlanRow[]>(withQuery("/api/v1/quality-plans", { itemSku }), { token });
}
export function createQualityPlan(token: string, body: { itemSku: string; revision: number; effectiveFrom: string; effectiveTo?: string | null }) {
  return apiFetch<{ id: string }>("/api/v1/quality-plans", { method: "POST", body, token });
}
export function addQualityCharacteristic(token: string, planId: string, body: {
  code: string; name: string; dataType: string; minValue?: number | null; maxValue?: number | null; unit?: string | null; mandatory: boolean;
}) {
  return apiFetch<{ id: string }>(`/api/v1/quality-plans/${planId}/characteristics`, { method: "POST", body, token });
}
export function approveQualityPlan(token: string, planId: string) {
  return apiFetch<{ id: string }>(`/api/v1/quality-plans/${planId}/approvals`, { method: "POST", body: {}, token });
}
export function createInspection(token: string, body: {
  qualityPlanId: string; orderId: string; itemSku: string; lotSerial?: string | null; populationQty: number;
}) {
  return apiFetch<InspectionRow>("/api/v1/inspections", { method: "POST", body, token });
}
export function addInspectionResult(token: string, inspectionId: string, body: { characteristicCode: string; rawValue: string }) {
  return apiFetch<InspectionResultRow>(`/api/v1/inspections/${inspectionId}/results`, { method: "POST", body, token });
}
export function finalizeInspection(token: string, inspectionId: string) {
  return apiFetch<InspectionRow>(`/api/v1/inspections/${inspectionId}/finalizations`, { method: "POST", body: {}, token });
}
export function createQualityHold(token: string, body: {
  itemSku: string; lotSerial?: string | null; qty: number; orderId?: string | null;
  inspectionId?: string | null; nonconformanceId?: string | null;
}) {
  return apiFetch<QualityHoldRow>("/api/v1/quality-holds", { method: "POST", body, token });
}
export function fetchQualityHolds(token: string) {
  return apiFetch<QualityHoldRow[]>("/api/v1/quality-holds", { token });
}
export function disposeQualityHold(token: string, holdId: string, body: { disposition: string; qty: number; reason?: string | null }) {
  return apiFetch<QualityHoldRow>(`/api/v1/quality-holds/${holdId}/dispositions`, { method: "POST", body, token });
}
export function raiseNonconformance(token: string, body: {
  sourceEventId: string; title: string; itemSku?: string | null; orderId?: string | null; affectedQty: number;
  objects: { objectType: string; objectRef: string; qty: number }[];
}) {
  return apiFetch<NonconformanceRow>("/api/v1/nonconformances", { method: "POST", body, token });
}
export function fetchNonconformances(token: string) {
  return apiFetch<NonconformanceRow[]>("/api/v1/nonconformances", { token });
}
export function addContainment(token: string, ncId: string, kind: string) {
  return apiFetch<{ id: string }>(`/api/v1/nonconformances/${ncId}/containments`, { method: "POST", body: { kind }, token });
}
export function releaseContainment(token: string, containmentId: string, reason: string) {
  return apiFetch<{ id: string }>(`/api/v1/containments/${containmentId}/releases`, { method: "POST", body: { reason }, token });
}
export function closeNonconformance(token: string, ncId: string) {
  return apiFetch<{ id: string }>(`/api/v1/nonconformances/${ncId}/closures`, { method: "POST", body: {}, token });
}
export function createCapaCase(token: string, body: { title: string; nonconformanceId?: string | null; effectivenessCriteria: string }) {
  return apiFetch<CapaRow>("/api/v1/capa-cases", { method: "POST", body, token });
}
export function fetchCapaCases(token: string) {
  return apiFetch<CapaRow[]>("/api/v1/capa-cases", { token });
}
export function addCapaAction(token: string, capaId: string, description: string) {
  return apiFetch<{ id: string }>(`/api/v1/capa-cases/${capaId}/actions`, { method: "POST", body: { description }, token });
}
export function completeCapaAction(token: string, actionId: string) {
  return apiFetch<{ id: string }>(`/api/v1/capa-actions/${actionId}/completions`, { method: "POST", body: {}, token });
}
export function verifyCapa(token: string, capaId: string, body: { criteriaResult: string; satisfied: boolean }) {
  return apiFetch<{ id: string }>(`/api/v1/capa-cases/${capaId}/verifications`, { method: "POST", body, token });
}
export function closeCapaCase(token: string, capaId: string) {
  return apiFetch<{ id: string }>(`/api/v1/capa-cases/${capaId}/closures`, { method: "POST", body: {}, token });
}
export function fetchQualityDashboard(token: string) {
  return apiFetch<QualityDashboardRow>("/api/v1/quality-dashboards", { token });
}
export function fetchInspectionResults(token: string, inspectionId: string) {
  return apiFetch<InspectionResultRow[]>(`/api/v1/inspections/${inspectionId}/results`, { token });
}

// --- MF10: genealogy / rework / reconciliation ---------------------------------------------

export type GenealogyRow = {
  events: { id: string; orderId: string; outputSku: string; outputQty: number; lotSerial: string | null; inputs: { itemSku: string; qty: number }[] }[];
  gaps: { itemSku: string; reason: string }[];
  complete: boolean;
};
export type ReworkRow = {
  id: string; sourceOrderId: string; itemSku: string; qty: number; status: string;
  reinspectionInspectionId: string | null; adjustmentReason: string | null;
};
export type CloseReconciliationRow = {
  buckets: { planned: number; issued: number; returned: number; consumed: number; completed: number; scrapped: number; reworked: number; heldQty: number; wipRemainder: number };
  variances: { id: string; bucketCode: string; qtyVariance: number; owner: string; disposition: string; isCritical: boolean; ageDays: number }[];
  criticalUnresolved: number;
  closePolicy: string;
};
export function fetchGenealogy(token: string, query: { lot?: string; sku?: string }) {
  return apiFetch<GenealogyRow>(withQuery("/api/v1/genealogy", { lot: query.lot, sku: query.sku }), { token });
}
export function fetchReworkOrders(token: string) {
  return apiFetch<ReworkRow[]>("/api/v1/rework-orders", { token });
}
export function createReworkOrder(token: string, body: {
  sourceOrderId: string; itemSku: string; qty: number; nonconformanceId?: string | null;
  holdId?: string | null; adjustmentReason?: string | null;
}) {
  return apiFetch<ReworkRow>("/api/v1/rework-orders", { method: "POST", body, token });
}
export function approveReworkOrder(token: string, reworkId: string) {
  return apiFetch<ReworkRow>(`/api/v1/rework-orders/${reworkId}/approvals`, { method: "POST", body: {}, token });
}
export function completeReworkOrder(token: string, reworkId: string) {
  return apiFetch<ReworkRow>(`/api/v1/rework-orders/${reworkId}/completions`, { method: "POST", body: {}, token });
}
export function reinspectReworkOrder(token: string, reworkId: string, inspectionId: string) {
  return apiFetch<ReworkRow>(`/api/v1/rework-orders/${reworkId}/reinspections`, { method: "POST", body: { inspectionId }, token });
}
export function fetchCloseReconciliation(token: string, orderId: string) {
  return apiFetch<CloseReconciliationRow>(`/api/v1/production-orders/${orderId}/close-reconciliation`, { token });
}
export function disposeVariance(token: string, orderId: string, body: { bucketCode: string; qtyVariance: number; isCritical: boolean; owner?: string | null }) {
  return apiFetch<{ id: string }>(`/api/v1/production-orders/${orderId}/close-reconciliation/variance-dispositions`, { method: "POST", body, token });
}
export function decideVariance(token: string, varianceId: string, body: { disposition: string; reason: string }) {
  return apiFetch<{ id: string }>(`/api/v1/variance-dispositions/${varianceId}/decisions`, { method: "POST", body, token });
}
