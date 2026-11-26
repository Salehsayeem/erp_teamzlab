// PEOPLE service module — the single API boundary for the people screens
// (workers, time capture/corrections, pay codes/groups, pay runs, payslips).
// Components never call fetch directly (CLAUDE.md services rule); this file
// imports `apiFetch` from ./api and never edits it.

import { apiFetch } from "./api";

// --- Query helper ------------------------------------------------------------

function withQuery(path: string, params: Record<string, string | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
  }
  const qs = search.toString();
  return qs ? `${path}?${qs}` : path;
}

// --- Domain types (camelCase mirrors of the api projections) -----------------

export type Worker = {
  id: string;
  fullName: string;
  preferredName: string | null;
  nationalId: string | null;
  email: string | null;
  phone: string | null;
  workerNo: string;
  status: string;
  categoryCode: string;
  managerUserId: string | null;
  locationId: string | null;
  weeklyPlannedHours: number;
  hireDate: string;
  terminationDate: string | null;
  terminationReason: string | null;
  createdBy: string;
  createdAt: string;
};

export type CompensationEventRow = {
  id: string;
  amount: number;
  currency: string;
  payBasis: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  reason: string;
  approvedBy: string;
};

export type CompensationsResponse = {
  workerId: string;
  asOf: { date: string; amount: number | null; payBasis: string | null; currency: string | null };
  events: CompensationEventRow[];
};

export type PayGroupRow = { id: string; code: string; name: string; frequency: string; active: boolean; members: string[] };
export type PayGroupMembership = { id: string; employmentId: string; payGroupId: string; effectiveFrom: string; effectiveTo: string | null };

export type PayRunLineRow = {
  employmentId: string;
  payCodeId: string;
  payCode: string;
  kind: string;
  hours: number;
  rate: number;
  amount: number;
  currency: string;
  sourceEntryId: string | null;
};

export type PayHistoryRow = {
  runId: string;
  periodId: string;
  payDate: string;
  currency: string;
  status: string;
  kind: string;
  reversalOfRunId: string | null;
  gross: number;
  deductions: number;
  net: number;
  reversalReason: string | null;
  fundsReleased: boolean | null;
  lines: PayRunLineRow[];
};

export type TimeEntryRow = {
  id: string;
  employmentId: string;
  workDate: string;
  hours: number;
  overtimeHours: number;
  status: string;
  source: string;
  idemKey: string | null;
  note: string | null;
  createdBy: string;
  createdAt: string;
};

export type TimeCorrectionRow = {
  id: string;
  timeEntryId: string;
  previousHours: number;
  previousOvertimeHours: number;
  proposedHours: number;
  proposedOvertimeHours: number;
  deltaHours: number;
  deltaOvertimeHours: number;
  reason: string;
  status: string;
  decisionReason: string | null;
  requestedBy: string;
  approvedBy: string | null;
  approvedAt: string | null;
  createdAt: string;
};

export type PayCodeRow = {
  id: string;
  code: string;
  kind: string;
  name: string;
  calcBasis: string;
  ratePct: number | null;
  debitAccount: string;
  creditAccount: string;
  active: boolean;
  effectiveFrom: string;
  version: number;
  createdBy: string;
  createdAt: string;
};

export type PayRunRow = {
  id: string;
  payGroupId: string;
  periodId: string;
  status: string;
  currency: string;
  payDate: string;
  grossTotal: number;
  deductionTotal: number;
  netTotal: number;
  calculatedBy: string | null;
  calculatedAt: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  journalId: string | null;
  postedBy: string | null;
  postedAt: string | null;
  reversalOfRunId: string | null;
  reversalReason: string | null;
  fundsReleased: boolean | null;
  createdBy: string;
  createdAt: string;
  lineCount: number;
};

export type ValidationFinding = { code: string; workerCount: number; message: string; ownerRole?: string };
export type RunValidations = {
  runId: string;
  status: string;
  canFinalize: boolean;
  blocking: ValidationFinding[];
  warnings: ValidationFinding[];
};

