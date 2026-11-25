namespace Erp.Api;

// Deferred SALES screens wave (ERP-SALES-012..014/018/020..026/030/034..040/050/051/054..056/
// 061..064/065). All tables tenant+entity scoped from day one; ids come from the shared doc_
// sequences (QTE/PBL/RCS/DLV/RMA/RCP/RBT) or deterministic natural keys for child rows —
// same convention as the sourcing/contract tables. Enums are new tables only, so ordering
// is free (the append-only rule guards EXISTING stored enums like SoStatus).

// --- S1 quotes (021/022/023/030) ---

public enum QuoteStatus { Draft, Accepted }
public enum QuoteRevisionStatus { Draft, PendingApproval, Approved, Rejected }
public enum QuoteApprovalStatus { Approved, Rejected }

// Quote.Id = doc_qte (QTE-####); the accepted revision is pinned by quote_acceptances rows.
public record Quote(string Id, string TenantId, string LegalEntityId, string CustomerId, string Currency, string? Terms, DateOnly? ExpiresAt, QuoteStatus Status, int CurrentRevision, string CreatedBy, DateTimeOffset CreatedAt);

// Revision.Id = "{quoteId}:R{n}" — material change = new revision row, prior retained (022 AC1)
public record QuoteRevision(string Id, string TenantId, string LegalEntityId, string QuoteId, int Revision, QuoteRevisionStatus Status, decimal SubTotal, decimal DiscountTotal, decimal TaxTotal, decimal Total, string? PriceEvidenceJson, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null)
{
    public List<QuoteLine> Lines { get; set; } = [];
    public QuoteRevision(string id, string tenantId, string legalEntityId, string quoteId, int revision,
        QuoteRevisionStatus status, decimal subTotal, decimal discountTotal, decimal taxTotal, decimal total,
        string? priceEvidenceJson, string createdBy, DateTimeOffset createdAt,
        IReadOnlyList<QuoteLine>? lines = null, string? approvedBy = null, DateTimeOffset? approvedAt = null)
        : this(id, tenantId, legalEntityId, quoteId, revision, status, subTotal, discountTotal, taxTotal, total,
            priceEvidenceJson, createdBy, createdAt, approvedBy, approvedAt)
    { if (lines is not null) Lines = [.. lines]; }
}

// shadow (RevisionId, LineNo) PK like so_lines; TaxTotal stays 0 — quotes are pre-tax documents
// (021 AC2 reconciliation: Total = SubTotal − DiscountTotal + TaxTotal, recomputed from lines)
public record QuoteLine(string ItemSku, string Description, decimal Qty, decimal UnitPrice, decimal DiscountPct);

// Revision approvals are a fresh set per revision — a post-approval material edit invalidates
// them by never carrying them over (023 AC2). Id = "{revisionId}:{approverId}"
public record QuoteApproval(string Id, string TenantId, string LegalEntityId, string RevisionId, string ApproverId, QuoteApprovalStatus Status, string? Reason, DateTimeOffset CreatedAt);

// 030 AC2: idempotent on externalEventId (unique per tenant). Id = "{revisionId}:{externalEventId ?? token}"
public record QuoteAcceptance(string Id, string TenantId, string LegalEntityId, string QuoteId, string RevisionId, string Method, string? ExternalEventId, string? EvidenceRef, string AcceptedBy, DateTimeOffset AcceptedAt);

// --- S2 price books + contract pricing (024/025/026) ---

// Book.Id = doc_pbl (PBL-####); entries are effective-dated; overlap is a service-level check
public record PriceBook(string Id, string TenantId, string LegalEntityId, string Name, string Currency, int Precedence, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<PriceBookEntry> Entries { get; set; } = [];
}

// shadow (BookId, LineNo) PK — Rule carries the resolution evidence label (024 AC1)
public record PriceBookEntry(string ItemSku, decimal MinQty, decimal UnitPrice, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Rule);

// Contract price wins over books by precedence (026 AC1); Id natural per customer+sku+from+minQty
public record CustomerContractPrice(string Id, string TenantId, string LegalEntityId, string CustomerId, string ItemSku, decimal MinQty, decimal UnitPrice, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string CreatedBy, DateTimeOffset CreatedAt);

// --- S3 customer 360 (012/013/014) — Customer gains ParentCustomerId/MergedIntoId (Sales.cs) ---

public enum OnboardingCheckStatus { Pending, Met }

// config-per-account rows; Id = "{customerId}:{checkCode}" — 013 AC2 unmet control IDs come from these
public record CustomerOnboardingCheck(string Id, string TenantId, string LegalEntityId, string CustomerId, string CheckCode, bool Mandatory, OnboardingCheckStatus Status, DateTimeOffset? MetAt = null, string? MetBy = null, string CreatedBy = "", DateTimeOffset CreatedAt = default);

