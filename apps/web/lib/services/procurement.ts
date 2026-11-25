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
  /** Absent in GET /awards summaries while prices are sealed for the caller. */
  unitPrice?: number;
  status: AwardStatus;
  basisEventVersion: number;
  /** Absent in sealed GET /awards summaries. */
  recommendation?: string;
  createdBy: string;
  approvedBy: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  createdAt?: string;
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

// ==================== deferred PROCUREMENT wave (P1-P6) ====================
// camelCase mirrors of the wave's service projections (RequisitionService, ProcurementCatalogService,
// PurchaseOrderChangeService, BlanketOrderService, ReceiptInspectionService, SupplierQualification-
// Service, SupplierScorecardsService, ProcurementAnalyticsService).

export type RequisitionStatus = "Draft" | "Submitted" | "Approved" | "Rejected" | "Returned" | "Cancelled";
export type CategoryStatus = "Active" | "Deprecated";
export type UomDimension = "Count" | "Length" | "Mass" | "Volume" | "Area" | "Time";
export type PoChangeStatus = "Pending" | "Approved" | "Rejected";
export type AckStatus = "Current" | "Superseded";
export type BlanketStatus = "Active" | "Expired" | "Closed";
export type DispatchState = "Sent" | "Failed" | "Unknown";
export type AsnStatus = "Matched" | "Quarantined";
export type InspectionStatus = "Pending" | "Accepted" | "Partial" | "Rejected";
export type ClaimKind = "Replacement" | "Credit";
export type ClaimStatus = "Open" | "Closed";
export type ScreeningOutcome = "Clear" | "PossibleMatch" | "Unknown";
export type ScorecardStatus = "Rated" | "NotRated";

export type RequisitionLine = {
  id: string; categoryId: string; description: string; qty: number; unitPrice: number;
  unit: string; needBy: string; baseQty: number; baseUnit: string;
};
export type Requisition = {
  id: string; purpose: string; status: RequisitionStatus; version: number; currency: string | null;
  createdBy: string; createdAt: string; submittedBy: string | null; submittedAt: string | null;
  ruleId: string | null; ruleVersion: number | null; approverRole: string | null;
  exceptionCode: string | null; exceptionNarrative: string | null; exceptionEvidenceRef: string | null;
  exceptionFlaggedAt: string | null;
  lines: RequisitionLine[]; total: number;
};
export type RequisitionStep = {
  id: string; stepNo: number; approverRole: string; decision: string | null; version: number;
  decidedBy: string | null; onBehalfOfDelegatorId: string | null; decidedAt: string | null;
  reason: string | null; ruleVersion: number | null;
};
export type RequisitionDecisionRow = {
  decision: string; reason: string | null; actorId: string; onBehalfOfDelegatorId: string | null;
  stepVersion: number; at: string;
};
export type RequisitionDetail = {
  requisition: Requisition; lines: RequisitionLine[]; step: RequisitionStep | null;
  decisions: RequisitionDecisionRow[];
  budget: { txnId: string; amount: number; currency: string; status: string } | null;
};

