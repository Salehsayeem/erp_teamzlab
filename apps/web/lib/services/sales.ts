// SALES service module — the single API boundary for the sales screens
// (leads → crm → opportunities → invoice documents → collections).
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

export type Lead = {
  id: string;
  company: string | null;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  source: string;
  sourceRef: string | null;
  status: string;
  reasonCode: string | null;
  ownerId: string;
  receivedAt: string;
  taxId: string | null;
  createdBy: string;
  createdAt: string;
};

export type LeadConversion = {
  customer: { id: string; name: string };
  opportunity: { id: string; title: string };
};

export type Interaction = {
  id: string;
  partyType: string;
  partyId: string;
  channel: string;
  subject: string;
  notes: string | null;
  occurredAt: string;
  recordedAt: string;
  createdBy: string;
};

export type CrmCase = {
  id: string;
  customerId: string | null;
  category: string;
  priority: string;
  status: string;
  ownerId: string | null;
  responseDueAt: string;
  resolution: string | null;
  salesOrderId: string | null;
  createdBy: string;
  createdAt: string;
  overdue: boolean;
};

export type OpportunityLine = { itemSku: string; description: string; qty: number; targetPrice: number };

export type Opportunity = {
  id: string;
  customerId: string | null;
  leadId: string | null;
  title: string;
  amount: number;
  currency: string;
  status: string;
  stage: string;
  expectedCloseDate: string | null;
  ownerId: string;
  lossReason: string | null;
  competitor: string | null;
  orderId: string | null;
  createdBy: string;
  createdAt: string;
  total: number;
  lines: OpportunityLine[];
};

export type InvoiceLine = {
  itemSku: string;
  description: string;
  qty: number;
  unitPrice: number;
  lineNet: number;
  taxCodeId: string | null;
  taxCodeVersion: number | null;
  taxAmount: number;
};

export type Invoice = {
  id: string;
  customerId: string;
  salesOrderId: string;
  sourceEventId: string;
  status: string;
  currency: string;
  invoiceDate: string;
  dueDate: string | null;
  paymentTermCode: string;
  subTotal: number;
  taxTotal: number;
  total: number;
  journalId: string | null;
  postedBy: string | null;
  postedAt: string | null;
  createdBy: string;
  createdAt: string;
  openAmount: number;
  stale: boolean;
  lines: InvoiceLine[];
};

export type PostInvoiceResult = {
  invoice: Invoice;
  journal: { id: string; memo: string; status: string };
};

export type AdjustmentLine = {
  itemSku: string;
  description: string;
  qty: number;
  unitPrice: number;
  lineNet: number;
  taxCodeId: string | null;
  taxCodeVersion: number | null;
  taxAmount: number;
};

export type AdjustmentDocument = {
  id: string;
  kind: string;
  invoiceId: string;
  reason: string;
  subTotal: number;
  taxTotal: number;
  total: number;
  status: string;
  journalId: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  createdBy: string;
  createdAt: string;
  lines: AdjustmentLine[];
};

export type WorklistRow = {
  invoiceId: string;
  customerId: string;
  invoiceDate: string;
  dueDate: string;
  daysPastDue: number;
  openAmount: number;
  disputed: boolean;
  suppressed: boolean;
  followUp: boolean;
};

export type CreditExposure = {
  customerId: string;
  creditLimit: number | null;
  openInvoiced: number;
  openOrders: number;
  total: number;
  overLimit: boolean;
  hasData: boolean;
};

export type CustomerRow = {
  id: string;
  name: string;
  taxId: string;
  email: string;
  createdAt: string;
  createdBy: string;
  creditLimit: number | null;
  paymentTermNetDays: number;
  remindersSuppressed: boolean;
};

export type SalesOrderLine = { id: string; itemSku: string; description: string; qty: number; unitPrice: number };

export type SalesOrderRow = {
  id: string;
  customerId: string;
  status: string;
  lines: SalesOrderLine[];
  createdBy: string;
  createdAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  total: number;
};

export type ShipmentResult = {
  shipment: { id: string; itemSku: string; qty: number };
  journal: { id: string; memo: string };
};