export type PreviewWorker = {
  employmentId: string;
  workerNo: string;
  name: string;
  gross: number;
  deductions: number;
  net: number;
  flags: string[];
  excluded: boolean;
};
export type PreviewLine = {
  employmentId: string;
  payCode: string;
  kind: string;
  hours: number;
  rate: number;
  amount: number;
  currency: string;
  sourceEntryId: string | null;
};
export type RunPreview = {
  runId: string;
  status: string;
  payGroupId: string;
  periodId: string;
  periodName: string;
  payDate: string;
  currency: string;
  grossTotal: number;
  deductionTotal: number;
  netTotal: number;
  reconciles: boolean;
  workers: PreviewWorker[];
  lines: PreviewLine[];
};

export type Payslip = {
  runId: string;
  workerId: string;
  workerNo: string;
  name: string;
  period: { id: string; name: string; startDate: string; endDate: string };
  payDate: string;
  currency: string;
  gross: number;
  deductions: number;
  net: number;
  runStatus: string;
  reversed: boolean;
  reversalRunId: string | null;
  lines: PayRunLineRow[];
};

export type FiscalPeriodRow = {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  status: string;
};

// --- Workers (R-PEO3 one-step register, 007 lifecycle, 024 compensation) ------

export function fetchWorkers(token: string, status?: string, search?: string) {
  return apiFetch<Worker[]>(withQuery("/api/v1/workers", { status, search }), { token });
}
export function createWorker(token: string, body: {
  fullName: string; preferredName?: string | null; nationalId?: string | null; email?: string | null;
  phone?: string | null; categoryCode: string; hireDate: string; weeklyPlannedHours: number;
  locationId?: string | null; managerUserId?: string | null;
}) {
  return apiFetch<Worker>("/api/v1/workers", { method: "POST", body, token });
}
export function transitionWorker(token: string, id: string, body: { status: string; effectiveDate: string; reason?: string | null }) {
  return apiFetch<Worker>(`/api/v1/workers/${id}/transitions`, { method: "POST", body, token });
}
export function addCompensation(token: string, id: string, body: {
  amount: number; currency: string; payBasis: string; effectiveFrom: string; reason: string;
}) {
  return apiFetch<CompensationEventRow>(`/api/v1/workers/${id}/compensations`, { method: "POST", body, token });
}
export function fetchCompensations(token: string, id: string, asOf?: string | null) {
  return apiFetch<CompensationsResponse>(withQuery(`/api/v1/workers/${id}/compensations`, { asOf }), { token });
}
export function addPayGroupMembership(token: string, id: string, body: { payGroupId: string; effectiveFrom: string; effectiveTo?: string | null }) {
  return apiFetch<PayGroupMembership>(`/api/v1/workers/${id}/pay-group-memberships`, { method: "POST", body, token });
}
export function fetchPayHistory(token: string, id: string) {
  return apiFetch<PayHistoryRow[]>(`/api/v1/workers/${id}/pay-history`, { token });
}

// --- Pay groups (035 trim: no calendar generation) ------------------------------

export function fetchPayGroups(token: string) {
  return apiFetch<PayGroupRow[]>("/api/v1/pay-groups", { token });
}
export function createPayGroup(token: string, body: { code: string; name: string; frequency: string }) {
  return apiFetch<PayGroupRow>("/api/v1/pay-groups", { method: "POST", body, token });
}

// --- Time capture + corrections (026/027) ----------------------------------------

export function fetchTimeEntries(token: string, employmentId?: string, status?: string) {
  return apiFetch<TimeEntryRow[]>(withQuery("/api/v1/time-entries", { employmentId, status }), { token });
}
export function createTimeEntry(token: string, body: {
  employmentId: string; workDate: string; hours: number; overtimeHours: number; note?: string | null;
}) {
  return apiFetch<TimeEntryRow>("/api/v1/time-entries", { method: "POST", body, token });
}
export function decideTimeEntry(token: string, id: string, approve: boolean, decisionReason?: string | null) {
  return apiFetch<TimeEntryRow>(`/api/v1/time-entries/${id}/decisions`, { method: "POST", body: { approve, decisionReason: decisionReason ?? null }, token });
}
export function fetchTimeCorrections(token: string, employmentId?: string, status?: string) {
  return apiFetch<TimeCorrectionRow[]>(withQuery("/api/v1/time-corrections", { employmentId, status }), { token });
}
export function createTimeCorrection(token: string, body: {
  timeEntryId: string; proposedHours: number; proposedOvertimeHours: number; reason: string;
}) {
  return apiFetch<TimeCorrectionRow>("/api/v1/time-corrections", { method: "POST", body, token });
}
export function decideTimeCorrection(token: string, id: string, approve: boolean, decisionReason?: string | null) {
  return apiFetch<TimeCorrectionRow>(`/api/v1/time-corrections/${id}/decisions`, { method: "POST", body: { approve, decisionReason: decisionReason ?? null }, token });
}

