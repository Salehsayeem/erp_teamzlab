// MFG service module — the single API boundary for the manufacturing screens
// (BOM revisions, production orders, shop-floor reporting, close reconciliation).
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

export type BomLineRow = { id: string; componentSku: string; qtyPer: number; unit: string; issuePolicy: string; unitCost: number };

export type BomRow = {
  id: string;
  outputSku: string;
  revision: number;
  status: string;
  outputQtyPer: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  createdBy: string;
  createdAt: string;
  lines: BomLineRow[];
};

export type ProductionOrderRow = {
  id: string;
  orderNo: string;
  outputSku: string;
  unit: string;
  status: string;
  demandedQty: number;
  plannedQty: number;
  releasedQty: number;
  completedQty: number;
  remainingQty: number;
  overTolerancePct: number;
  plannedBomRevisionId: string | null;
  releasedBomRevisionId: string | null;
  cancelReason: string | null;
  closedBy: string | null;
  closedAt: string | null;
  reopenedBy: string | null;
  reopenedAt: string | null;
  createdBy: string;
  createdAt: string;
};

export type CloseCheckItem = { code: string; detail: string; qty: number };
export type CloseCheckScrapRow = {
  scrapId: string;
  itemSku: string;
  qty: number;
  unitCost: number;
  reasonCode: string;
  disposition: string;
  value: number;
  journalId: string | null;
};

export type CloseCheckRow = {
  orderId: string;
  orderNo: string;
  status: string;
  open: boolean;
  items: CloseCheckItem[];
  wipDebits: number;
  wipCredits: number;
  wipRemainder: number;
  scrap: CloseCheckScrapRow[];
};

// --- BOM revisions (014/015: immutable approved revisions) -------------------

export function fetchBoms(token: string, outputSku?: string, status?: string) {
  return apiFetch<BomRow[]>(withQuery("/api/v1/boms", { outputSku, status }), { token });
}
export function createBom(token: string, body: {
  outputSku: string; revision: number; outputQtyPer: number; effectiveFrom: string; effectiveTo?: string | null;
}) {
  return apiFetch<BomRow>("/api/v1/boms", { method: "POST", body, token });
}
export function addBomLine(token: string, bomId: string, body: {
  componentSku: string; qtyPer: number; unit: string; issuePolicy: string; unitCost: number;
}) {
  return apiFetch<BomLineRow>(`/api/v1/boms/${bomId}/lines`, { method: "POST", body, token });
}
export function approveBom(token: string, bomId: string, reason: string | null) {
  return apiFetch<BomRow>(`/api/v1/boms/${bomId}/approvals`, { method: "POST", body: { reason }, token });
}
export function retireBom(token: string, bomId: string, reason: string | null) {
  return apiFetch<BomRow>(`/api/v1/boms/${bomId}/retirements`, { method: "POST", body: { reason }, token });
}

// --- Production orders (007/008/010/013/022) -----------------------------------

export function fetchProductionOrders(token: string, status?: string, outputSku?: string) {
  return apiFetch<ProductionOrderRow[]>(withQuery("/api/v1/production-orders", { status, outputSku }), { token });
}
export function createProductionOrder(token: string, body: {
  outputSku: string; demandedQty: number; unit: string; overTolerancePct: number;
}) {
  return apiFetch<ProductionOrderRow>("/api/v1/production-orders", { method: "POST", body, token });
}
export function transitionOrder(token: string, id: string, body: {
  status: string; newPlannedQty?: number | null; reason?: string | null;
}) {
  return apiFetch<{ order: ProductionOrderRow; amendment: { id: string; field: string; oldValue: string; newValue: string; reason: string } | null }>(
    `/api/v1/production-orders/${id}/transitions`, { method: "POST", body, token });
}
export function releaseOrder(token: string, id: string, reason: string | null) {
  return apiFetch<ProductionOrderRow>(`/api/v1/production-orders/${id}/releases`, { method: "POST", body: { reason }, token });
}

// --- Shop-floor reporting (042/043/041/067) -------------------------------------

export function issueMaterial(token: string, id: string, body: { componentSku: string; qty: number }) {
  return apiFetch<unknown>(`/api/v1/production-orders/${id}/material-issues`, { method: "POST", body, token });
}
export function returnIssue(token: string, id: string, body: { issueId: string; qty: number }) {
  return apiFetch<unknown>(`/api/v1/production-orders/${id}/issue-returns`, { method: "POST", body, token });
}
export function reportCompletion(token: string, id: string, body: { qty: number; note?: string | null }) {
  return apiFetch<unknown>(`/api/v1/production-orders/${id}/completions`, { method: "POST", body, token });
}
export function reportScrap(token: string, id: string, body: {
  itemSku: string; qty: number; reasonCode: string; disposition: string;
}) {
  return apiFetch<unknown>(`/api/v1/production-orders/${id}/scrap-reports`, { method: "POST", body, token });
}

// --- Close reconciliation (013 AC1: the machine-readable item list) --------------
// GET /close-check is the same {code, detail, qty} shape the close gate lifts
// into error.items — the UI renders readiness from here (api.ts is off-limits
// for edits, so gate 409s re-read this authoritative view instead).

export function fetchCloseCheck(token: string, id: string) {
  return apiFetch<CloseCheckRow>(`/api/v1/production-orders/${id}/close-check`, { token });
}
export function closeOrder(token: string, id: string, residualAcknowledgment: string | null) {
  return apiFetch<{ order: ProductionOrderRow; residualJournal: { id: string; memo: string } | null }>(
    `/api/v1/production-orders/${id}/closures`, { method: "POST", body: { residualAcknowledgment }, token });
}
export function reopenOrder(token: string, id: string, reason: string) {
  return apiFetch<{ order: ProductionOrderRow }>(`/api/v1/production-orders/${id}/reopenings`, { method: "POST", body: { reason }, token });
}
