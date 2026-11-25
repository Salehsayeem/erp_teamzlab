namespace Erp.Api;

// ==================== deferred PROCUREMENT wave (brief: deferred-screens-procurement.md) ====================
// P1 requisitions + approvals, P2 category taxonomy + UoM, P3 PO change control + blanket orders,
// P4 receipt workspace (dispatch/ASN/inspection/returns/claims), P5 supplier qualification.
// Rulings: Q-P1 budget stub fail-closed; Q-P2 fixed maker-checker (rule_version reserved); Q-P7 R-QC
// quality authority; Q-S1 second procurement user. Contract correction: doc_dsp (sales disputes) and
// doc_scr (MFG scrap) sequences already exist, so dispatches/scorecards use deterministic ids instead
// of the brief's colliding prefixes. Every table carries tenant_id + legal_entity_id from day one.

public enum RequisitionStatus { Draft, Submitted, Approved, Rejected, Returned, Cancelled }
public enum RequisitionDecisionKind { Approve, Reject, Return, RequestInfo }
public enum ReservationStatus { Reserved, Released }
public enum CategoryStatus { Active, Deprecated }
public enum UomDimension { Count, Length, Mass, Volume, Area, Time }
public enum PoChangeStatus { Pending, Approved, Rejected }
public enum AckStatus { Current, Superseded }
public enum BlanketStatus { Active, Expired, Closed }
public enum DispatchState { Sent, Failed, Unknown }
public enum AsnStatus { Matched, Quarantined }
public enum InspectionStatus { Pending, Accepted, Partial, Rejected }
public enum ClaimStatus { Open, Closed }
public enum ClaimKind { Replacement, Credit }
public enum ScreeningOutcome { Clear, PossibleMatch, Unknown }
public enum ScreeningDisposition { ConfirmedClear, FalsePositive, ConfirmedMatch }
public enum ResponseReviewDecision { Approved, Returned }
public enum ScorecardStatus { Rated, NotRated }

// ---------- P1: requisitions (011–019) ----------

// line Id comes from doc_rql at creation; LineNo is the generated shadow PK (po_lines pattern).
// UnitPrice feeds the budget-control contract (013 AC1: currency + amount + txn-ID).
public record RequisitionLine(string Id, string CategoryId, string Description, decimal Qty, decimal UnitPrice,
    string Unit, DateOnly NeedBy, decimal BaseQty, string BaseUnit);

public record Requisition(string Id, string TenantId, string LegalEntityId, string Purpose,
    RequisitionStatus Status, int Version, string CreatedBy, DateTimeOffset CreatedAt,
    string? SubmittedBy = null, DateTimeOffset? SubmittedAt = null,
    string? RuleId = null, int? RuleVersion = null, string? ApproverRole = null, string? Currency = null,
    string? ExceptionCode = null, string? ExceptionNarrative = null, string? ExceptionEvidenceRef = null,
    DateTimeOffset? ExceptionFlaggedAt = null)
{
    public List<RequisitionLine> Lines { get; set; } = [];
}

// versioned rule row (014 AC1) — Q-P2: resolution is fixed maker-checker, rule_version snapshot only
public record ApprovalRule(string Id, string TenantId, string LegalEntityId, string Name,
    int Version, bool IsActive, string ApproverRole, decimal? MaxAmount, string CreatedBy, DateTimeOffset CreatedAt);

// one pending step per requisition (fixed maker-checker); Version is the decision token (018)
public record ApprovalStep(string Id, string TenantId, string LegalEntityId, string RequisitionId,
    int StepNo, string ApproverRole, RequisitionDecisionKind? Decision, int Version,
    string? DecidedBy = null, string? OnBehalfOfDelegatorId = null, DateTimeOffset? DecidedAt = null, string? Reason = null);

// append-only decision history (017 AC3); delegate decisions carry OnBehalfOfDelegatorId (015 AC3)
public record RequisitionDecision(string Id, string TenantId, string LegalEntityId, string RequisitionId,
    string StepId, RequisitionDecisionKind Decision, string? Reason, string ActorId,
    string? OnBehalfOfDelegatorId, int StepVersion, DateTimeOffset At);

// unique (tenant, entity, txn_id) — same txn-ID retry must not double-reserve (013 AC3)
public record BudgetReservation(string Id, string TenantId, string LegalEntityId, string TxnId,
    decimal Amount, string Currency, ReservationStatus Status, string CreatedBy, DateTimeOffset CreatedAt);

// time/scope-bounded approval delegation (015) — never broader than the delegator's own authority
public record ApprovalDelegation(string Id, string TenantId, string LegalEntityId,
    string DelegatorId, string DelegateId, DateTimeOffset From, DateTimeOffset To,
    decimal? MaxAmount, string? CategoryId, string Reason, string CreatedBy, DateTimeOffset CreatedAt);

// ---------- P2: category taxonomy + purchasing units (002–004) ----------

public record ProcurementCategory(string Id, string TenantId, string LegalEntityId, string? ParentId,
    string Name, CategoryStatus Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? ExternalCode,
    string CreatedBy, DateTimeOffset CreatedAt);

public record UnitOfMeasure(string Id, string TenantId, string LegalEntityId, string Code, string Name,
    UomDimension Dimension, int Precision, bool IsBase, string CreatedBy, DateTimeOffset CreatedAt);

// conversion valid only inside the active interval for the txn date (004 AC2)
public record UomConversion(string Id, string TenantId, string LegalEntityId, string FromCode, string ToCode,
    decimal Factor, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string CreatedBy, DateTimeOffset CreatedAt);

// ---------- P3: PO change control + blanket orders (052–054) ----------

