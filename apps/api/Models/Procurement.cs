namespace Erp.Api;

public enum PoStatus { Draft, PendingApproval, Approved, Sent, Received, Rejected }

// T6 (ERP-PROCUREMENT-047 AC1): lifecycle status trails the original fields as OPTIONALS so every
// Task-1 call site (seed, SupplierService, tests) compiles unchanged — same trick as JournalLine.
public record Supplier(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy,
    SupplierLifecycleStatus Status = SupplierLifecycleStatus.Active, string? StatusReason = null);

public record PoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice);

// EF Core cannot bind collection navigations in a primary constructor (task-2 §2) — Lines lives in
// a settable body property that EF populates; the secondary constructor keeps every Task-1
// positional call site byte-identical. JSON keeps a single "lines" field.
public record PurchaseOrder(
    string Id, string TenantId, string LegalEntityId, string SupplierId, PoStatus Status,
    string CreatedBy, DateTimeOffset CreatedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null)
{
    public List<PoLine> Lines { get; set; } = [];
    public PurchaseOrder(string id, string tenantId, string legalEntityId, string supplierId, PoStatus status,
        IReadOnlyList<PoLine> lines, string createdBy, DateTimeOffset createdAt,
        string? approvedBy = null, DateTimeOffset? approvedAt = null, string? rejectionReason = null)
        : this(id, tenantId, legalEntityId, supplierId, status, createdBy, createdAt, approvedBy, approvedAt, rejectionReason)
    { Lines = [.. lines]; }
}

public record Receipt(string Id, string TenantId, string LegalEntityId, string PurchaseOrderId, string ItemSku, decimal Qty, DateTimeOffset ReceivedAt, string ReceivedBy);

// ==================== T6 PROCUREMENT deep (sourcing → bids → evaluation → award → contracts) ====================
// Rulings R-T6a..i: bids are buyer-recorded (no portal), sealed-bid machinery trimmed to
// "hidden until an approved opening", BAFO rounds deferred, award posts nothing (no encumbrance),
// commitment-only contracts, manual renewal alerting, string EvidenceRef, award→PO manual.

public enum SourcingEventType { Rfq, Itb, Rfi } // Rfi has no award intent (022 edge)
public enum SourcingEventStatus { Draft, Published, Closed, Awarded, NoAward, Cancelled }
public enum CriterionKind { Scored, PassFail }
public enum InvitationStatus { Sent, Failed, Declined } // failure is never stored as success (024 AC2)
public enum QaScope { EventWide, SupplierSpecific }
public enum BidStatus { Submitted, Opened, Disqualified }
public enum AwardStatus { Recommended, Approved, Rejected, Alternate } // Alternate = ranked, not activated (034 edge)
public enum ContractStatus { Draft, Active, Expired, Terminated }
public enum ObligationStatus { Open, EvidenceSubmitted, Verified, Waived } // overdue is computed on read, never stored
public enum SupplierLifecycleStatus { Active, Pending, Suspended, Blocked, Inactive }
public enum GateSeverity { Hard, Warning } // in-force Hard gate fails eligibility until excepted (023 AC1/AC3)

// deadlines are instants + a named zone (026 AC1); Version bumps on every published event-wide
// answer (027 AC3) and is snapshotted into bids (028) and score cards (030 AC3)
public record SourcingEvent(string Id, string TenantId, string LegalEntityId, string Title,
    SourcingEventType EventType, SourcingEventStatus Status, DateTimeOffset OpensAt, DateTimeOffset ClosesAt,
    string TimeZoneName, string Currency, int Version, string CreatedBy, DateTimeOffset CreatedAt);

// lots are independently evaluable/awardable (022 AC2)
public record EventLot(string Id, string TenantId, string LegalEntityId, string EventId, string LotNo,
    string Description, string? ItemSku, decimal? TargetQty, decimal? CeilingValue);

// evaluation criteria are frozen at publication (030 AC1)
public record EvalCriterion(string Id, string TenantId, string LegalEntityId, string EventId,
    string Code, string Name, CriterionKind Kind, decimal WeightPct, int ScaleMax);