// --- Pay codes (037: mapping required before activation) --------------------------

export function fetchPayCodes(token: string, kind?: string) {
  return apiFetch<PayCodeRow[]>(withQuery("/api/v1/pay-codes", { kind }), { token });
}
export function createPayCode(token: string, body: {
  code: string; kind: string; name: string; calcBasis: string; ratePct?: number | null;
  debitAccount: string; creditAccount: string;
}) {
  return apiFetch<PayCodeRow>("/api/v1/pay-codes", { method: "POST", body, token });
}
export function updatePayCode(token: string, code: string, body: { name?: string | null; ratePct?: number | null; calcBasis?: string | null }) {
  return apiFetch<PayCodeRow>(`/api/v1/pay-codes/${code}/updates`, { method: "POST", body, token });
}
export function deactivatePayCode(token: string, code: string) {
  return apiFetch<PayCodeRow>(`/api/v1/pay-codes/${code}/deactivations`, { method: "POST", body: {}, token });
}

// --- Pay runs (048/049/053/055) ----------------------------------------------------

export function fetchPayRuns(token: string, status?: string, periodId?: string) {
  return apiFetch<PayRunRow[]>(withQuery("/api/v1/pay-runs", { status, periodId }), { token });
}
export function createPayRun(token: string, body: { payGroupId: string; periodId: string; payDate: string }) {
  return apiFetch<PayRunRow>("/api/v1/pay-runs", { method: "POST", body, token });
}
export function calculatePayRun(token: string, id: string) {
  return apiFetch<PayRunRow>(`/api/v1/pay-runs/${id}/calculations`, { method: "POST", body: {}, token });
}
export function fetchRunValidations(token: string, id: string) {
  return apiFetch<RunValidations>(`/api/v1/pay-runs/${id}/validations`, { token });
}
export function fetchRunPreview(token: string, id: string) {
  return apiFetch<RunPreview>(`/api/v1/pay-runs/${id}/preview`, { token });
}
export function approvePayRun(token: string, id: string, reason: string | null) {
  return apiFetch<PayRunRow>(`/api/v1/pay-runs/${id}/approvals`, { method: "POST", body: { reason }, token });
}
export function postPayRun(token: string, id: string) {
  return apiFetch<{ run: PayRunRow; journal: { id: string; memo: string; status: string } | null }>(`/api/v1/pay-runs/${id}/postings`, { method: "POST", body: {}, token });
}
export function fetchPayslip(token: string, id: string, workerId: string) {
  return apiFetch<Payslip>(`/api/v1/pay-runs/${id}/payslips/${workerId}`, { token });
}
export function reversePayRun(token: string, id: string, body: { reason: string; fundsReleased: boolean }) {
  return apiFetch<{ run: PayRunRow; journal: { id: string; memo: string } }>(`/api/v1/pay-runs/${id}/reversals`, { method: "POST", body, token });
}

// --- Fiscal periods (period picker; needs periods:read — HR degrades to input) -----

export function fetchFiscalPeriods(token: string, status?: string) {
  return apiFetch<FiscalPeriodRow[]>(withQuery("/api/v1/fiscal-periods", { status }), { token });
}

// --- Deferred screens (PM1–PM10) — same envelope through apiFetch ---------------

