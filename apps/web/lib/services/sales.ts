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
  activeHolds: number;
  pendingAmendment: boolean;
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

// --- Quotes (ERP-SALES-021/022/023/030) -----------------------------------------

export type QuotePriceEvidence = {
  itemSku: string;
  priceBookId: string | null;
  rule: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  baseValue: number;
  sourceRef: string;
};

export type QuoteLine = { itemSku: string; description: string; qty: number; unitPrice: number; discountPct: number };

export type QuoteApprovalRow = { approverId: string; status: string; reason: string | null; createdAt: string };

export type QuoteRevisionRow = {
  id: string;
  quoteId: string;
  revision: number;
  status: string;
  subTotal: number;
  discountTotal: number;
  taxTotal: number;
  total: number;
  priceEvidence: QuotePriceEvidence[] | null;
  lines: QuoteLine[];
  approvals: QuoteApprovalRow[];
  approvedBy: string | null;
  approvedAt: string | null;
  createdBy: string;
  createdAt: string;
};

export type QuoteAcceptanceRow = {
  id: string;
  quoteId: string;
  revisionId: string;
  revision: number;
  method: string;
  externalEventId: string | null;
  evidenceRef: string | null;
  acceptedBy: string;
  acceptedAt: string;
  idempotent: boolean;
};

export type QuoteRow = {
  id: string;
  customerId: string;
  currency: string;
  terms: string | null;
  expiresAt: string | null;
  status: string;
  revision: number;
  revisionStatus: string | null;
  subTotal: number | null;
  discountTotal: number | null;
  taxTotal: number | null;
  total: number | null;
  priceEvidence: QuotePriceEvidence[] | null;
  createdBy: string;
  createdAt: string;
};

export type QuoteDetail = QuoteRow & {
  acceptedRevision: number | null;
  revisions: QuoteRevisionRow[];
  acceptances: QuoteAcceptanceRow[];
};

export type QuoteLineInput = { itemSku: string; qty: number; unitPrice?: number; discountPct?: number };

export function listQuotes(token: string) {
  return apiFetch<QuoteRow[]>("/api/v1/quotes", { token });
}
export function getQuote(token: string, id: string) {
  return apiFetch<QuoteDetail>(`/api/v1/quotes/${id}`, { token });
}
export function createQuote(token: string, body: { customerId: string; currency: string; lines: QuoteLineInput[]; terms?: string; expiresAt?: string }) {
  return apiFetch<QuoteRow>("/api/v1/quotes", { method: "POST", body, token });
}
export function createQuoteRevision(token: string, id: string, body: { lines: QuoteLineInput[]; rationale?: string }) {
  return apiFetch<QuoteDetail>(`/api/v1/quotes/${id}/revisions`, { method: "POST", body, token });
}
export function approveQuoteRevision(token: string, id: string, revision: number, reason: string | null) {
  return apiFetch<QuoteRevisionRow>(`/api/v1/quotes/${id}/revisions/${revision}/approvals`, { method: "POST", body: { reason }, token });
}
export function acceptQuoteRevision(token: string, id: string, revision: number, body: { method: string; externalEventId?: string; evidenceRef?: string }) {
  return apiFetch<QuoteAcceptanceRow>(`/api/v1/quotes/${id}/revisions/${revision}/acceptances`, { method: "POST", body, token });
}
export function revalidateQuote(token: string, id: string, body: { expiresAt: string; reason: string }) {
  return apiFetch<QuoteDetail>(`/api/v1/quotes/${id}/revalidations`, { method: "POST", body, token });
}

// --- Price books + contract prices (ERP-SALES-024/025/026) -----------------------

export type PriceBookEntryRow = {
  itemSku: string;
  minQty: number;
  unitPrice: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  rule: string | null;
};

export type PriceBookRow = {
  id: string;
  name: string;
  currency: string;
  precedence: number;
  createdBy: string;
  createdAt: string;
  entries: PriceBookEntryRow[];
};

