namespace Erp.Api;

// T7 appended CreditHold LAST (057 AC2): EF stores enums as ints — inserting mid-enum would
// renumber every existing sales_orders.status row in postgres. Deferred-screens wave appends
// PendingAmendmentApproval (034) + Cancelled (039) at the END for the same reason.
public enum SoStatus { Draft, PendingApproval, Confirmed, Shipped, Invoiced, Rejected, CreditHold, PendingAmendmentApproval, Cancelled }

// Customer trailing optionals (T7): credit limit + payment terms live on the customer row
// (057 AC1 sources, 053 AC1). Existing 8-arg call sites compile unchanged. Deferred-screens
// wave (S3): hierarchy parent + merge redirect — 012/014.
public record Customer(string Id, string TenantId, string LegalEntityId, string Name, string TaxId, string Email, DateTimeOffset CreatedAt, string CreatedBy,
    decimal? CreditLimit = null, int PaymentTermNetDays = 30, bool RemindersSuppressed = false, string? ParentCustomerId = null, string? MergedIntoId = null);

// ExpectedDate (037): backorder expected date stamped by a short shipment against the line
public record SoLine(string Id, string ItemSku, string Description, decimal Qty, decimal UnitPrice, DateOnly? ExpectedDate = null);

// Lines as a settable body property for EF (see PurchaseOrder) — same JSON shape, same call sites.
// SourceQuoteRevisionId (030 AC1): the accepted quote revision stamped on the generated order.
public record SalesOrder(string Id, string TenantId, string LegalEntityId, string CustomerId, SoStatus Status, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, string? RejectionReason = null, string? SourceQuoteRevisionId = null)
{
    public List<SoLine> Lines { get; set; } = [];
    public SalesOrder(string id, string tenantId, string legalEntityId, string customerId, SoStatus status,
        IReadOnlyList<SoLine> lines, string createdBy, DateTimeOffset createdAt,
        string? approvedBy = null, DateTimeOffset? approvedAt = null, string? rejectionReason = null,
        string? sourceQuoteRevisionId = null)
        : this(id, tenantId, legalEntityId, customerId, status, createdBy, createdAt, approvedBy, approvedAt, rejectionReason, sourceQuoteRevisionId)
    { Lines = [.. lines]; }
}

public record Shipment(string Id, string TenantId, string LegalEntityId, string SalesOrderId, string ItemSku, decimal Qty, DateTimeOffset ShippedAt, string ShippedBy);

// --- task-7 SALES deep: CRM, opportunities, invoice documents, collections ---

// 004: qualification lifecycle; Disqualified→Recycled→Working is the salvage path
public enum LeadStatus { New, Working, Qualified, Disqualified, Converted, Recycled }

// 003: ReceivedAt is the SERVER receipt time; Company/ContactName optional (anonymous inquiries).
// TaxId trailing optional (R-T7 deviation): duplicate detection at conversion matches the
// normalized tax id against existing customers (005) — the brief's Lead shape omitted the field
// its own ruling requires.
public record Lead(string Id, string TenantId, string LegalEntityId, string? Company, string? ContactName, string? Email, string? Phone, string Source, string? SourceRef, LeadStatus Status, string? ReasonCode, string OwnerId, DateTimeOffset ReceivedAt, string CreatedBy, DateTimeOffset CreatedAt, string? TaxId = null);

public enum InteractionPartyType { Lead, Customer, Opportunity }

// 007: OccurredAt (when it happened) is shown separately from RecordedAt (server receipt)
public record Interaction(string Id, string TenantId, string LegalEntityId, InteractionPartyType PartyType, string PartyId, string Channel, string Subject, string? Notes, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, string CreatedBy);

public enum CrmCaseStatus { Open, InProgress, Resolved, Closed }
// CasePriority is reused from Models/Assets.cs — R-T7d keeps CRM cases a SEPARATE entity from
// ASSETS ServiceCases (distinct lifecycles, distinct permissions); only the priority enum is shared.

// 008: response target computed at intake from priority (same flat hours mapping as service cases)
public record CrmCase(string Id, string TenantId, string LegalEntityId, string? CustomerId, string Category, CasePriority Priority, CrmCaseStatus Status, string? OwnerId, DateTimeOffset ResponseDueAt, string? Resolution, string? SalesOrderId, string CreatedBy, DateTimeOffset CreatedAt);

public enum OpportunityStatus { Open, Won, Lost }

// 015/016: early-stage opportunities may link a Lead before conversion; OrderId stamped at
// closed-won (016 AC2); LossReason required on Lost (019 AC2)
public record Opportunity(string Id, string TenantId, string LegalEntityId, string? CustomerId, string? LeadId, string Title, decimal Amount, string Currency, OpportunityStatus Status, string Stage, DateOnly? ExpectedCloseDate, string OwnerId, string? LossReason, string? Competitor, string? OrderId, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<OpportunityLine> Lines { get; set; } = [];
    public Opportunity(string id, string tenantId, string legalEntityId, string? customerId, string? leadId,
        string title, decimal amount, string currency, OpportunityStatus status, string stage,
        DateOnly? expectedCloseDate, string ownerId, string? lossReason, string? competitor, string? orderId,
        string createdBy, DateTimeOffset createdAt, IReadOnlyList<OpportunityLine>? lines = null)
        : this(id, tenantId, legalEntityId, customerId, leadId, title, amount, currency, status, stage,
            expectedCloseDate, ownerId, lossReason, competitor, orderId, createdBy, createdAt)
    { if (lines is not null) Lines = [.. lines]; }
}