export type OrgUnitRow = { id: string; code: string; name: string; parentOrgUnitId: string | null; locationId: string | null; effectiveFrom: string; effectiveTo: string | null };
export type WorkLocationRow = { id: string; code: string; name: string; timezone: string; active: boolean };
export type PositionRow = { id: string; code: string; title: string; gradeCode: string | null; requiredCredentialCode: string | null; budgetedFte: number; budgetedHeadcount: number; status: string; assignedFte: number; incumbents: number };
export type PositionAssignmentRow = { id: string; positionId: string; employmentId: string; fte: number; costCenter: string | null; effectiveFrom: string; status: string; exceptionApprovedBy: string | null; approvedVariance: number | null; exceptionReason: string | null };

export function fetchOrgUnits(token: string, asOf?: string | null) {
  return apiFetch<OrgUnitRow[]>(withQuery("/api/v1/org-units", { asOf }), { token });
}
export function createOrgUnit(token: string, body: { code: string; name: string; parentOrgUnitId?: string | null; effectiveFrom: string }) {
  return apiFetch<OrgUnitRow>("/api/v1/org-units", { method: "POST", body, token });
}
export function fetchWorkLocations(token: string) {
  return apiFetch<WorkLocationRow[]>("/api/v1/work-locations", { token });
}
export function createWorkLocation(token: string, body: { code: string; name: string; timezone: string }) {
  return apiFetch<WorkLocationRow>("/api/v1/work-locations", { method: "POST", body, token });
}
export function fetchPositions(token: string) {
  return apiFetch<PositionRow[]>("/api/v1/positions", { token });
}
export function createPosition(token: string, body: { code: string; title: string; gradeCode?: string | null; requiredCredentialCode?: string | null; budgetedFte: number; budgetedHeadcount: number }) {
  return apiFetch<PositionRow>("/api/v1/positions", { method: "POST", body, token });
}
export function assignPosition(token: string, id: string, body: { employmentId: string; fte: number; costCenter?: string | null; effectiveFrom: string; exceptionReason?: string | null }) {
  return apiFetch<PositionAssignmentRow>(`/api/v1/positions/${id}/assignments`, { method: "POST", body, token });
}
export function decideAssignmentException(token: string, id: string, body: { approve: boolean; reason: string }) {
  return apiFetch<PositionAssignmentRow>(`/api/v1/position-assignments/${id}/exception-decisions`, { method: "POST", body, token });
}
export function fetchPositionAssignments(token: string, employmentId?: string | null) {
  return apiFetch<PositionAssignmentRow[]>(withQuery("/api/v1/position-assignments", { employmentId }), { token });
}

export type OnboardingTaskRow = { id: string; employmentId: string; taskKey: string; name: string; dueDate: string; ownerRole: string; required: boolean; status: string; decidedBy: string | null; decisionReason: string | null };
export type OnboardingTaskWithOverdue = { task: OnboardingTaskRow; overdue: boolean }; // API wraps each task with a computed overdue flag

export function createOnboardingTemplate(token: string, body: { categoryCode: string; taskKey: string; name: string; dueOffsetDays: number; ownerRole: string; required: boolean }) {
  return apiFetch<OnboardingTaskRow>("/api/v1/onboarding-templates", { method: "POST", body, token });
}
export function generateOnboardingTasks(token: string, workerId: string) {
  return apiFetch<{ created: number; tasks: OnboardingTaskRow[] }>(`/api/v1/workers/${workerId}/onboarding-tasks/generations`, { method: "POST", body: {}, token });
}
export function fetchOnboardingTasks(token: string, workerId: string) {
  return apiFetch<OnboardingTaskWithOverdue[]>(`/api/v1/workers/${workerId}/onboarding-tasks`, { token });
}
export function completeOnboardingTask(token: string, id: string) {
  return apiFetch<OnboardingTaskRow>(`/api/v1/onboarding-tasks/${id}/completions`, { method: "POST", body: {}, token });
}
export function waiveOnboardingTask(token: string, id: string, reason: string) {
  return apiFetch<OnboardingTaskRow>(`/api/v1/onboarding-tasks/${id}/waivers`, { method: "POST", body: { reason }, token });
}