export function listPriceBooks(token: string) {
  return apiFetch<PriceBookRow[]>("/api/v1/price-books", { token });
}
export function createPriceBook(token: string, body: { name: string; currency: string; precedence: number }) {
  return apiFetch<PriceBookRow>("/api/v1/price-books", { method: "POST", body, token });
}
export function addBookEntry(token: string, id: string, body: { itemSku: string; minQty: number; unitPrice: number; effectiveFrom: string; effectiveTo?: string; rule?: string }) {
  return apiFetch<PriceBookRow>(`/api/v1/price-books/${id}/entries`, { method: "POST", body, token });
}
export function createContractPrice(token: string, body: { customerId: string; itemSku: string; minQty: number; unitPrice: number; effectiveFrom: string; effectiveTo?: string }) {
  return apiFetch<PriceBookEntryRow>("/api/v1/customer-contract-prices", { method: "POST", body, token });
}

// --- Customer 360 (ERP-SALES-012/013/014) ----------------------------------------

export type HierarchyInfo = {
  parentId: string | null;
  childrenIds: string[];
  includedEntityIds: string[];
  rollupOpenOrderTotal: number;
};

export type OnboardingCheckRow = {
  checkCode: string;
  mandatory: boolean;
  status: string;
  metAt: string | null;
  metBy: string | null;
};

export type Customer360 = {
  id: string;
  name: string;
  taxId: string;
  email: string;
  creditLimit: number | null;
  paymentTermNetDays: number;
  remindersSuppressed: boolean;
  mergedIntoId: string | null;
  hierarchy: HierarchyInfo;
  openOrders: { id: string; status: string; total: number }[];
  openInvoiceCount: number;
  openInvoiceTotal: number;
  exposure: CreditExposure | null;
  onboardingChecks: OnboardingCheckRow[];
};

export type MergePreviewRow = {
  id: string;
  survivorId: string;
  mergedId: string;
  reason: string;
  status: string;
  reassignments: Record<string, number>;
  requestedBy: string;
  requestedAt: string;
  executedBy: string | null;
  executedAt: string | null;
};

export function getCustomer360(token: string, id: string) {
  return apiFetch<Customer360>(`/api/v1/customers/${id}`, { token });
}
export function setCustomerHierarchy(token: string, id: string, parentId: string | null) {
  return apiFetch<{ customerId: string; parentId: string | null }>(`/api/v1/customers/${id}/hierarchy`, { method: "POST", body: { parentId }, token });
}
export function addOnboardingCheck(token: string, id: string, body: { checkCode: string; mandatory: boolean }) {
  return apiFetch<OnboardingCheckRow>(`/api/v1/customers/${id}/onboarding-checks`, { method: "POST", body, token });
}
export function markOnboardingCheckMet(token: string, id: string, checkCode: string) {
  return apiFetch<OnboardingCheckRow>(`/api/v1/customers/${id}/onboarding-checks/${checkCode}/met`, { method: "POST", token });
}
export function mergePreview(token: string, body: { survivorId: string; mergedId: string; reason: string }) {
  return apiFetch<MergePreviewRow>("/api/v1/customer-merges/preview", { method: "POST", body, token });
}
export function mergeExecute(token: string, previewId: string) {
  return apiFetch<MergePreviewRow>(`/api/v1/customer-merges/${previewId}/executions`, { method: "POST", body: {}, token });
}

// --- Order lifecycle (ERP-SALES-031..040) -----------------------------------------

export type OrderLineDetail = {
  itemSku: string;
  description: string;
  qty: number;
  unitPrice: number;
  expectedDate: string | null;
  fulfilledQty: number;
  remainingQty: number;
  backorderQty: number;
};

export type OrderHoldRow = {
  id: string;
  holdType: string;
  status: string;
  reason: string;
  source: string;
  setBy: string;
  setAt: string;
  clearedBy: string | null;
  clearedAt: string | null;
};