export type ProcurementCategory = {
  id: string; parentId: string | null; name: string; status: CategoryStatus;
  effectiveFrom: string; effectiveTo: string | null; externalCode: string | null;
  createdBy: string; createdAt: string;
};
export type UnitOfMeasure = {
  id: string; code: string; name: string; dimension: UomDimension; precision: number;
  isBase: boolean; createdBy: string; createdAt: string;
};
export type UomConversion = {
  id: string; fromCode: string; toCode: string; factor: number;
  effectiveFrom: string; effectiveTo: string | null; createdBy: string; createdAt: string;
};
export type PurchaseOrderDetail = {
  id: string; supplierId: string; status: string; version: number; createdBy: string; createdAt: string;
  approvedBy: string | null; approvedAt: string | null; rejectionReason: string | null;
  lines: { id: string | null; itemSku: string; description: string; qty: number; unitPrice: number }[];
  total: number;
};
export type PoChange = {
  id: string; poId: string; changeNo: number; status: PoChangeStatus; rationale: string;
  isMaterial: boolean; baseOrderVersion: number; requestedBy: string; requestedAt: string;
  approvedBy: string | null; approvedAt: string | null;
};
export type PoAck = {
  id: string; poId: string; orderVersion: number; supplierId: string; status: AckStatus;
  hasDiscrepancy: boolean; receivedBy: string; receivedAt: string; late: boolean;
};
export type BlanketOrder = {
  id: string; supplierId: string; currency: string; ceilingAmount: number; releasedAmount: number;
  validFrom: string; validTo: string; status: BlanketStatus; version: number;
  createdBy: string; createdAt: string; remaining: number;
};
export type BlanketOrderDetail = BlanketOrder & {
  releases: { id: string; amount: number; poId: string | null; releasedBy: string; releasedAt: string }[];
};
export type DispatchRow = {
  id: string; poId: string; orderVersion: number; state: DispatchState;
  detail: string | null; sentBy: string; sentAt: string;
};
export type AsnLine = { id: string; itemSku: string; qty: number; quarantined: boolean; quarantineReason: string | null };
export type Asn = {
  id: string; poId: string; orderVersion: number; shipmentRef: string; status: AsnStatus;
  receivedBy: string; receivedAt: string; lines: AsnLine[];
};
export type ReceiptRow = {
  id: string; purchaseOrderId: string; itemSku: string; qty: number; receivedAt: string; receivedBy: string;
};
export type InspectionRow = {
  id: string; itemSku: string; qtyReceived: number; qtyAccepted: number; qtyRejected: number;
  status: InspectionStatus; defectCode: string | null; evidenceRef: string | null;
  dispositionedBy: string | null; dispositionedAt: string | null; createdBy: string; createdAt: string;
};
export type ReturnRow = { id: string; itemSku: string; qty: number; reason: string; kind: ClaimKind; createdBy: string; createdAt: string };
export type Balances = {
  orderId: string; ordered: number; received: number; rejected: number; accepted: number;
  remaining: number; currency: string | null; calculatedAt: string; sourceVersion: number;
};
export type ReceiptDetail = {
  receipt: ReceiptRow; inspections: InspectionRow[]; returns: ReturnRow[]; balances: Balances | null;
};
export type Questionnaire = { id: string; name: string; version: number; createdBy: string; createdAt: string };
export type QuestionnaireResponseRow = {
  id: string; supplierId: string; questionnaireId: string; questionnaireVersion: number;
  answersJson: string; fieldCommentsJson: string | null; reviewDecision: string | null;
  reviewedBy: string | null; reviewedAt: string | null; createdBy: string; createdAt: string;
};
export type Evidence = {
  id: string; supplierId: string; evidenceType: string; metadataJson: string;
  expiresOn: string | null; supersedesId: string | null; verifiedBy: string | null;
  verifiedAt: string | null; createdBy: string; createdAt: string;
};
export type QualificationStatus = {
  supplierId: string; eligible: boolean;
  expiredEvidences: { id: string; evidenceType: string; expiresOn: string }[];
  policyGrace: boolean | null; checkedAt: string;
};
export type ScreeningRow = {
  id: string; supplierId: string; source: string; outcome: ScreeningOutcome; status: string;
  sourceRef: string | null; disposition: string | null; dispositionBy: string | null;
  dispositionAt: string | null; createdBy: string; createdAt: string;
};
export type Observation = {
  id: string; supplierId: string; metric: string; value: number; period: string; source: string;
  disputeReason: string | null; amendedValue: number | null; disputedBy: string | null;
  disputedAt: string | null; createdBy: string; createdAt: string;
};
export type RiskProfile = {
  supplierId: string; score: number; band: string; disposition: string; ruleVersion: number;
  stale: boolean; sources: string; computedAt: string;
};
export type Scorecard = {
  id: string; supplierId: string; periodFrom: string; periodTo: string; status: ScorecardStatus;
  calcVersion: number; metrics: Record<string, { definition: string; count: number; mean: number; disputed: boolean }> | null;
  supplierResponse: string | null; createdBy: string; createdAt: string;
};
export type CorrectiveAction = {
  id: string; scorecardId: string; finding: string; action: string; status: string;
  outcome: string | null; closedBy: string | null; closedAt: string | null; createdBy: string; createdAt: string;
};
export type AnalyticsMetric = {
  metric: string; groupBy: string; definition: string; from: string | null; to: string | null;
  total: number | null; rows: { key: string; amount?: number; count?: number; currency?: string | null }[];
};

const enc = encodeURIComponent;

