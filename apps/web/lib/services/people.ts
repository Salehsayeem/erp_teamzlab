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