export type OrderAmendmentRow = {
  id: string;
  revision: number;
  status: string;
  rationale: string;
  lines: { itemSku: string; description: string; qty: number; unitPrice: number }[];
  createdBy: string;
  createdAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
};

export type OrderCancellationRow = {
  id: string;
  scope: string;
  reason: string;
  cancelledBy: string;
  cancelledAt: string;
};

export type SalesOrderDetail = {
  id: string;
  customerId: string;
  status: string;
  createdBy: string;
  createdAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  sourceQuoteRevisionId: string | null;
  total: number;
  lines: OrderLineDetail[];
  holds: OrderHoldRow[];
  amendments: OrderAmendmentRow[];
  cancellations: OrderCancellationRow[];
};

export function getSalesOrder(token: string, id: string) {
  return apiFetch<SalesOrderDetail>(`/api/v1/sales-orders/${id}`, { token });
}
export function placeHold(token: string, id: string, body: { holdType: string; reason: string; source?: string }) {
  return apiFetch<SalesOrderDetail>(`/api/v1/sales-orders/${id}/holds`, { method: "POST", body, token });
}
export function releaseHold(token: string, id: string, holdId: string, reason: string | null) {
  return apiFetch<SalesOrderDetail>(`/api/v1/sales-orders/${id}/holds/${holdId}/releases`, { method: "POST", body: { reason }, token });
}
export function createAmendment(token: string, id: string, body: { lines: { itemSku: string; description: string; qty: number; unitPrice: number }[]; rationale: string }) {
  return apiFetch<OrderAmendmentRow>(`/api/v1/sales-orders/${id}/amendments`, { method: "POST", body, token });
}
export function approveAmendment(token: string, id: string, amendmentId: string, reason: string | null) {
  return apiFetch<OrderAmendmentRow>(`/api/v1/sales-orders/${id}/amendments/${amendmentId}/approvals`, { method: "POST", body: { reason }, token });
}
export function cancelOrder(token: string, id: string, body: { reason: string; scope: string }) {
  return apiFetch<SalesOrderDetail>(`/api/v1/sales-orders/${id}/cancellations`, { method: "POST", body, token });
}
export function createSchedule(token: string, body: { customerId: string; frequency: string; nextRunDate: string; lines: { itemSku: string; qty: number }[] }) {
  return apiFetch<unknown>("/api/v1/recurring-schedules", { method: "POST", body, token });
}
export function pauseSchedule(token: string, id: string) {
  return apiFetch<unknown>(`/api/v1/recurring-schedules/${id}/pauses`, { method: "POST", body: {}, token });
}
export function generateSchedule(token: string, id: string, period: string) {
  return apiFetch<unknown>(`/api/v1/recurring-schedules/${id}/generations`, { method: "POST", body: { period }, token });
}

// --- Forecast (ERP-SALES-017/018/020) ----------------------------------------------

export type ForecastSubmissionRow = {
  id: string;
  owner: string;
  period: string;
  category: string;
  overrideValue: number | null;
  version: number;
  submittedBy: string;
  submittedAt: string;
};

export type ForecastReport = {
  period: string;
  unweighted: { count: number; total: number };
  weighted: { total: number };
  override: { count: number; total: number };
  submissions: ForecastSubmissionRow[];
  unclassified: { count: number };
};

export type ForecastSnapshotRow = {
  id: string;
  period: string;
  status: string;
  payload: { unweightedTotal?: number } | null;
  generatedBy: string;
  generatedAt: string;
};

export function submitForecast(token: string, body: { owner: string; period: string; category: string; overrideValue?: number }) {
  return apiFetch<ForecastSubmissionRow>("/api/v1/forecast-submissions", { method: "POST", body, token });
}
export function getForecast(token: string, period: string) {
  return apiFetch<ForecastReport>(withQuery("/api/v1/forecast", { period }), { token });
}
export function createSnapshot(token: string, period: string) {
  return apiFetch<ForecastSnapshotRow>("/api/v1/forecast-snapshots", { method: "POST", body: { period }, token });
}
export function listSnapshots(token: string, period: string) {
  return apiFetch<ForecastSnapshotRow[]>(withQuery("/api/v1/forecast-snapshots", { period }), { token });
}

