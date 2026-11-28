// FINANCE service module — the single API boundary for the finance admin screens
// (currency/FX master, dimensions, number-series). Components never call fetch
// directly (CLAUDE.md services rule); this file imports `apiFetch` from ./api
// and never edits it.

import { apiFetch } from "./api";

// --- Domain types (camelCase mirrors of the api projections) -----------------

export type Currency = { id: string; code: string; name: string; decimalScale: number; active: boolean };

export type FxRate = {
  id: string;
  baseCurrency: string;
  quoteCurrency: string;
  rateDate: string;
  rateType: string;
  rate: number;
  precision: number;
  source: string;
  retrievedAt: string;
  status: string;
  createdBy: string;
  approvedBy: string | null;
  approvedAt: string | null;
};

export type DimensionSet = { id: string; code: string; name: string; required: boolean; active: boolean };

export type DimensionValue = {
  id: string;
  setCode: string;
  value: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  active: boolean;
};

export type NumberSeriesRow = { prefix: string; lastValue: number | null; lastNumber: string; voidedCount: number };

// FINANCE-deep P1 (004/062/063): fiscal calendars, period status board, close checklists
export type CalendarPeriod = { name: string; startDate: string; endDate: string; isAdjustment: boolean };

export type FiscalCalendar = {
  id: string;
  name: string;
  year: number;
  pattern: string;
  startDate: string;
  endDate: string;
  activated: boolean;
  activatedBy: string | null;
  activatedAt: string | null;
  periods: CalendarPeriod[];
};

export type FiscalPeriodRow = {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  status: string;
  closedBy: string | null;
  closedAt: string | null;
  reopenReason: string | null;
  isAdjustment: boolean;
};

export type CloseTaskRow = {
  id: string;
  periodId: string;
  title: string;
  mandatory: boolean;
  status: string;
  dueDate: string | null;
  dependsOnTaskId: string | null;
  evidenceRef: string | null;
  waiverReason: string | null;
  naReason: string | null;
  completedBy: string | null;
  completedAt: string | null;
};

export type CloseTaskInput = { title: string; mandatory: boolean; dueDate?: string; dependsOnTaskId?: string };

// --- Endpoint helpers ---------------------------------------------------------

export function fetchFiscalPeriods(token: string, status?: string) {
  const qs = status ? `?status=${encodeURIComponent(status)}` : "";
  return apiFetch<FiscalPeriodRow[]>(`/api/v1/fiscal-periods${qs}`, { token });
}

export function fetchFiscalCalendars(token: string) {
  return apiFetch<FiscalCalendar[]>("/api/v1/fiscal-calendars", { token });
}

export function createFiscalCalendar(
  token: string,
  body: { name: string; year: number; pattern: string; startDate: string },
) {
  return apiFetch<FiscalCalendar>("/api/v1/fiscal-calendars", { method: "POST", body, token });
}

export function activateFiscalCalendar(token: string, id: string) {
  return apiFetch<FiscalCalendar>(`/api/v1/fiscal-calendars/${encodeURIComponent(id)}/activations`, {
    method: "POST",
    body: {},
    token,
  });
}

export function closeFiscalPeriod(token: string, id: string, body: { reason?: string; waiveBlockers?: boolean }) {
  return apiFetch<FiscalPeriodRow>(`/api/v1/fiscal-periods/${encodeURIComponent(id)}/close`, {
    method: "POST",
    body,
    token,
  });
}

export function reopenFiscalPeriod(token: string, id: string, body: { reason: string }) {
  return apiFetch<FiscalPeriodRow>(`/api/v1/fiscal-periods/${encodeURIComponent(id)}/reopen`, {
    method: "POST",
    body,
    token,
  });
}

export function fetchCloseChecklists(token: string, periodId?: string) {
  const qs = periodId ? `?periodId=${encodeURIComponent(periodId)}` : "";
  return apiFetch<CloseTaskRow[]>(`/api/v1/close-checklists${qs}`, { token });
}

export function createCloseChecklist(token: string, body: { periodId: string; tasks: CloseTaskInput[] }) {
  return apiFetch<CloseTaskRow[]>("/api/v1/close-checklists", { method: "POST", body, token });
}

export function addCloseTask(token: string, periodId: string, body: CloseTaskInput) {
  return apiFetch<CloseTaskRow>(`/api/v1/close-checklists/${encodeURIComponent(periodId)}/tasks`, {
    method: "POST",
    body,
    token,
  });
}

export function completeCloseTask(
  token: string,
  periodId: string,
  taskId: string,
  body: { evidenceRef?: string; waiverReason?: string },
) {
  return apiFetch<CloseTaskRow>(
    `/api/v1/close-checklists/${encodeURIComponent(periodId)}/tasks/${encodeURIComponent(taskId)}/completions`,
    { method: "POST", body, token },
  );
}

export function naCloseTask(token: string, periodId: string, taskId: string, body: { reason: string }) {
  return apiFetch<CloseTaskRow>(
    `/api/v1/close-checklists/${encodeURIComponent(periodId)}/tasks/${encodeURIComponent(taskId)}/na`,
    { method: "POST", body, token },
  );
}

export function fetchCurrencies(token: string) {
  return apiFetch<Currency[]>("/api/v1/currencies", { token });
}

export function createCurrency(token: string, body: { code: string; name: string; decimalScale: number; active?: boolean }) {
  return apiFetch<Currency>("/api/v1/currencies", { method: "POST", body, token });
}

export function fetchFxRates(token: string) {
  return apiFetch<FxRate[]>("/api/v1/fx-rates", { token });
}

export function approveFxRate(token: string, id: string) {
  return apiFetch<FxRate>(`/api/v1/fx-rates/${id}/approvals`, { method: "POST", body: {}, token });
}

export function fetchDimensionSets(token: string) {
  return apiFetch<DimensionSet[]>("/api/v1/dimension-sets", { token });
}

export function createDimensionSet(token: string, body: { code: string; name: string; required?: boolean }) {
  return apiFetch<DimensionSet>("/api/v1/dimension-sets", { method: "POST", body, token });
}

export function fetchDimensionValues(token: string, setCode?: string) {
  const qs = setCode ? `?setCode=${encodeURIComponent(setCode)}` : "";
  return apiFetch<DimensionValue[]>(`/api/v1/dimension-values${qs}`, { token });
}

export function createDimensionValue(
  token: string,
  body: { setCode: string; value: string; effectiveFrom: string; effectiveTo?: string; active?: boolean },
) {
  return apiFetch<DimensionValue>("/api/v1/dimension-values", { method: "POST", body, token });
}

export function fetchNumberSeries(token: string) {
  return apiFetch<NumberSeriesRow[]>("/api/v1/number-series", { token });
}

export function voidNumber(token: string, prefix: string, body: { number: string; reason: string }) {
  return apiFetch<unknown>(`/api/v1/number-series/${encodeURIComponent(prefix)}/voids`, { method: "POST", body, token });
}
