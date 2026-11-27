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

// --- Endpoint helpers ---------------------------------------------------------

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