// comms are log-only in the pilot (R-T6a: no portal); WarningGates records warning-severity gates
// that were allowed through (023 AC2) — trailing optional keeps the briefed shape call-compatible
public record EventInvitation(string Id, string TenantId, string LegalEntityId, string EventId,
    string SupplierId, InvitationStatus Status, string Channel, DateTimeOffset SentAt, string? FailureReason,
    string? WarningGates = null);

public record EventQa(string Id, string TenantId, string LegalEntityId, string EventId, string? SupplierId,
    string Question, string? Answer, QaScope Scope, bool Published, DateTimeOffset? PublishedAt, string AnsweredBy);

// Bid.Id IS the receipt id (028 AC1); ContentDigest = SHA-256 hex of the submitted payload.
// Bids are sealed (OpenedBy/OpenedAt null) until an approved opening stamps them (025 trimmed).
public record Bid(string Id, string TenantId, string LegalEntityId, string EventId, string SupplierId,
    int EventVersion, BidStatus Status, string ContentDigest, DateTimeOffset SubmittedAt, string? OpenedBy, DateTimeOffset? OpenedAt);

// 029 AC2 — LineTotal is computed on read, never stored: Round(Qty × UnitPrice × (1 − pct/100), 2, AwayFromZero)
public record BidLine(string Id, string TenantId, string LegalEntityId, string BidId, string LotId,
    decimal Qty, decimal UnitPrice, decimal DiscountPct)
{
    public decimal LineTotal => decimal.Round(Qty * UnitPrice * (1m - DiscountPct / 100m), 2, MidpointRounding.AwayFromZero);
}

// child rows (assignments, scores, lines, invitations, price lines, gates) use deterministic
// natural string ids — no extra number-series prefixes beyond the eight the brief lists
public record EvaluatorAssignment(string Id, string TenantId, string LegalEntityId, string EventId, string UserId);

public record ScoreCard(string Id, string TenantId, string LegalEntityId, string EventId, string BidId,
    string CriterionId, string EvaluatorUserId, decimal? RawScore, bool? PassFail, string? Comment,
    int CriteriaVersion, DateTimeOffset SubmittedAt);

public record Award(string Id, string TenantId, string LegalEntityId, string EventId, string LotId,
    string SupplierId, decimal Qty, decimal UnitPrice, AwardStatus Status, int BasisEventVersion,
    string Recommendation, string? ApprovedBy, DateTimeOffset? ApprovedAt, string? RejectionReason,
    string CreatedBy, DateTimeOffset CreatedAt);

// commitment-only (R-T6f): no journal is ever posted from a contract
public record Contract(string Id, string TenantId, string LegalEntityId, string SupplierId, string? AwardId,
    ContractStatus Status, string Currency, DateOnly EffectiveFrom, DateOnly EffectiveTo,
    int? RenewalNoticeDays, int? OptionPeriodsTotal, int OptionsExercised, string OwnerUserId, int Version,
    string? ActivatedBy, DateTimeOffset? ActivatedAt, string CreatedBy, DateTimeOffset CreatedAt);

// executed versions are never overwritten — this table is insert-only (037 AC1/AC2)
public record ContractAmendment(string Id, string TenantId, string LegalEntityId, string ContractId,
    int Version, DateOnly EffectiveDate, string Summary, string ApprovedBy, DateTimeOffset ApprovedAt);

public record ContractPriceLine(string Id, string TenantId, string LegalEntityId, string ContractId,
    string ItemSku, decimal UnitPrice, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public record ContractObligation(string Id, string TenantId, string LegalEntityId, string ContractId,
    string ObligationType, string OwnerUserId, DateOnly DueDate, string? SourceClause, ObligationStatus Status,
    string? EvidenceRef, string? VerifiedBy, DateTimeOffset? VerifiedAt, string? WaiverReason,
    string? CreatedBy = null); // trailing optional: maker-checker anchor for verify/waive (038)

// effective-dated eligibility criterion (023 AC1, 044 AC1); a time-bounded exception (023 AC3)
// lives on the gate row itself — ExceptionBy/ExceptionUntil
public record SupplierGate(string Id, string TenantId, string LegalEntityId, string SupplierId,
    string GateCode, GateSeverity Severity, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? EvidenceRef,
    string? ExceptionBy, DateTimeOffset? ExceptionUntil);