// --- E-invoice + delivery (ERP-SALES-054/055) — services only; screen wiring lands later ---

export type EinvoiceSubmissionRow = {
  id: string;
  invoiceId: string;
  jurisdiction: string;
  schemaVersion: string;
  correlationId: string;
  status: string;
  submittedBy: string;
  submittedAt: string;
};

export type InvoiceDeliveryRow = {
  id: string;
  channel: string;
  recipient: string;
  status: string;
  attempt: number;
  reason: string | null;
  sentAt: string;
  bouncedAt: string | null;
};

export function submitEinvoice(token: string, invoiceId: string) {
  return apiFetch<EinvoiceSubmissionRow>(`/api/v1/invoices/${invoiceId}/einvoice-submissions`, { method: "POST", body: {}, token });
}
export function addDeliveryAttempt(token: string, invoiceId: string, body: { channel: string; recipient: string }) {
  return apiFetch<InvoiceDeliveryRow>(`/api/v1/invoices/${invoiceId}/delivery-attempts`, { method: "POST", body, token });
}
export function bounceDelivery(token: string, invoiceId: string, deliveryId: string, reason: string) {
  return apiFetch<InvoiceDeliveryRow>(`/api/v1/invoices/${invoiceId}/delivery-attempts/${deliveryId}/bounce`, { method: "POST", body: { reason }, token });
}
export function listDeliveries(token: string, invoiceId: string) {
  return apiFetch<InvoiceDeliveryRow[]>(`/api/v1/invoices/${invoiceId}/deliveries`, { token });
}

// --- Sales returns + refunds (ERP-SALES-044/045/046) --------------------------------

export type SalesReturnLine = { itemSku: string; qty: number; reason: string; condition: string };

export type ReturnReceiptRow = { id: string; receivedQty: number; receivedBy: string; receivedAt: string };

export type SalesReturnRow = {
  id: string;
  salesOrderId: string;
  customerId: string;
  status: string;
  reason: string;
  createdBy: string;
  createdAt: string;
  lines: SalesReturnLine[];
  authorizedQty: number;
  receivedQty: number;
  receipts: ReturnReceiptRow[];
};

export type RefundRequestRow = {
  id: string;
  adjustmentId: string;
  invoiceId: string;
  amount: number;
  status: string;
  requestedBy: string;
  requestedAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
};

export function listSalesReturns(token: string) {
  return apiFetch<SalesReturnRow[]>("/api/v1/sales-returns", { token });
}
export function getSalesReturn(token: string, id: string) {
  return apiFetch<SalesReturnRow>(`/api/v1/sales-returns/${id}`, { token });
}
export function createSalesReturn(token: string, body: { orderId: string; lines: { itemSku: string; qty: number; reason: string; condition: string }[]; overrideJustification?: string }) {
  return apiFetch<SalesReturnRow>("/api/v1/sales-returns", { method: "POST", body, token });
}
export function receiveReturn(token: string, id: string, receivedQty: number) {
  return apiFetch<SalesReturnRow>(`/api/v1/sales-returns/${id}/receipts`, { method: "POST", body: { receivedQty }, token });
}
export function requestRefund(token: string, adjustmentId: string) {
  return apiFetch<RefundRequestRow>(`/api/v1/adjustments/${adjustmentId}/refund-requests`, { method: "POST", body: {}, token });
}
export function approveRefund(token: string, id: string, reason: string | null) {
  return apiFetch<RefundRequestRow>(`/api/v1/refund-requests/${id}/approvals`, { method: "POST", body: { reason }, token });
}

// --- Receipt candidates + AR reconciliation (ERP-SALES-061/062) — services only ------