public enum PartyMergeStatus { Previewed, Executed }

// 014: elevated permission + reason + immutable audit; posted balances block (MERGE_BLOCKED).
// Id = "PMG-xxxx" — preview computes reassignments, execution applies them.
public record PartyMerge(string Id, string TenantId, string LegalEntityId, string SurvivorId, string MergedId, string Reason, PartyMergeStatus Status, string? ReassignmentsJson, string RequestedBy, DateTimeOffset RequestedAt, string? ExecutedBy = null, DateTimeOffset? ExecutedAt = null);

// --- S4 order lifecycle (034..037/039/040; 038/041 deferred per Q-S4/Q-S5) ---

public enum AmendmentStatus { Pending, Approved, Rejected }

// material edit → amendment holding the NEW lines; order lines stay prior until approval (034 AC1)
// Id = "{soId}:A{n}". Named SalesOrderAmendment — MFG already owns OrderAmendment + the
// order_amendments table (name-collision lesson); this aggregate lands in so_amendments.
public record SalesOrderAmendment(string Id, string TenantId, string LegalEntityId, string SalesOrderId, int Revision, AmendmentStatus Status, string Rationale, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null)
{
    public List<SoLine> Lines { get; set; } = [];
    public SalesOrderAmendment(string id, string tenantId, string legalEntityId, string salesOrderId, int revision,
        AmendmentStatus status, string rationale, string createdBy, DateTimeOffset createdAt,
        IReadOnlyList<SoLine>? lines = null, string? approvedBy = null, DateTimeOffset? approvedAt = null, string? rejectionReason = null)
        : this(id, tenantId, legalEntityId, salesOrderId, revision, status, rationale, createdBy, createdAt, approvedBy, approvedAt, rejectionReason)
    { if (lines is not null) Lines = [.. lines]; }
}

public enum OrderHoldStatus { Active, Cleared }
public enum OrderHoldType { Credit, Compliance, Pricing, Manual }

// 035: blocking hold gates shipment; set/clear carry actor + time. Id = "{soId}:H{n}"
public record OrderHold(string Id, string TenantId, string LegalEntityId, string SalesOrderId, OrderHoldType HoldType, OrderHoldStatus Status, string Reason, string Source, string SetBy, DateTimeOffset SetAt, string? ClearedBy = null, DateTimeOffset? ClearedAt = null);

public enum CancellationScope { Remaining, Full }

// 039: dated event + reason + remaining-qty outcome; posted invoices remain (credit-note flow)
// Id = "{soId}:C{n}"
public record OrderCancellation(string Id, string TenantId, string LegalEntityId, string SalesOrderId, CancellationScope Scope, string Reason, string OutcomeJson, string CancelledBy, DateTimeOffset CancelledAt);

public enum RecurringFrequency { Weekly, Monthly }
public enum RecurringScheduleStatus { Active, Paused }

// 040: ≤1 occurrence per (schedule, period) — unique index makes generation idempotent
public record RecurringSchedule(string Id, string TenantId, string LegalEntityId, string CustomerId, RecurringFrequency Frequency, RecurringScheduleStatus Status, DateOnly NextRunDate, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<SoLine> Lines { get; set; } = [];
    public RecurringSchedule(string id, string tenantId, string legalEntityId, string customerId,
        RecurringFrequency frequency, RecurringScheduleStatus status, DateOnly nextRunDate,
        string createdBy, DateTimeOffset createdAt, IReadOnlyList<SoLine>? lines = null)
        : this(id, tenantId, legalEntityId, customerId, frequency, status, nextRunDate, createdBy, createdAt)
    { if (lines is not null) Lines = [.. lines]; }
}

// Id = "{scheduleId}:{periodKey}" (periodKey "2026-11" monthly / ISO week "2026-W45")
public record RecurringOccurrence(string Id, string TenantId, string LegalEntityId, string ScheduleId, string PeriodKey, string OrderId, string GeneratedBy, DateTimeOffset GeneratedAt);

// --- S5 forecast categories + snapshots (018/020) ---

public enum ForecastCategory { Pipeline, BestCase, Commit }

// 018 AC2: submission records owner/period/time/version; resubmission bumps Version in place.
// Id = "{ownerId}:{period}:{category}"
public record ForecastSubmission(string Id, string TenantId, string LegalEntityId, string OwnerId, string Period, ForecastCategory Category, decimal? OverrideValue, int Version, string SubmittedBy, DateTimeOffset SubmittedAt);

public enum SnapshotStatus { Complete, Incomplete }

// 020: immutable freeze of {includedIds, values, categories, generatedAt}; failures → Incomplete
public record ForecastSnapshot(string Id, string TenantId, string LegalEntityId, string Period, SnapshotStatus Status, string PayloadJson, string GeneratedBy, DateTimeOffset GeneratedAt);