export type ReminderRow = {
  id: string;
  invoiceId: string;
  channel: string;
  templateVersion: string | null;
  recipient: string;
  remindedAt: string;
  createdBy: string;
};

export type DisputeRow = {
  id: string;
  invoiceId: string;
  reason: string;
  status: string;
  promiseDueDate: string | null;
  outcome: string | null;
  createdBy: string;
  createdAt: string;
};

// --- Leads (ERP-SALES-003/004/005) -------------------------------------------

export function fetchLeads(token: string, status?: string) {
  return apiFetch<Lead[]>(withQuery("/api/v1/leads", { status }), { token });
}
export function createLead(token: string, body: { company?: string; contactName?: string; email?: string; phone?: string; source: string; sourceRef?: string; taxId?: string }) {
  return apiFetch<Lead>("/api/v1/leads", { method: "POST", body, token });
}
export function transitionLead(token: string, id: string, status: string, reasonCode?: string | null) {
  return apiFetch<Lead>(`/api/v1/leads/${id}/transitions`, { method: "POST", body: { status, reasonCode: reasonCode ?? null }, token });
}
export function convertLead(token: string, id: string) {
  return apiFetch<LeadConversion>(`/api/v1/leads/${id}/conversions`, { method: "POST", body: {}, token });
}

// --- CRM: interactions + cases (ERP-SALES-007/008) ----------------------------

export function fetchInteractions(token: string, partyType?: string, partyId?: string) {
  return apiFetch<Interaction[]>(withQuery("/api/v1/crm/interactions", { partyType, partyId }), { token });
}
export function logInteraction(token: string, body: { partyType: string; partyId: string; channel: string; subject: string; notes?: string; occurredAt: string }) {
  return apiFetch<Interaction>("/api/v1/crm/interactions", { method: "POST", body, token });
}
export function fetchCrmCases(token: string, overdueOnly?: boolean) {
  return apiFetch<CrmCase[]>(withQuery("/api/v1/crm/cases", { overdue: overdueOnly ? "true" : undefined }), { token });
}
export function createCrmCase(token: string, body: { customerId?: string; category: string; priority: string; salesOrderId?: string }) {
  return apiFetch<CrmCase>("/api/v1/crm/cases", { method: "POST", body, token });
}
export function transitionCrmCase(token: string, id: string, status: string, resolution?: string | null) {
  return apiFetch<CrmCase>(`/api/v1/crm/cases/${id}/transitions`, { method: "POST", body: { status, resolution: resolution ?? null }, token });
}

// --- Opportunities (ERP-SALES-015/016/017) ------------------------------------

export function fetchOpportunities(token: string, status?: string) {
  return apiFetch<Opportunity[]>(withQuery("/api/v1/opportunities", { status }), { token });
}
export function addOpportunityLine(token: string, id: string, body: { itemSku: string; description: string; qty: number; targetPrice: number }) {
  return apiFetch<Opportunity>(`/api/v1/opportunities/${id}/lines`, { method: "POST", body, token });
}
export function transitionOpportunity(token: string, id: string, body: { status: string; stage: string; orderId?: string; lossReason?: string; competitor?: string }) {
  return apiFetch<Opportunity>(`/api/v1/opportunities/${id}/transitions`, { method: "POST", body, token });
}

// --- Invoice documents (ERP-SALES-042..053) ------------------------------------

export function draftInvoiceFromOrder(token: string, soId: string, invoiceDate?: string | null) {
  return apiFetch<Invoice>(`/api/v1/sales-orders/${soId}/invoice-drafts`, { method: "POST", body: { invoiceDate: invoiceDate ?? null }, token });
}
export function fetchInvoices(token: string, status?: string, customerId?: string) {
  return apiFetch<Invoice[]>(withQuery("/api/v1/invoices", { status, customerId }), { token });
}
export function postInvoice(token: string, id: string) {
  return apiFetch<PostInvoiceResult>(`/api/v1/invoices/${id}/postings`, { method: "POST", body: {}, token });
}
export function discardInvoice(token: string, id: string) {
  return apiFetch<Invoice>(`/api/v1/invoices/${id}/discards`, { method: "POST", token });
}
export function createAdjustment(token: string, body: { kind: string; invoiceId: string; reason: string; lines: { itemSku: string; qty: number; unitPrice: number; taxCodeId?: string; description?: string }[] }) {
  return apiFetch<AdjustmentDocument>("/api/v1/adjustments", { method: "POST", body, token });
}
export function approveAdjustment(token: string, id: string, reason: string | null) {
  return apiFetch<AdjustmentDocument>(`/api/v1/adjustments/${id}/approvals`, { method: "POST", body: { reason }, token });
}
// Read endpoint landing in parallel (GET /adjustments?invoiceId=) — the UI
// degrades to session-only history when it is not merged yet.
export function fetchAdjustments(token: string, invoiceId: string) {
  return apiFetch<AdjustmentDocument[]>(withQuery("/api/v1/adjustments", { invoiceId }), { token });
}

