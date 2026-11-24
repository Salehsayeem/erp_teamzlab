// PROCUREMENT service boundary (T6 UI slice) — sits beside api.ts, which this module
// imports from but never modifies. Components import endpoint helpers from here and
// never call fetch directly. Domain types are local camelCase mirrors of the service
// projections in apps/api (Services/SourcingService.cs, ContractsService.cs,
// SupplierService.cs).
import { apiFetch, type ApiResult } from "./api";

// --- Enums (string unions mirroring apps/api Models/Procurement.cs) ---

export type SourcingEventType = "Rfq" | "Itb" | "Rfi";
export type SourcingEventStatus = "Draft" | "Published" | "Closed" | "Awarded" | "NoAward" | "Cancelled";
export type CriterionKind = "Scored" | "PassFail";
export type QaScope = "EventWide" | "SupplierSpecific";
export type InvitationStatus = "Sent" | "Failed" | "Declined";
export type BidStatus = "Submitted" | "Opened" | "Disqualified";
export type AwardStatus = "Recommended" | "Approved" | "Rejected" | "Alternate";
export type ContractStatus = "Draft" | "Active" | "Expired" | "Terminated";
export type ObligationStatus = "Open" | "EvidenceSubmitted" | "Verified" | "Waived";
export type SupplierLifecycleStatus = "Active" | "Pending" | "Suspended" | "Blocked" | "Inactive";
export type GateSeverity = "Hard" | "Warning";

// --- Entities ---

export type SourcingEvent = {
  id: string;
  title: string;
  eventType: SourcingEventType;
  status: SourcingEventStatus;
  opensAt: string;
  closesAt: string;
  timeZoneName: string;
  currency: string;
  version: number;
  createdBy: string;
  createdAt: string;
};

export type EventLot = {
  id: string;
  eventId: string;
  lotNo: string;
  description: string;
  itemSku: string | null;
  targetQty: number | null;
  ceilingValue: number | null;
};

export type EvalCriterion = {
  id: string;
  eventId: string;
  code: string;
  name: string;
  kind: CriterionKind;
  weightPct: number;
  scaleMax: number;
};

export type EventInvitation = {
  id: string;
  eventId: string;
  supplierId: string;
  status: InvitationStatus;
  channel: string;
  sentAt: string;
  failureReason: string | null;
  warningGates: string | null;
};

export type EventQa = {
  id: string;
  eventId: string;
  supplierId: string | null;
  question: string;
  answer: string;
  scope: QaScope;
  published: boolean;
  publishedAt: string | null;
  answeredBy: string;
};

export type EvaluatorAssignment = { id: string; eventId: string; userId: string };

/** Sealed reads strip price fields — unitPrice/discountPct/lineTotal are optional. */
export type BidLine = { lotId: string; qty: number; unitPrice?: number; discountPct?: number; lineTotal?: number };

export type Bid = {
  bidId: string;
  eventId: string;
  supplierId: string;
  eventVersion: number;
  status: BidStatus;
  contentDigest?: string;
  submittedAt: string;
  openedBy: string | null;
  openedAt: string | null;
  lines: BidLine[];
};

export type BidReceipt = { bidId: string; eventVersion: number; submittedAt: string; contentDigest: string };
export type BidOpenResult = { eventId: string; openedBy: string; openedAt: string; bidCount: number };

export type ScoreRow = {
  criterionId: string;
  criterionCode: string;
  kind: CriterionKind;
  weightPct: number;
  rawScore: number | null;
  passFail: boolean | null;
  normalized: number | null;
  contribution: number;
  missing: boolean;
  evaluatorUserId: string | null;
  comment: string | null;
};

export type ScoreSummaryBid = {
  bidId: string;
  supplierId: string;
  bidStatus: BidStatus;
  criteriaVersion: number;
  ownRowsOnly: boolean;
  disqualified: boolean | null;
  total: number | null;
  rank?: number;
  rows: ScoreRow[];
};

export type ScoreSummary = {
  eventId: string;
  formulaVersion: string;
  criteria: EvalCriterion[];
  bids: ScoreSummaryBid[];
};

export type ScoreSaved = {
  id: string;
  eventId: string;
  bidId: string;
  criterionCode: string;
  evaluatorUserId: string;
  rawScore: number | null;
  passFail: boolean | null;
  comment: string | null;
  criteriaVersion: number;
  submittedAt: string;
};

export type Award = {
  id: string;
  eventId: string;
  lotId: string;
  supplierId: string;
  qty: number;
  unitPrice: number;
  status: AwardStatus;
  basisEventVersion: number;
  recommendation: string;
  createdBy: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  createdAt: string;
};