// 017: opportunity total = Σ Round(Qty × TargetPrice, 2); the PK is a generated shadow line
// number (same owned-line pattern as count_lines/journal_lines — server-assembled, no client ids)
public record OpportunityLine(string ItemSku, string Description, decimal Qty, decimal TargetPrice);

// Discarded APPENDED (fix round 1, review 1b): a mis-drafted invoice must not be a dead end —
// discard is terminal, frees the shipment source event for re-billing, and burns the INV number.
public enum InvoiceStatus { Draft, Posted, Discarded }

// 042/043/045: SourceEventId is the shipment that triggered the document (no double billing);
// tax components are pinned per line at posting (048 AC2); DueDate is computed from the
// customer's payment term at posting and never recomputed (053 AC2)
public record Invoice(string Id, string TenantId, string LegalEntityId, string CustomerId, string SalesOrderId, string SourceEventId, InvoiceStatus Status, string Currency, DateOnly InvoiceDate, DateOnly? DueDate, string PaymentTermCode, decimal SubTotal, decimal TaxTotal, decimal Total, string? JournalId, string? PostedBy, DateTimeOffset? PostedAt, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<InvoiceLine> Lines { get; set; } = [];
    public Invoice(string id, string tenantId, string legalEntityId, string customerId, string salesOrderId,
        string sourceEventId, InvoiceStatus status, string currency, DateOnly invoiceDate, DateOnly? dueDate,
        string paymentTermCode, decimal subTotal, decimal taxTotal, decimal total,
        string? journalId, string? postedBy, DateTimeOffset? postedAt, string createdBy, DateTimeOffset createdAt,
        IReadOnlyList<InvoiceLine>? lines = null)
        : this(id, tenantId, legalEntityId, customerId, salesOrderId, sourceEventId, status, currency, invoiceDate,
            dueDate, paymentTermCode, subTotal, taxTotal, total, journalId, postedBy, postedAt, createdBy, createdAt)
    { if (lines is not null) Lines = [.. lines]; }
}

// TaxCodeId + TaxCodeVersion + TaxAmount pinned at posting → a replay reproduces the components
// without consulting the current rate (048 AC2). Shadow line-number PK (server-assembled lines).
public record InvoiceLine(string ItemSku, string Description, decimal Qty, decimal UnitPrice, decimal LineNet, string? TaxCodeId = null, int? TaxCodeVersion = null, decimal TaxAmount = 0);

public enum AdjustmentKind { Credit, Debit }
public enum AdjustmentStatus { Draft, Approved, Rejected }

// 046: every adjustment references an original invoice (standalone memos deferred, R-T7k); the
// document totals are finalized at approval when the reversing journal posts
public record AdjustmentDocument(string Id, string TenantId, string LegalEntityId, AdjustmentKind Kind, string InvoiceId, string Reason, decimal SubTotal, decimal TaxTotal, decimal Total, AdjustmentStatus Status, string? JournalId, string? ApprovedBy, DateTimeOffset? ApprovedAt, string? RejectionReason, string CreatedBy, DateTimeOffset CreatedAt)
{
    public List<AdjustmentLine> Lines { get; set; } = [];
    public AdjustmentDocument(string id, string tenantId, string legalEntityId, AdjustmentKind kind, string invoiceId,
        string reason, decimal subTotal, decimal taxTotal, decimal total, AdjustmentStatus status,
        string? journalId, string? approvedBy, DateTimeOffset? approvedAt, string? rejectionReason,
        string createdBy, DateTimeOffset createdAt, IReadOnlyList<AdjustmentLine>? lines = null)
        : this(id, tenantId, legalEntityId, kind, invoiceId, reason, subTotal, taxTotal, total, status,
            journalId, approvedBy, approvedAt, rejectionReason, createdBy, createdAt)
    { if (lines is not null) Lines = [.. lines]; }
}

public record AdjustmentLine(string ItemSku, string Description, decimal Qty, decimal UnitPrice, decimal LineNet, string? TaxCodeId = null, int? TaxCodeVersion = null, decimal TaxAmount = 0);

// 058 AC2: append-only reminder history
public record CollectionReminder(string Id, string TenantId, string LegalEntityId, string InvoiceId, string Channel, string? TemplateVersion, string Recipient, DateTimeOffset RemindedAt, string CreatedBy);

public enum DisputeStatus { Open, Promised, Resolved }

// 059: an Open/Promised dispute suppresses reminders; promise expiry → overdue follow-up flag
public record Dispute(string Id, string TenantId, string LegalEntityId, string InvoiceId, string Reason, DisputeStatus Status, DateOnly? PromiseDueDate, string? Outcome, string OwnerId, string CreatedBy, DateTimeOffset CreatedAt);