// --- P1 requisitions ---
export function fetchRequisitions(token: string, status?: string) {
  return apiFetch<Requisition[]>(`/api/v1/requisitions${status ? `?status=${enc(status)}` : ""}`, { token });
}
export function fetchRequisitionDetail(token: string, id: string) {
  return apiFetch<RequisitionDetail>(`/api/v1/requisitions/${enc(id)}`, { token });
}
export function createRequisition(token: string, body: { purpose: string; currency: string; lines: { categoryId: string; description: string; qty: number; unitPrice: number; unit: string; needBy: string }[] }) {
  return apiFetch<Requisition>("/api/v1/requisitions", { method: "POST", body, token });
}
export function submitRequisition(token: string, id: string, overrideReason?: string | null) {
  return apiFetch<Requisition>(`/api/v1/requisitions/${enc(id)}/submission`, { method: "POST", body: { overrideReason: overrideReason ?? null }, token });
}
export function decideRequisition(token: string, id: string, body: { decision: string; version: number; reason?: string | null }) {
  return apiFetch<Requisition>(`/api/v1/requisitions/${enc(id)}/decisions`, { method: "POST", body, token });
}
export function flagRequisitionException(token: string, id: string, body: { reasonCode: string; narrative: string; evidenceRef?: string | null }) {
  return apiFetch<Requisition>(`/api/v1/requisitions/${enc(id)}/exceptions`, { method: "POST", body, token });
}
export function createApprovalDelegation(token: string, body: { delegateId: string; from: string; to: string; maxAmount?: number | null; categoryId?: string | null; reason: string }) {
  return apiFetch<{ id: string }>("/api/v1/approval-delegations", { method: "POST", body, token });
}

// --- P2 taxonomy + units ---
export function fetchCategories(token: string) {
  return apiFetch<ProcurementCategory[]>("/api/v1/procurement-categories", { token });
}
export function createCategory(token: string, body: { name: string; parentId?: string | null; externalCode?: string | null }) {
  return apiFetch<ProcurementCategory>("/api/v1/procurement-categories", { method: "POST", body, token });
}
export function updateCategory(token: string, id: string, body: { name?: string | null; parentId?: string | null; externalCode?: string | null }) {
  return apiFetch<ProcurementCategory>(`/api/v1/procurement-categories/${enc(id)}`, { method: "POST", body, token });
}
export function deprecateCategory(token: string, id: string) {
  return apiFetch<ProcurementCategory>(`/api/v1/procurement-categories/${enc(id)}/deprecations`, { method: "POST", body: {}, token });
}
export function fetchUnits(token: string) {
  return apiFetch<UnitOfMeasure[]>("/api/v1/unit-of-measures", { token });
}
export function createUom(token: string, body: { code: string; name: string; dimension: UomDimension; precision: number; isBase: boolean }) {
  return apiFetch<UnitOfMeasure>("/api/v1/unit-of-measures", { method: "POST", body, token });
}
export function createConversion(token: string, code: string, body: { toCode: string; factor: number; effectiveFrom: string; effectiveTo?: string | null }) {
  return apiFetch<UomConversion>(`/api/v1/unit-of-measures/${enc(code)}/conversions`, { method: "POST", body, token });
}