export type ReceiptCandidateRow = {
  id: string;
  customerId: string | null;
  bankRef: string;
  amount: number;
  currency: string;
  valueDate: string;
  channel: string;
  status: string;
  payerRef: string | null;
  createdBy: string;
  createdAt: string;
};

export type ArReconciliation = {
  asOf: string;
  totalOpen: number;
  unresolvedCount: number;
  rows: { docId: string; financeRef: string | null; currency: string; openAmount: number; status: string }[];
  unappliedCandidates: { count: number; total: number };
};

export function listReceiptCandidates(token: string) {
  return apiFetch<ReceiptCandidateRow[]>("/api/v1/receipt-candidates", { token });
}
export function createReceiptCandidate(token: string, body: { customerId?: string; bankRef: string; amount: number; currency: string; valueDate: string; channel: string }) {
  return apiFetch<ReceiptCandidateRow>("/api/v1/receipt-candidates", { method: "POST", body, token });
}
export function linkPayer(token: string, id: string, customerId: string) {
  return apiFetch<ReceiptCandidateRow>(`/api/v1/receipt-candidates/${id}/payer-links`, { method: "POST", body: { customerId }, token });
}
export function getArReconciliation(token: string, asOf?: string) {
  return apiFetch<ArReconciliation>(withQuery("/api/v1/ar-reconciliation", { asOf }), { token });
}

// --- Rebates (ERP-SALES-066/067/068) — services only; screen wiring lands later -------

export type RebateAgreementRow = {
  id: string;
  customerId: string;
  basis: string;
  ratePct: number;
  accrualPeriod: string;
  status: string;
  version: number;
  validFrom: string;
  validTo: string | null;
  createdBy: string;
  createdAt: string;
};

export type RebateAccrualRow = {
  id: string;
  agreementId: string;
  period: string;
  accruedAmount: number;
  approvedAmount: number | null;
  status: string;
  calculatedBy: string;
  calculatedAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
};

export type RebateClaimRow = {
  id: string;
  agreementId: string;
  period: string;
  amount: number;
  status: string;
  requestedBy: string;
  requestedAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
};

export function listRebateAgreements(token: string) {
  return apiFetch<RebateAgreementRow[]>("/api/v1/rebate-agreements", { token });
}
export function createRebateAgreement(token: string, body: { customerId: string; basis: string; ratePct: number; accrualPeriod: string; validFrom: string }) {
  return apiFetch<RebateAgreementRow>("/api/v1/rebate-agreements", { method: "POST", body, token });
}
export function calculateAccrual(token: string, id: string, period: string) {
  return apiFetch<RebateAccrualRow>(`/api/v1/rebate-agreements/${id}/accruals`, { method: "POST", body: { period }, token });
}
export function approveAccrual(token: string, id: string, period: string) {
  return apiFetch<RebateAccrualRow>(`/api/v1/rebate-agreements/${id}/accruals/${period}/approvals`, { method: "POST", body: {}, token });
}
export function createRebateClaim(token: string, body: { agreementId: string; period: string; amount: number; eligibleTxnIds: string[] }) {
  return apiFetch<RebateClaimRow>("/api/v1/rebate-claims", { method: "POST", body, token });
}
export function approveRebateClaim(token: string, id: string, reason: string | null) {
  return apiFetch<RebateClaimRow>(`/api/v1/rebate-claims/${id}/approvals`, { method: "POST", body: { reason }, token });
}

// --- Sales analytics (ERP-SALES-065) -------------------------------------------------

export type AnalyticsRow = { groupKey: string; count: number; total: number; docIds: string[] };

export type SalesAnalyticsReport = {
  domain: string;
  definition: string;
  rows: AnalyticsRow[];
};

export function getSalesAnalytics(token: string, domain: string, groupBy?: string) {
  return apiFetch<SalesAnalyticsReport>(withQuery("/api/v1/sales-analytics", { domain, groupBy }), { token });
}
