// Single API boundary — components never call fetch directly (CLAUDE.md services rule).

export type ApiResult<T> = { ok: true; data: T } | { ok: false; code: string; message: string };

type ApiOptions = { method?: string; body?: unknown; token?: string | null };

export const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5000";

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

function asString(v: unknown, fallback: string): string {
  return typeof v === "string" && v.length > 0 ? v : fallback;
}

/** Narrow the API envelope `{ success, data | error{code,message} }` from unknown JSON. */
function parseEnvelope<T>(json: unknown, fallbackCode: string): ApiResult<T> {
  if (!isRecord(json) || typeof json.success !== "boolean") {
    return { ok: false, code: fallbackCode, message: "Unexpected response from the server." };
  }
  if (json.success) return { ok: true, data: json.data as T };
  const err = isRecord(json.error) ? json.error : {};
  return { ok: false, code: asString(err.code, "ERROR"), message: asString(err.message, "Request failed.") };
}

export async function apiFetch<T>(path: string, opts: ApiOptions = {}): Promise<ApiResult<T>> {
  try {
    const headers: Record<string, string> = { "X-Correlation-Id": crypto.randomUUID() };
    if (opts.token) headers.Authorization = `Bearer ${opts.token}`;
    if (opts.body !== undefined) headers["Content-Type"] = "application/json";
    const res = await fetch(API_BASE + path, {
      method: opts.method ?? "GET",
      headers,
      body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
      cache: "no-store",
    });
    const json: unknown = await res.json();
    return parseEnvelope<T>(json, `HTTP_${res.status}`);
  } catch {
    return { ok: false, code: "NETWORK", message: "Network request failed." };
  }
}

// --- Domain types (mirror apps/api Models.cs, camelCase over the wire) ---

export type SessionUser = { token: string; id: string; email: string; displayName: string };
export type LegalEntity = { id: string; name: string; jurisdiction: string; currency: string; defaultLocale: string };
export type Me = {
  user: { id: string; email: string; name: string };
  role: string;
  permissions: string[];
  tenant: { id: string; name: string };
  legalEntity: LegalEntity;
};
export type Supplier = { id: string; name: string; taxId: string; email: string; createdAt: string; createdBy: string };
export type PoLine = { id: string; itemSku: string; description: string; qty: number; unitPrice: number };
export type PurchaseOrder = {
  id: string;
  supplierId: string;
  status: string;
  lines: PoLine[];
  createdBy: string;
  createdAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  total: number;
};
export type JournalLine = { accountCode: string; accountName: string; debit: number; credit: number };
export type Journal = {
  id: string;
  status: string;
  memo: string;
  lines: JournalLine[];
  createdAt: string;
  createdBy: string;
  reversalOfId: string | null;
};
export type Warehouse = { id: string; code: string; name: string };
export type StockLocation = { id: string; warehouseId: string; code: string; name: string };
export type StockRow = {
  warehouseId: string;
  warehouseCode: string;
  locationId: string;
  locationCode: string;
  itemSku: string;
  qtyOnHand: number;
};
export type StockTransfer = {
  id: string;
  fromLocationId: string;
  toLocationId: string;
  itemSku: string;
  qty: number;
  status: string;
  createdAt: string;
  createdBy: string;
};
export type CountLine = { itemSku: string; countedQty: number; systemQty: number };
export type StockCount = {
  id: string;
  locationId: string;
  status: string;
  items: CountLine[];
  createdBy: string;
  createdAt: string;
  closedBy: string | null;
  closedAt: string | null;
};
export type StockAdjustment = {
  id: string;
  countId: string;
  itemSku: string;
  locationId: string;
  qtyVariance: number;
  unitCost: number;
  amount: number;
  journalId: string;
  createdAt: string;
  createdBy: string;
};
export type CountCloseResult = { count: StockCount; adjustments: StockAdjustment[] };
export type ValuationRow = { itemSku: string; qtyOnHand: number; unitCost: number; value: number };

// --- Endpoint helpers ---

export function exchangeToken(email: string) {
  return apiFetch<SessionUser>("/api/v1/auth/token-exchange", { method: "POST", body: { email } });
}
export function fetchMe(token: string) {
  return apiFetch<Me>("/api/v1/me", { token });
}
export function fetchSuppliers(token: string) {
  return apiFetch<Supplier[]>("/api/v1/suppliers", { token });
}
export function createSupplier(token: string, body: { name: string; taxId: string; email: string }) {
  return apiFetch<Supplier>("/api/v1/suppliers", { method: "POST", body, token });
}
export function fetchPurchaseOrders(token: string) {
  return apiFetch<PurchaseOrder[]>("/api/v1/purchase-orders", { token });
}
export function submitPo(token: string, id: string) {
  return apiFetch<PurchaseOrder>(`/api/v1/purchase-orders/${id}/submit`, { method: "POST", token });
}
export function approvePo(token: string, id: string, reason: string | null) {
  return apiFetch<PurchaseOrder>(`/api/v1/purchase-orders/${id}/approvals`, {
    method: "POST",
    body: { reason },
    token,
  });
}
export function postReceipt(token: string, body: { purchaseOrderId: string; itemSku: string; qty: number }) {
  return apiFetch<unknown>("/api/v1/receipts", { method: "POST", body, token });
}
export function fetchJournals(token: string) {
  return apiFetch<Journal[]>("/api/v1/journals", { token });
}
export function reverseJournal(token: string, id: string) {
  return apiFetch<Journal>(`/api/v1/journals/${id}/reversals`, { method: "POST", token });
}
export function fetchCount(token: string, path: string) {
  return apiFetch<unknown[]>(path, { token });
}

// --- Inventory ---

export function fetchWarehouses(token: string) {
  return apiFetch<Warehouse[]>("/api/v1/warehouses", { token });
}
export function createWarehouse(token: string, body: { code: string; name: string }) {
  return apiFetch<Warehouse>("/api/v1/warehouses", { method: "POST", body, token });
}
export function fetchLocations(token: string) {
  return apiFetch<StockLocation[]>("/api/v1/locations", { token });
}
export function createLocation(token: string, body: { warehouseId: string; code: string; name: string }) {
  return apiFetch<StockLocation>("/api/v1/locations", { method: "POST", body, token });
}
export function fetchStock(token: string) {
  return apiFetch<StockRow[]>("/api/v1/stock", { token });
}
export function createTransfer(token: string, body: { fromLocationId: string; toLocationId: string; itemSku: string; qty: number }) {
  return apiFetch<StockTransfer>("/api/v1/stock/transfers", { method: "POST", body, token });
}
export function openCount(token: string, body: { locationId: string; items: { itemSku: string; countedQty: number }[] }) {
  return apiFetch<StockCount>("/api/v1/stock/counts", { method: "POST", body, token });
}
export function closeCount(token: string, id: string) {
  return apiFetch<CountCloseResult>(`/api/v1/stock/counts/${id}/close`, { method: "POST", token });
}
export function fetchValuation(token: string) {
  return apiFetch<ValuationRow[]>("/api/v1/stock/valuation", { token });
}