// --- P3 change control + acks + blanket orders ---
export function fetchPoDetail(token: string, poId: string) {
  return apiFetch<PurchaseOrderDetail>(`/api/v1/purchase-orders/${enc(poId)}`, { token });
}
export function fetchPoChanges(token: string, poId: string) {
  return apiFetch<PoChange[]>(`/api/v1/purchase-orders/${enc(poId)}/changes`, { token });
}
export function createPoChange(token: string, poId: string, body: { rationale: string; supplierId?: string | null; lines: { itemSku: string; qty?: number | null; unitPrice?: number | null }[] }) {
  return apiFetch<PoChange>(`/api/v1/purchase-orders/${enc(poId)}/changes`, { method: "POST", body, token });
}
export function decidePoChange(token: string, poId: string, changeId: string, approve: boolean) {
  return apiFetch<PoChange>(`/api/v1/purchase-orders/${enc(poId)}/changes/${enc(changeId)}/decisions`, { method: "POST", body: { approve }, token });
}
export function createPoAck(token: string, poId: string, body: { supplierId: string; orderVersion: number; lines: { itemSku: string; status: string }[] }) {
  return apiFetch<PoAck>(`/api/v1/purchase-orders/${enc(poId)}/acknowledgements`, { method: "POST", body, token });
}
export function sendDispatch(token: string, poId: string, body: { orderVersion: number; state: DispatchState; detail?: string | null }) {
  return apiFetch<DispatchRow>(`/api/v1/purchase-orders/${enc(poId)}/dispatches`, { method: "POST", body, token });
}
export function fetchBlanketOrders(token: string) {
  return apiFetch<BlanketOrder[]>("/api/v1/blanket-orders", { token });
}
export function fetchBlanketOrder(token: string, id: string) {
  return apiFetch<BlanketOrderDetail>(`/api/v1/blanket-orders/${enc(id)}`, { token });
}
export function createBlanketOrder(token: string, body: { supplierId: string; currency: string; ceilingAmount: number; validFrom: string; validTo: string }) {
  return apiFetch<BlanketOrder>("/api/v1/blanket-orders", { method: "POST", body, token });
}
export function createBlanketRelease(token: string, id: string, body: { amount: number; poId?: string | null }) {
  return apiFetch<{ id: string; amount: number }>(`/api/v1/blanket-orders/${enc(id)}/releases`, { method: "POST", body, token });
}

// --- P4 receipt workspace ---
export function fetchReceipts(token: string) {
  return apiFetch<ReceiptRow[]>("/api/v1/receipts", { token });
}
export function fetchReceiptDetail(token: string, id: string) {
  return apiFetch<ReceiptDetail>(`/api/v1/receipts/${enc(id)}`, { token });
}
export function fetchAsns(token: string, status?: string) {
  return apiFetch<Asn[]>(`/api/v1/advance-ship-notices${status ? `?status=${enc(status)}` : ""}`, { token });
}
export function intakeAsn(token: string, body: { poId: string; shipmentRef: string; orderVersion: number; lines: { itemSku: string; qty: number }[] }) {
  return apiFetch<Asn>("/api/v1/advance-ship-notices", { method: "POST", body, token });
}
export function createInspection(token: string, receiptId: string, body: { itemSku: string; qtyReceived: number }) {
  return apiFetch<InspectionRow>(`/api/v1/receipts/${enc(receiptId)}/inspections`, { method: "POST", body, token });
}
export function disposeInspection(token: string, receiptId: string, inspectionId: string, body: { status: InspectionStatus; qtyAccepted: number; qtyRejected: number; defectCode?: string | null; evidenceRef?: string | null }) {
  return apiFetch<InspectionRow>(`/api/v1/receipts/${enc(receiptId)}/inspections/${enc(inspectionId)}/dispositions`, { method: "POST", body, token });
}
export function createSupplierReturn(token: string, body: { receiptId: string; poId: string; itemSku: string; qty: number; reason: string; kind: ClaimKind }) {
  return apiFetch<ReturnRow>("/api/v1/supplier-returns", { method: "POST", body, token });
}
export function createSupplierClaim(token: string, body: { receiptId?: string | null; poId: string; kind: ClaimKind; amount?: number | null }) {
  return apiFetch<{ id: string; status: ClaimStatus }>("/api/v1/supplier-claims", { method: "POST", body, token });
}
export function closeSupplierClaim(token: string, id: string, body: { outcomeCode: string; reconciliationStatus: string }) {
  return apiFetch<{ id: string; status: ClaimStatus }>(`/api/v1/supplier-claims/${enc(id)}/closures`, { method: "POST", body, token });
}
export function fetchBalances(token: string, orderId: string) {
  return apiFetch<Balances>(`/api/v1/procurement-balances?orderId=${enc(orderId)}`, { token });
}