export type JobProfileRow = { id: string; code: string; name: string; department: string | null; versions: { id: string; version: number; title: string; effectiveFrom: string }[] };
export type GradeRangeRow = { id: string; gradeCode: string; currency: string; minAmount: number; midAmount: number; maxAmount: number; effectiveFrom: string; effectiveTo: string | null };
export type PolicyCheckResult = { workerId: string; gate: string; inPolicy: boolean; approvalRequired: boolean; message: string };

export function fetchJobProfiles(token: string) {
  return apiFetch<JobProfileRow[]>("/api/v1/job-profiles", { token });
}
export function createJobProfile(token: string, body: { code: string; name: string; title: string; effectiveFrom: string }) {
  return apiFetch<JobProfileRow>("/api/v1/job-profiles", { method: "POST", body, token });
}
export function fetchGradeRanges(token: string) {
  return apiFetch<GradeRangeRow[]>("/api/v1/grade-ranges", { token });
}
export function createGradeRange(token: string, body: { gradeCode: string; currency: string; frequency: string; minAmount: number; midAmount: number; maxAmount: number; effectiveFrom: string }) {
  return apiFetch<GradeRangeRow>("/api/v1/grade-ranges", { method: "POST", body, token });
}
export function checkCompPolicy(token: string, workerId: string, body: { amount: number; effectiveFrom: string }) {
  return apiFetch<PolicyCheckResult>(`/api/v1/comp-changes/${workerId}/policy-checks`, { method: "POST", body, token });
}

export type ScheduleRow = { id: string; code: string; name: string; timezone: string; startTimeLocal: string; versions: { id: string; version: number; weeklyPlannedHours: number; effectiveFrom: string; effectiveTo: string | null }[] };
export type ScheduleDay = { date: string; isWorkday: boolean; plannedHours: number; holiday: string | null; intervals: { startUtc: string; endUtc: string }[] };

export function fetchWorkSchedules(token: string) {
  return apiFetch<ScheduleRow[]>("/api/v1/work-schedules", { token });
}
export function createWorkSchedule(token: string, body: { code: string; name: string; timezone: string; startTimeLocal: string }) {
  return apiFetch<ScheduleRow>("/api/v1/work-schedules", { method: "POST", body, token });
}
export function createScheduleVersion(token: string, id: string, body: { weeklyPlannedHours: number; effectiveFrom: string; holidayCalendarId?: string | null }) {
  return apiFetch<unknown>(`/api/v1/work-schedules/${id}/versions`, { method: "POST", body, token });
}
export function expandSchedule(token: string, id: string, from: string, to: string) {
  return apiFetch<{ days: ScheduleDay[] }>(`/api/v1/work-schedules/${id}/expansions?from=${from}&to=${to}`, { token });
}

export type LeaveTypeRow = { id: string; code: string; name: string; units: string; accrualMethod: string; accrualRatePerMonth: number | null; active: boolean };
export type LeaveRequestRow = { id: string; employmentId: string; leaveTypeId: string; fromDate: string; toDate: string; days: number; status: string; requestedBy: string; decidedBy: string | null };
export type LeaveBalance = { workerId: string; balance: number; entries: { id: string; period: string; kind: string; qty: number; entryDate: string; referenceId: string | null }[] };
export type LeaveAdjustmentRow = { id: string; employmentId: string; leaveTypeId: string; qty: number; reason: string; status: string; requestedBy: string };
export type AttendanceExceptionRow = { id: string; employmentId: string; workDate: string; kind: string; sourceFact: string; disposition: string | null; dispositionedBy: string | null };