export type AwardLotAllocation = {
  lotId: string;
  targetQty: number | null;
  awardedQty: number;
  remainingTargetQty: number | null;
};

/** Approve returns lot allocation; reject returns the moved event instead. */
export type AwardDecision = { award: Award; lot?: AwardLotAllocation; event?: SourcingEvent };

export type Contract = {
  id: string;
  supplierId: string;
  awardId: string | null;
  status: ContractStatus;
  currency: string;
  effectiveFrom: string;
  effectiveTo: string;
  renewalNoticeDays: number | null;
  optionPeriodsTotal: number | null;
  optionsExercised: number;
  ownerUserId: string;
  version: number;
  activatedBy: string | null;
  activatedAt: string | null;
  createdBy: string;
  createdAt: string;
  amendmentCount: number;
  overdueObligations: number;
  noticeDue: boolean;
};

export type ContractAmendment = {
  id: string;
  contractId: string;
  version: number;
  effectiveDate: string;
  summary: string;
  approvedBy: string | null;
  approvedAt: string | null;
};

export type ContractPriceLine = {
  id: string;
  contractId: string;
  amendmentId: string;
  itemSku: string;
  unitPrice: number;
  effectiveFrom: string;
  effectiveTo: string | null;
};

export type ContractObligation = {
  id: string;
  contractId: string;
  obligationType: string;
  ownerUserId: string;
  dueDate: string;
  sourceClause: string | null;
  status: ObligationStatus;
  evidenceRef: string | null;
  verifiedBy: string | null;
  verifiedAt: string | null;
  waiverReason: string | null;
  createdBy: string;
  overdue: boolean;
};

export type AmendmentResult = { amendment: ContractAmendment; contract: Contract };

/** GET /contracts/{id} once landed; arrays stay empty until then. */
export type ContractDetail = Contract & {
  amendments: ContractAmendment[];
  priceLines: ContractPriceLine[];
  obligations: ContractObligation[];
};

export type SupplierRow = {
  id: string;
  name: string;
  taxId: string;
  email: string;
  createdAt: string;
  createdBy: string;
  status: SupplierLifecycleStatus;
  statusReason: string | null;
};

export type SupplierGate = {
  id: string;
  supplierId: string;
  gateCode: string;
  severity: GateSeverity;
  effectiveFrom: string;
  effectiveTo: string | null;
  evidenceRef: string | null;
  exceptionBy: string | null;
  exceptionUntil: string | null;
};

/** GET /sourcing-events/{id} once landed; children stay empty until then. */
export type EventDetail = {
  event: SourcingEvent;
  lots: EventLot[];
  criteria: EvalCriterion[];
  invitations: EventInvitation[];
  qa: EventQa[];
  assignments: EvaluatorAssignment[];
};

// --- Tolerant narrowing for the four read endpoints landing in parallel ---
// (they may nest the entity under `event`/`contract` or return it flat; arrays may
// sit on the root or inside a wrapper object). Casts are safe: the write endpoints
// on the same entities already return exactly these camelCase projections.

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

function asArray(v: unknown): unknown[] {
  if (Array.isArray(v)) return v;
  if (isRecord(v)) {
    for (const key of ["data", "items", "rows", "awards", "gates", "lots", "criteria", "invitations", "qa", "assignments", "amendments", "obligations", "priceLines"]) {
      if (Array.isArray(v[key])) return v[key];
    }
  }
  return [];
}