// --- P5 qualification + scorecards ---
export function createQuestionnaire(token: string, body: { name: string; questions: { code: string; prompt: string; required: boolean; conditional: boolean; conditionalOnCode?: string | null }[] }) {
  return apiFetch<Questionnaire>("/api/v1/supplier-questionnaires", { method: "POST", body, token });
}
export function createQuestionnaireResponse(token: string, supplierId: string, body: { questionnaireId: string; answersJson: string }) {
  return apiFetch<QuestionnaireResponseRow>(`/api/v1/suppliers/${enc(supplierId)}/questionnaire-responses`, { method: "POST", body, token });
}
export function reviewQuestionnaireResponse(token: string, id: string, body: { reviewDecision: string; fieldCommentsJson?: string | null }) {
  return apiFetch<QuestionnaireResponseRow>(`/api/v1/questionnaire-responses/${enc(id)}/reviews`, { method: "POST", body, token });
}
export function fetchQualification(token: string, supplierId: string) {
  return apiFetch<QualificationStatus>(`/api/v1/suppliers/${enc(supplierId)}/qualification`, { token });
}
export function fetchEvidences(token: string, supplierId: string) {
  return apiFetch<Evidence[]>(`/api/v1/suppliers/${enc(supplierId)}/evidences`, { token });
}
export function createEvidence(token: string, supplierId: string, body: { evidenceType: string; metadataJson: string; expiresOn?: string | null; supersedesId?: string | null }) {
  return apiFetch<Evidence>(`/api/v1/suppliers/${enc(supplierId)}/evidences`, { method: "POST", body, token });
}
export function fetchRiskProfile(token: string, supplierId: string) {
  return apiFetch<RiskProfile>(`/api/v1/suppliers/${enc(supplierId)}/risk-profile`, { token });
}
export function createScreening(token: string, supplierId: string, body: { source: string; sourceRef?: string | null }) {
  return apiFetch<ScreeningRow>(`/api/v1/suppliers/${enc(supplierId)}/screenings`, { method: "POST", body, token });
}
export function fetchScreeningsBySupplier(token: string, supplierId: string) {
  return apiFetch<ScreeningRow[]>(`/api/v1/suppliers/${enc(supplierId)}/screenings`, { token });
}
export function disposeScreening(token: string, id: string, body: { disposition: string; reason: string }) {
  return apiFetch<ScreeningRow>(`/api/v1/screenings/${enc(id)}/dispositions`, { method: "POST", body, token });
}
export function createObservation(token: string, supplierId: string, body: { metric: string; value: number; period: string; source: string }) {
  return apiFetch<Observation>(`/api/v1/suppliers/${enc(supplierId)}/observations`, { method: "POST", body, token });
}
export function disputeObservation(token: string, id: string, body: { reason: string; amendedValue: number }) {
  return apiFetch<Observation>(`/api/v1/observations/${enc(id)}/disputes`, { method: "POST", body, token });
}
export function fetchScorecards(token: string, supplierId: string) {
  return apiFetch<Scorecard[]>(`/api/v1/suppliers/${enc(supplierId)}/scorecards`, { token });
}
export function fetchScorecard(token: string, id: string) {
  return apiFetch<Scorecard>(`/api/v1/supplier-scorecards/${enc(id)}`, { token });
}
export function createScorecard(token: string, body: { supplierId: string; periodFrom: string; periodTo: string }) {
  return apiFetch<Scorecard>("/api/v1/supplier-scorecards", { method: "POST", body, token });
}
export function addScorecardSupplierResponse(token: string, id: string, response: string) {
  return apiFetch<Scorecard>(`/api/v1/supplier-scorecards/${enc(id)}/supplier-responses`, { method: "POST", body: { response }, token });
}
export function createCorrectiveAction(token: string, scorecardId: string, body: { finding: string; action: string }) {
  return apiFetch<CorrectiveAction>(`/api/v1/scorecards/${enc(scorecardId)}/corrective-actions`, { method: "POST", body, token });
}
export function closeCorrectiveAction(token: string, id: string, outcome: string) {
  return apiFetch<CorrectiveAction>(`/api/v1/corrective-actions/${enc(id)}/closures`, { method: "POST", body: { outcome }, token });
}

// --- P6 analytics ---
export function fetchSpend(token: string, groupBy: string, from?: string | null, to?: string | null) {
  const q = new URLSearchParams({ groupBy });
  if (from) q.set("from", from);
  if (to) q.set("to", to);
  return apiFetch<AnalyticsMetric>(`/api/v1/procurement-analytics/spend?${q.toString()}`, { token });
}
export function fetchDemand(token: string) {
  return apiFetch<AnalyticsMetric>("/api/v1/procurement-analytics/demand?groupBy=currency", { token });
}
export function fetchSourcingAnalytics(token: string) {
  return apiFetch<AnalyticsMetric>("/api/v1/procurement-analytics/sourcing", { token });
}