// --- S6 e-invoice adapter + delivery tracking (050/051) ---

public enum EinvoiceStatus { NotEnabled, ValidationFailed, Transmitted }

// adapter boundary result pinned per submission; Id = "{invoiceId}:EIV:{n}"
public record EinvoiceSubmission(string Id, string TenantId, string LegalEntityId, string InvoiceId, string Jurisdiction, string SchemaVersion, string CorrelationId, EinvoiceStatus Status, string? ErrorsJson, string SubmittedBy, DateTimeOffset SubmittedAt);

public enum DeliveryStatus { Sent, Delivered, Bounced }

// 051: resend = new attempt on the same posted revision; bounce never mutates payment state.
// Id = doc_dlv (DLV-####)
public record InvoiceDelivery(string Id, string TenantId, string LegalEntityId, string InvoiceId, string Channel, string Recipient, DeliveryStatus Status, int Attempt, string? Reason, DateTimeOffset SentAt, DateTimeOffset? BouncedAt = null, string CreatedBy = "", DateTimeOffset CreatedAt = default);

// --- S7 returns/RMA + refunds (061/062) ---

public enum ReturnStatus { Authorized, PartiallyReceived, Completed }

// 061: authorized ≤ sold − prior returns unless override approved; received tracked separately.
// Id = doc_rma (RMA-####)
public record SalesReturn(string Id, string TenantId, string LegalEntityId, string SalesOrderId, string CustomerId, ReturnStatus Status, string Reason, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<ReturnLine> Lines { get; set; } = [];
    public SalesReturn(string id, string tenantId, string legalEntityId, string salesOrderId, string customerId,
        ReturnStatus status, string reason, string createdBy, DateTimeOffset createdAt, IReadOnlyList<ReturnLine>? lines = null)
        : this(id, tenantId, legalEntityId, salesOrderId, customerId, status, reason, createdBy, createdAt)
    { if (lines is not null) Lines = [.. lines]; }
}

// shadow (ReturnId, LineNo) PK; Condition = Resellable/Damage (free text kept short)
public record ReturnLine(string ItemSku, decimal Qty, string Reason, string Condition);

public record ReturnReceipt(string Id, string TenantId, string LegalEntityId, string ReturnId, decimal ReceivedQty, string ReceivedBy, DateTimeOffset ReceivedAt);

public enum RefundStatus { Requested, Approved, Rejected }

// 062: posting a credit alone never completes a refund; refund approval is R-FIN + ≠requester.
// Id = "{adjustmentId}:REF" — one refund per credit adjustment, idempotent on retry
public record RefundRequest(string Id, string TenantId, string LegalEntityId, string AdjustmentId, string InvoiceId, decimal Amount, RefundStatus Status, string RequestedBy, DateTimeOffset RequestedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null);

// --- S8 receipt candidates + AR recon (054/055/056) ---

public enum ReceiptCandidateStatus { Unapplied, Applied }

// 054: bank/provider reference + raw amount/currency preserved; unmatched stays Unapplied.
// Allocation stays FINANCE-owned (055) — no allocation write here. Id = doc_rcp (RCP-####)
public record ReceiptCandidate(string Id, string TenantId, string LegalEntityId, string? CustomerId, string BankRef, decimal Amount, string Currency, DateOnly ValueDate, string Channel, ReceiptCandidateStatus Status, string? PayerRef, string CreatedBy, DateTimeOffset CreatedAt);

// --- S9 rebates + commission boundary (063/064) ---

public enum RebateAgreementStatus { Active, Retired }

// Id = doc_rbt (RBT-####); versioned — retro change = new version row (recalc preview first)
public record RebateAgreement(string Id, string TenantId, string LegalEntityId, string CustomerId, string Basis, decimal RatePct, string AccrualPeriod, RebateAgreementStatus Status, int Version, DateOnly ValidFrom, DateOnly? ValidTo, string CreatedBy, DateTimeOffset CreatedAt);

public enum RebateAccrualStatus { Calculated, Approved }

// Id = "{agreementId}:{period}"; settlement cap = ApprovedAmount (063 AC2)
public record RebateAccrual(string Id, string TenantId, string LegalEntityId, string AgreementId, string Period, decimal AccruedAmount, decimal? ApprovedAmount, string EligibleTxnJson, int AgreementVersion, RebateAccrualStatus Status, string CalculatedBy, DateTimeOffset CalculatedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null);

public enum RebateClaimStatus { Submitted, Approved, Rejected }

// Id = "{agreementId}:{period}:C{n}"; settlement above approved accrual → REBATE_EXCEEDS_ACCRUAL
public record RebateClaim(string Id, string TenantId, string LegalEntityId, string AgreementId, string Period, decimal Amount, int AgreementVersion, string EligibleTxnJson, RebateClaimStatus Status, string RequestedBy, DateTimeOffset RequestedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null);