export async function fetchEventDetail(token: string, eventId: string): Promise<ApiResult<EventDetail>> {
  const res = await apiFetch<unknown>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}`, { token });
  if (!res.ok) return res;
  const root = isRecord(res.data) ? res.data : {};
  const inner = isRecord(root.event) ? root.event : root;
  const event = inner as unknown as SourcingEvent;
  if (typeof event?.id !== "string" || event.id.length === 0) {
    return { ok: false, code: "SHAPE", message: "Unexpected sourcing event payload." };
  }
  return {
    ok: true,
    data: {
      event,
      lots: asArray(root.lots ?? inner.lots) as EventLot[],
      criteria: asArray(root.criteria ?? inner.criteria) as EvalCriterion[],
      invitations: asArray(root.invitations ?? inner.invitations) as EventInvitation[],
      qa: asArray(root.qa ?? inner.qa) as EventQa[],
      assignments: asArray(root.assignments ?? inner.assignments) as EvaluatorAssignment[],
    },
  };
}

export async function fetchEventAwards(token: string, eventId: string): Promise<ApiResult<Award[]>> {
  const res = await apiFetch<unknown>(`/api/v1/awards?eventId=${encodeURIComponent(eventId)}`, { token });
  if (!res.ok) return res;
  return { ok: true, data: asArray(res.data) as Award[] };
}

export async function fetchContractDetail(token: string, contractId: string): Promise<ApiResult<ContractDetail>> {
  const res = await apiFetch<unknown>(`/api/v1/contracts/${encodeURIComponent(contractId)}`, { token });
  if (!res.ok) return res;
  const root = isRecord(res.data) ? res.data : {};
  const inner = isRecord(root.contract) ? root.contract : root;
  const contract = inner as unknown as Contract;
  if (typeof contract?.id !== "string" || contract.id.length === 0) {
    return { ok: false, code: "SHAPE", message: "Unexpected contract payload." };
  }
  return {
    ok: true,
    data: {
      ...contract,
      amendments: asArray(root.amendments ?? inner.amendments) as ContractAmendment[],
      priceLines: asArray(root.priceLines ?? inner.priceLines) as ContractPriceLine[],
      obligations: asArray(root.obligations ?? inner.obligations) as ContractObligation[],
    },
  };
}

export async function fetchSupplierGates(token: string, supplierId: string): Promise<ApiResult<SupplierGate[]>> {
  const res = await apiFetch<unknown>(`/api/v1/suppliers/${encodeURIComponent(supplierId)}/gates`, { token });
  if (!res.ok) return res;
  return { ok: true, data: asArray(res.data) as SupplierGate[] };
}

// --- Sourcing events ---

export function fetchSourcingEvents(token: string, filters: { status?: string; eventType?: string } = {}) {
  const q = new URLSearchParams();
  if (filters.status) q.set("status", filters.status);
  if (filters.eventType) q.set("eventType", filters.eventType);
  const qs = q.toString();
  return apiFetch<SourcingEvent[]>(`/api/v1/sourcing-events${qs ? `?${qs}` : ""}`, { token });
}

export function createSourcingEvent(
  token: string,
  body: { title: string; eventType: SourcingEventType; opensAt: string; closesAt: string; timeZoneName: string; currency: string },
) {
  return apiFetch<SourcingEvent>("/api/v1/sourcing-events", { method: "POST", body, token });
}

export function addLot(
  token: string,
  eventId: string,
  body: { lotNo: string; description: string; itemSku?: string | null; targetQty?: number | null; ceilingValue?: number | null },
) {
  return apiFetch<EventLot>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/lots`, { method: "POST", body, token });
}

export function addCriterion(
  token: string,
  eventId: string,
  body: { code: string; name: string; kind: CriterionKind; weightPct: number; scaleMax: number },
) {
  return apiFetch<EvalCriterion>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/criteria`, { method: "POST", body, token });
}

export function publishEvent(token: string, eventId: string) {
  return apiFetch<SourcingEvent>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/publications`, { method: "POST", token });
}

export function inviteSuppliers(token: string, eventId: string, supplierIds: string[]) {
  return apiFetch<EventInvitation[]>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/invitations`, {
    method: "POST",
    body: { supplierIds },
    token,
  });
}

export function answerQa(
  token: string,
  eventId: string,
  body: { question: string; answer: string; scope: QaScope; supplierId?: string | null; publish: boolean },
) {
  return apiFetch<EventQa>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/questions`, { method: "POST", body, token });
}

export function submitBid(
  token: string,
  eventId: string,
  body: { supplierId: string; lines: { lotId: string; qty: number; unitPrice: number; discountPct: number }[] },
) {
  return apiFetch<BidReceipt>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/bids`, { method: "POST", body, token });
}

export function fetchBids(token: string, eventId: string) {
  return apiFetch<Bid[]>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/bids`, { token });
}

export function openBids(token: string, eventId: string) {
  return apiFetch<BidOpenResult>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/bid-openings`, { method: "POST", token });
}

export function assignEvaluators(token: string, eventId: string, userIds: string[]) {
  return apiFetch<EvaluatorAssignment[]>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/evaluator-assignments`, {
    method: "POST",
    body: { userIds },
    token,
  });
}