// before/after snapshots kept as JSON text (052 AC3); insert-only until disposition
public record PurchaseOrderChange(string Id, string TenantId, string LegalEntityId, string PoId,
    int ChangeNo, PoChangeStatus Status, string BeforeJson, string AfterJson, string Rationale,
    bool IsMaterial, int BaseOrderVersion, string RequestedBy, DateTimeOffset RequestedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null);

// supplier ack carries the exact order version it acknowledges (053 AC2); superseded versions stored + flagged
public record PurchaseOrderAcknowledgement(string Id, string TenantId, string LegalEntityId, string PoId,
    int OrderVersion, string SupplierId, AckStatus Status, string LinesJson, bool HasDiscrepancy,
    string ReceivedBy, DateTimeOffset ReceivedAt);

// ceiling-debited releases (054): ReleasedAmount is mutated only inside the FOR UPDATE row lock
public record BlanketOrder(string Id, string TenantId, string LegalEntityId, string SupplierId,
    string Currency, decimal CeilingAmount, decimal ReleasedAmount, DateOnly ValidFrom, DateOnly ValidTo,
    BlanketStatus Status, int Version, string CreatedBy, DateTimeOffset CreatedAt);

public record BlanketRelease(string Id, string TenantId, string LegalEntityId, string BlanketOrderId,
    decimal Amount, string? PoId, string ReleasedBy, DateTimeOffset ReleasedAt);

// ---------- P4: receipt workspace (055–059) ----------

public record Dispatch(string Id, string TenantId, string LegalEntityId, string PoId, int OrderVersion,
    DispatchState State, string? Detail, string SentBy, DateTimeOffset SentAt);

public record AsnLine(string Id, string ItemSku, decimal Qty, bool Quarantined, string? QuarantineReason);

public record AdvanceShipNotice(string Id, string TenantId, string LegalEntityId, string PoId, int OrderVersion,
    string ShipmentRef, AsnStatus Status, string ReceivedBy, DateTimeOffset ReceivedAt)
{
    public List<AsnLine> Lines { get; set; } = [];
}

// rejected qty is excluded from invoice-matchable accepted (058 AC2); disposition ≠ receiver (Q-P7 SoD)
public record ReceiptInspection(string Id, string TenantId, string LegalEntityId, string ReceiptId,
    string PoId, string ItemSku, decimal QtyReceived, decimal QtyAccepted, decimal QtyRejected,
    InspectionStatus Status, string? DefectCode, string? EvidenceRef,
    string CreatedBy, DateTimeOffset CreatedAt, string? DispositionedBy = null, DateTimeOffset? DispositionedAt = null);

public record SupplierReturn(string Id, string TenantId, string LegalEntityId, string ReceiptId, string PoId,
    string ItemSku, decimal Qty, string Reason, ClaimKind Kind, string CreatedBy, DateTimeOffset CreatedAt);

public record SupplierClaim(string Id, string TenantId, string LegalEntityId, string? ReceiptId, string PoId,
    ClaimKind Kind, decimal? Amount, ClaimStatus Status, string? OutcomeCode, string? ReconciliationStatus,
    string CreatedBy, DateTimeOffset CreatedAt, string? ClosedBy = null, DateTimeOffset? ClosedAt = null);

// ---------- P5: supplier qualification + scorecards (040–044, 061–062) ----------

public record SupplierQuestionnaire(string Id, string TenantId, string LegalEntityId, string Name,
    int Version, string QuestionsJson, string CreatedBy, DateTimeOffset CreatedAt);

// response pins the questionnaire version it answers (040 AC1)
public record QuestionnaireResponse(string Id, string TenantId, string LegalEntityId, string SupplierId,
    string QuestionnaireId, int QuestionnaireVersion, string AnswersJson, string? FieldCommentsJson,
    string? ReviewDecision, string? ReviewedBy, DateTimeOffset? ReviewedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

// replacement rows point back via SupersedesId — prior version + verification preserved (041 AC3)
public record SupplierEvidence(string Id, string TenantId, string LegalEntityId, string SupplierId,
    string EvidenceType, string MetadataJson, DateOnly? ExpiresOn, string? SupersedesId,
    string? VerifiedBy, DateTimeOffset? VerifiedAt, string CreatedBy, DateTimeOffset CreatedAt);

public record SupplierRiskProfile(string Id, string TenantId, string LegalEntityId, string SupplierId,
    decimal Score, string Band, string SourcesJson, int RuleVersion, DateTimeOffset ComputedAt,
    string Disposition, bool Stale);

public record ScreeningResult(string Id, string TenantId, string LegalEntityId, string SupplierId,
    string Source, ScreeningOutcome Outcome, string Status, string? SourceRef, string? Disposition,
    string? DispositionBy, DateTimeOffset? DispositionAt, string CreatedBy, DateTimeOffset CreatedAt);

// performance observation (061); dispute retains original value alongside the amendment
public record SupplierObservation(string Id, string TenantId, string LegalEntityId, string SupplierId,
    string Metric, decimal Value, string Period, string Source, string? DisputeReason, decimal? AmendedValue,
    string? DisputedBy, DateTimeOffset? DisputedAt, string CreatedBy, DateTimeOffset CreatedAt);

// one scorecard per supplier per period; insufficient volume → NotRated (062 edge)
public record SupplierScorecard(string Id, string TenantId, string LegalEntityId, string SupplierId,
    string PeriodFrom, string PeriodTo, ScorecardStatus Status, string? MetricSummaryJson, int CalcVersion,
    string? SupplierResponse, string CreatedBy, DateTimeOffset CreatedAt);

public record CorrectiveAction(string Id, string TenantId, string LegalEntityId, string ScorecardId,
    string Finding, string Action, string Status, string? Outcome, string? ClosedBy, DateTimeOffset? ClosedAt,
    string CreatedBy, DateTimeOffset CreatedAt);