export function fetchLeaveTypes(token: string) {
  return apiFetch<LeaveTypeRow[]>("/api/v1/leave-types", { token });
}
export function createLeaveType(token: string, body: { code: string; name: string; units: string; accrualMethod: string; accrualRatePerMonth?: number | null; requiresApproval: boolean; effectiveFrom: string }) {
  return apiFetch<LeaveTypeRow>("/api/v1/leave-types", { method: "POST", body, token });
}
export function runAccrual(token: string, id: string, body: { period: string; asOf: string }) {
  return apiFetch<{ created: number }>(`/api/v1/leave-types/${id}/accrual-runs`, { method: "POST", body, token });
}
export function fetchLeaveRequests(token: string) {
  return apiFetch<LeaveRequestRow[]>("/api/v1/leave-requests", { token });
}
export function createLeaveRequest(token: string, body: { employmentId: string; leaveTypeId: string; fromDate: string; toDate: string; days: number; reason: string }) {
  return apiFetch<LeaveRequestRow>("/api/v1/leave-requests", { method: "POST", body, token });
}
export function decideLeaveRequest(token: string, id: string, approve: boolean) {
  return apiFetch<LeaveRequestRow>(`/api/v1/leave-requests/${id}/decisions`, { method: "POST", body: { approve }, token });
}
export function withdrawLeaveRequest(token: string, id: string, reason: string) {
  return apiFetch<LeaveRequestRow>(`/api/v1/leave-requests/${id}/withdrawals`, { method: "POST", body: { reason }, token });
}
export function fetchLeaveBalance(token: string, workerId: string, leaveTypeId?: string | null, asOf?: string | null) {
  return apiFetch<LeaveBalance>(withQuery(`/api/v1/workers/${workerId}/leave-balances`, { leaveTypeId, asOf }), { token });
}
export function fetchLeaveAdjustments(token: string) {
  return apiFetch<LeaveAdjustmentRow[]>("/api/v1/leave-adjustments", { token });
}
export function createLeaveAdjustment(token: string, body: { employmentId: string; leaveTypeId: string; qty: number; reason: string }) {
  return apiFetch<LeaveAdjustmentRow>("/api/v1/leave-adjustments", { method: "POST", body, token });
}
export function decideLeaveAdjustment(token: string, id: string, approve: boolean) {
  return apiFetch<LeaveAdjustmentRow>(`/api/v1/leave-adjustments/${id}/approvals`, { method: "POST", body: { approve }, token });
}
export function fetchAttendanceExceptions(token: string) {
  return apiFetch<AttendanceExceptionRow[]>("/api/v1/attendance-exceptions", { token });
}
export function generateAttendanceExceptions(token: string, body: { from: string; to: string }) {
  return apiFetch<{ created: number }>("/api/v1/attendance-exceptions/generations", { method: "POST", body, token });
}
export function disposeAttendanceException(token: string, id: string, body: { disposition: string; reason: string }) {
  return apiFetch<AttendanceExceptionRow>(`/api/v1/attendance-exceptions/${id}/dispositions`, { method: "POST", body, token });
}

export type TaxElectionRow = { id: string; employmentId: string; kind: string; status: string; effectiveFrom: string };
export type ContributionRuleRow = { id: string; code: string; name: string; party: string; ratePct: number; payCodeId: string };
export type GarnishmentRow = { id: string; employmentId: string; kind: string; priority: number; amountPerPeriod: number; status: string; releaseEvidence: string | null; releasedBy: string | null };
export type BenefitPlanRow = { id: string; code: string; name: string; providerCode: string | null; employeePct: number; payCodeId: string };
export type BenefitEnrollmentRow = { id: string; planId: string; employmentId: string; status: string; instructions: { id: string; period: string; amount: number; version: number }[] };