export function submitScore(
  token: string,
  eventId: string,
  body: { bidId: string; criterionId: string; rawScore?: number | null; passFail?: boolean | null; comment?: string | null },
) {
  return apiFetch<ScoreSaved>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/scores`, { method: "POST", body, token });
}

export function fetchScoreSummary(token: string, eventId: string) {
  return apiFetch<ScoreSummary>(`/api/v1/sourcing-events/${encodeURIComponent(eventId)}/score-summary`, { token });
}

// --- Awards ---

export function recommendAward(
  token: string,
  body: { eventId: string; lotId: string; supplierId: string; qty: number; unitPrice: number; recommendation: string },
) {
  return apiFetch<Award>("/api/v1/awards", { method: "POST", body, token });
}

export function decideAward(token: string, awardId: string, reason: string | null) {
  return apiFetch<AwardDecision>(`/api/v1/awards/${encodeURIComponent(awardId)}/approvals`, { method: "POST", body: { reason }, token });
}

// --- Contracts ---

export function fetchContracts(token: string, filters: { status?: string; expiringWithinDays?: number } = {}) {
  const q = new URLSearchParams();
  if (filters.status) q.set("status", filters.status);
  if (filters.expiringWithinDays !== undefined) q.set("expiringWithinDays", String(filters.expiringWithinDays));
  const qs = q.toString();
  return apiFetch<Contract[]>(`/api/v1/contracts${qs ? `?${qs}` : ""}`, { token });
}

export function createContract(
  token: string,
  body: {
    supplierId: string;
    awardId?: string | null;
    currency: string;
    effectiveFrom: string;
    effectiveTo: string;
    renewalNoticeDays?: number | null;
    optionPeriodsTotal?: number | null;
  },
) {
  return apiFetch<Contract>("/api/v1/contracts", { method: "POST", body, token });
}

export function activateContract(token: string, contractId: string) {
  return apiFetch<Contract>(`/api/v1/contracts/${encodeURIComponent(contractId)}/activations`, { method: "POST", token });
}

export function addAmendment(
  token: string,
  contractId: string,
  body: { effectiveDate: string; summary: string; priceLines: { itemSku: string; unitPrice: number; effectiveFrom: string; effectiveTo?: string | null }[] },
) {
  return apiFetch<AmendmentResult>(`/api/v1/contracts/${encodeURIComponent(contractId)}/amendments`, { method: "POST", body, token });
}

export function addObligation(
  token: string,
  contractId: string,
  body: { obligationType: string; ownerUserId: string; dueDate: string; sourceClause?: string | null },
) {
  return apiFetch<ContractObligation>(`/api/v1/contracts/${encodeURIComponent(contractId)}/obligations`, { method: "POST", body, token });
}

export function submitEvidence(token: string, contractId: string, obligationId: string, evidenceRef: string) {
  return apiFetch<ContractObligation>(`/api/v1/contracts/${encodeURIComponent(contractId)}/obligations/${encodeURIComponent(obligationId)}/evidence`, {
    method: "POST",
    body: { evidenceRef },
    token,
  });
}

export function verifyObligation(token: string, contractId: string, obligationId: string) {
  return apiFetch<ContractObligation>(`/api/v1/contracts/${encodeURIComponent(contractId)}/obligations/${encodeURIComponent(obligationId)}/verifications`, {
    method: "POST",
    token,
  });
}

export function waiveObligation(token: string, contractId: string, obligationId: string, reason: string) {
  return apiFetch<ContractObligation>(`/api/v1/contracts/${encodeURIComponent(contractId)}/obligations/${encodeURIComponent(obligationId)}/waivers`, {
    method: "POST",
    body: { reason },
    token,
  });
}

export function exerciseRenewal(token: string, contractId: string, optionNumber: number) {
  return apiFetch<Contract>(`/api/v1/contracts/${encodeURIComponent(contractId)}/renewals`, { method: "POST", body: { optionNumber }, token });
}

// --- Supplier lifecycle & gates ---

export function fetchSupplierRows(token: string) {
  return apiFetch<SupplierRow[]>("/api/v1/suppliers", { token });
}

export function setSupplierStatus(token: string, supplierId: string, status: SupplierLifecycleStatus, reason: string) {
  return apiFetch<SupplierRow>(`/api/v1/suppliers/${encodeURIComponent(supplierId)}/status`, { method: "POST", body: { status, reason }, token });
}

export function addSupplierGate(
  token: string,
  supplierId: string,
  body: {
    gateCode: string;
    severity: GateSeverity;
    effectiveFrom: string;
    effectiveTo?: string | null;
    evidenceRef?: string | null;
    exceptionUntil?: string | null;
  },
) {
  return apiFetch<SupplierGate>(`/api/v1/suppliers/${encodeURIComponent(supplierId)}/gates`, { method: "POST", body, token });
}

// --- Shared error mapping ---
// API messages are locale-neutral codes+text; pages map known codes to i18n strings
// and fall back to the raw message passthrough.

export function procError(code: string, message: string, mapped: Record<string, string>, fallback: string): string {
  return mapped[code] ?? `${fallback}: ${message}`;
}