// --- Collections (ERP-SALES-057/058/059) ---------------------------------------

export function fetchWorklist(token: string) {
  return apiFetch<WorklistRow[]>("/api/v1/collections/worklist", { token });
}
export function logReminder(token: string, body: { invoiceId: string; channel: string; templateVersion?: string; recipient: string }) {
  return apiFetch<ReminderRow>("/api/v1/collections/reminders", { method: "POST", body, token });
}
export function createDispute(token: string, body: { invoiceId: string; reason: string; promiseDueDate?: string | null }) {
  return apiFetch<DisputeRow>("/api/v1/collections/disputes", { method: "POST", body, token });
}
export function resolveDispute(token: string, id: string, outcome: string) {
  return apiFetch<DisputeRow>(`/api/v1/collections/disputes/${id}/resolutions`, { method: "POST", body: { outcome }, token });
}
// Read endpoints landing in parallel — history is best-effort; the worklist
// flags drive the UI when these are not merged yet.
export function fetchInvoiceReminders(token: string, invoiceId: string) {
  return apiFetch<ReminderRow[]>(`/api/v1/invoices/${invoiceId}/reminders`, { token });
}
export function fetchInvoiceDisputes(token: string, invoiceId: string) {
  return apiFetch<DisputeRow[]>(`/api/v1/invoices/${invoiceId}/disputes`, { token });
}

// --- Credit exposure (ERP-SALES-057, credit:read) -------------------------------

export function fetchCreditExposure(token: string, customerId: string) {
  return apiFetch<CreditExposure>(`/api/v1/customers/${customerId}/credit-exposure`, { token });
}

// --- Sales orders (order-to-cash pilot) -----------------------------------------

export function fetchSalesOrders(token: string) {
  return apiFetch<SalesOrderRow[]>("/api/v1/sales-orders", { token });
}
export function createSalesOrder(token: string, body: { customerId: string; lines: { itemSku: string; description: string; qty: number; unitPrice: number }[] }) {
  return apiFetch<SalesOrderRow>("/api/v1/sales-orders", { method: "POST", body, token });
}
export function submitSalesOrder(token: string, id: string) {
  return apiFetch<SalesOrderRow>(`/api/v1/sales-orders/${id}/submit`, { method: "POST", token });
}
export function approveSalesOrder(token: string, id: string, reason: string | null) {
  return apiFetch<SalesOrderRow>(`/api/v1/sales-orders/${id}/approvals`, { method: "POST", body: { reason }, token });
}
export function shipSalesOrder(token: string, id: string, itemSku: string, qty: number) {
  return apiFetch<ShipmentResult>(`/api/v1/sales-orders/${id}/shipments`, { method: "POST", body: { itemSku, qty }, token });
}
export function releaseCreditHold(token: string, id: string) {
  return apiFetch<SalesOrderRow>(`/api/v1/sales-orders/${id}/credit-releases`, { method: "POST", body: {}, token });
}

// --- Customers -------------------------------------------------------------------

export function fetchCustomers(token: string) {
  return apiFetch<CustomerRow[]>("/api/v1/customers", { token });
}
export function createCustomer(token: string, body: { name: string; taxId: string; email: string; creditLimit?: number; paymentTermNetDays?: number; remindersSuppressed?: boolean }) {
  return apiFetch<CustomerRow>("/api/v1/customers", { method: "POST", body, token });
}