export function fetchTaxElections(token: string) {
  return apiFetch<TaxElectionRow[]>("/api/v1/tax-elections", { token });
}
export function createTaxElection(token: string, body: { employmentId: string; kind: string; value?: string | null; effectiveFrom: string }) {
  return apiFetch<TaxElectionRow>("/api/v1/tax-elections", { method: "POST", body, token });
}
export function verifyTaxElection(token: string, id: string) {
  return apiFetch<TaxElectionRow>(`/api/v1/tax-elections/${id}/verifications`, { method: "POST", body: {}, token });
}
export function fetchContributionRules(token: string) {
  return apiFetch<ContributionRuleRow[]>("/api/v1/contribution-rules", { token });
}
export function createContributionRule(token: string, body: { code: string; name: string; party: string; ratePct: number; payCodeId: string; effectiveFrom: string }) {
  return apiFetch<ContributionRuleRow>("/api/v1/contribution-rules", { method: "POST", body, token });
}
export function reconcileContribution(token: string, id: string, runId: string) {
  return apiFetch<{ expected: number; posted: number; reconciles: boolean }>(`/api/v1/contribution-rules/${id}/reconciliations?runId=${runId}`, { token });
}
export function fetchGarnishments(token: string) {
  return apiFetch<GarnishmentRow[]>("/api/v1/garnishment-orders", { token });
}
export function createGarnishment(token: string, body: { employmentId: string; kind: string; priority: number; amountPerPeriod: number; courtRef: string; effectiveFrom: string }) {
  return apiFetch<GarnishmentRow>("/api/v1/garnishment-orders", { method: "POST", body, token });
}
export function releaseGarnishment(token: string, id: string, evidence: string) {
  return apiFetch<GarnishmentRow>(`/api/v1/garnishment-orders/${id}/releases`, { method: "POST", body: { evidence }, token });
}
export function fetchBenefitPlans(token: string) {
  return apiFetch<BenefitPlanRow[]>("/api/v1/benefit-plans", { token });
}
export function createBenefitPlan(token: string, body: { code: string; name: string; employeePct: number; payCodeId: string }) {
  return apiFetch<BenefitPlanRow>("/api/v1/benefit-plans", { method: "POST", body, token });
}
export function createEnrollment(token: string, body: { employmentId: string; planId: string; effectiveFrom: string }) {
  return apiFetch<BenefitEnrollmentRow>("/api/v1/benefit-enrollments", { method: "POST", body, token });
}
export function fetchEnrollment(token: string, id: string) {
  return apiFetch<BenefitEnrollmentRow>(`/api/v1/benefit-enrollments/${id}`, { token });
}
export function postProviderFeed(token: string, id: string, body: { period: string; amount: number; sourceEventId: string }) {
  return apiFetch<{ duplicate: boolean }>(`/api/v1/benefit-enrollments/${id}/provider-feeds`, { method: "POST", body, token });
}
export function reconcileEnrollment(token: string, id: string, period: string) {
  return apiFetch<{ flags: string[]; outOfSync: boolean }>(`/api/v1/benefit-enrollments/${id}/reconciliations?period=${period}`, { token });
}

export type PaymentFileRow = { id: string; runId: string; status: string; provider: string; itemCount: number; totalNet: number; currency: string; contentHash: string; createdBy: string; releasedBy: string | null; items: { employmentId: string; netAmount: number; status: string; rejectReason: string | null }[] };

export function fetchPaymentFiles(token: string) {
  return apiFetch<PaymentFileRow[]>("/api/v1/payment-files", { token });
}
export function createPaymentFile(token: string, runId: string, provider: string) {
  return apiFetch<PaymentFileRow>(`/api/v1/pay-runs/${runId}/payment-files`, { method: "POST", body: { provider }, token });
}
export function releasePaymentFile(token: string, id: string) {
  return apiFetch<PaymentFileRow>(`/api/v1/payment-files/${id}/releases`, { method: "POST", body: {}, token });
}
export function rejectPaymentFileItems(token: string, id: string, items: { employmentId: string; reason: string }[]) {
  return apiFetch<{ status: string; rejected: { employmentId: string }[]; accepted: { employmentId: string; status: string }[] }>(`/api/v1/payment-files/${id}/rejections`, { method: "POST", body: { items }, token });
}

export type CloseChecklistItem = { code: string; required: boolean; status: string; message: string };
export type CloseChecklist = { periodId: string; canClose: boolean; items: CloseChecklistItem[] };
export type PeriodCloseRow = { id: string; status: string; closedBy: string; summaryHash: string | null; summary: { runIds: string[]; totals?: { gross: number; deductions: number; net: number }; waivers?: { code: string; reason: string; approvedBy: string }[] } | null };

export type PayrollMappingRow = { id: string; element: string; accountCode: string; effectiveFrom: string; effectiveTo: string | null; active: boolean };

export function fetchPayrollAccountMappings(token: string) {
  return apiFetch<PayrollMappingRow[]>("/api/v1/payroll-account-mappings", { token });
}
export function createPayrollAccountMapping(token: string, body: { element: string; accountCode: string; effectiveFrom: string }) {
  return apiFetch<PayrollMappingRow>("/api/v1/payroll-account-mappings", { method: "POST", body, token });
}

export function fetchCloseChecklist(token: string, periodId: string) {
  return apiFetch<CloseChecklist>(`/api/v1/pay-periods/${periodId}/close-checklist`, { token });
}
export function closePayPeriod(token: string, periodId: string, body: { acknowledgeLater?: boolean | null; waivers: { code: string; reason: string }[] }) {
  return apiFetch<PeriodCloseRow>(`/api/v1/pay-periods/${periodId}/closures`, { method: "POST", body, token });
}
export function reopenPayPeriod(token: string, periodId: string, reason: string) {
  return apiFetch<PeriodCloseRow>(`/api/v1/pay-periods/${periodId}/reopenings`, { method: "POST", body: { reason }, token });
}

export type CredentialRow = { id: string; employmentId: string; code: string; name: string; issuedOn: string | null; expiresOn: string | null; status: string; effectiveStatus: string; waivedBy: string | null };

export function fetchCredentials(token: string, employmentId?: string | null) {
  return apiFetch<CredentialRow[]>(withQuery("/api/v1/credentials", { employmentId }), { token });
}
export function createCredential(token: string, body: { employmentId: string; code: string; name: string; issuedBy?: string | null; issuedOn?: string | null; expiresOn?: string | null; evidenceRef?: string | null }) {
  return apiFetch<CredentialRow>("/api/v1/credentials", { method: "POST", body, token });
}
export function waiveCredential(token: string, id: string, reason: string) {
  return apiFetch<CredentialRow>(`/api/v1/credentials/${id}/waivers`, { method: "POST", body: { reason }, token });
}

export type WorkforcePlanRow = { id: string; code: string; name: string; horizonStart: string; horizonEnd: string; status: string; lines: { positionCode: string; title: string; fte: number; promoted: boolean; promotedPositionId: string | null }[] };
export type AllocationSetRow = { id: string; code: string; name: string; unit: string; status: string; lines: { costCenter: string; pct: number; heldForReview: boolean }[] };
export type AnalyticsBucket = { key: string; count: number; value: number | null; suppressed: boolean };

export function fetchWorkforcePlans(token: string) {
  return apiFetch<WorkforcePlanRow[]>("/api/v1/workforce-plans", { token });
}
export function createWorkforcePlan(token: string, body: { code: string; name: string; horizonStart: string; horizonEnd: string; lines: { positionCode: string; title: string; fte: number }[] }) {
  return apiFetch<WorkforcePlanRow>("/api/v1/workforce-plans", { method: "POST", body, token });
}
export function promoteWorkforcePlan(token: string, id: string) {
  return apiFetch<{ createdPositions: { code: string }[] }>(`/api/v1/workforce-plans/${id}/promotions`, { method: "POST", body: {}, token });
}
export function fetchAllocationSets(token: string) {
  return apiFetch<AllocationSetRow[]>("/api/v1/allocation-sets", { token });
}
export function createAllocationSet(token: string, body: { code: string; name: string; unit: string; lines: { costCenter: string; pct: number }[] }) {
  return apiFetch<AllocationSetRow>("/api/v1/allocation-sets", { method: "POST", body, token });
}
export function approveAllocationSet(token: string, id: string) {
  return apiFetch<AllocationSetRow>(`/api/v1/allocation-sets/${id}/approvals`, { method: "POST", body: {}, token });
}
export function reconcileAllocationSet(token: string, id: string, runId: string) {
  return apiFetch<{ reconciles: boolean; buckets: { costCenter: string; allocated: number; actual: number; variance: number }[] }>(`/api/v1/allocation-sets/${id}/reconciliations?runId=${runId}`, { token });
}
export function fetchAnalytics(token: string, metric: string) {
  return apiFetch<{ metric: string; threshold: number; buckets: AnalyticsBucket[]; total: { value: number | null; suppressed: boolean } }>(`/api/v1/people-analytics/${metric}`, { token });
}
