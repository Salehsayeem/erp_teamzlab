namespace Erp.Api.Dtos;

using Erp.Api;

public record CreateCustomerRequest(string Name, string TaxId, string Email,
    decimal? CreditLimit = null, int PaymentTermNetDays = 30, bool RemindersSuppressed = false);
public record CreateSoRequest(string CustomerId, IReadOnlyList<SoLine> Lines, string? SourceQuoteRevisionId = null);
public record ApproveSoRequest(string? Reason);
// ExpectedDate (037): stamped on the line when a shipment leaves a measurable remainder
public record ShipRequest(string ItemSku, decimal Qty, DateOnly? ExpectedDate = null);
public record ReleaseCreditRequest();

// --- task-7 SALES deep ---

public record CreateLeadRequest(string? Company, string? ContactName, string? Email, string? Phone, string Source, string? SourceRef, string? TaxId = null);
public record TransitionLeadRequest(LeadStatus Status, string? ReasonCode);
public record ConvertLeadRequest();

public record LogInteractionRequest(InteractionPartyType PartyType, string PartyId, string Channel, string Subject, string? Notes, DateTimeOffset OccurredAt);

public record CreateCrmCaseRequest(string? CustomerId, string Category, CasePriority Priority, string? SalesOrderId);
public record TransitionCrmCaseRequest(CrmCaseStatus Status, string? Resolution);

public record CreateOpportunityRequest(string? CustomerId, string? LeadId, string Title, string Currency, DateOnly? ExpectedCloseDate, string OwnerId);
public record AddOpportunityLineRequest(string ItemSku, string Description, decimal Qty, decimal TargetPrice);
public record TransitionOpportunityRequest(OpportunityStatus Status, string Stage, string? OrderId, string? LossReason, string? Competitor);

public record CreateInvoiceDraftRequest(DateOnly? InvoiceDate = null);
public record PostInvoiceRequest();

public record CreateAdjustmentRequest(AdjustmentKind Kind, string InvoiceId, string Reason, IReadOnlyList<AdjustmentLineInput> Lines);
public record AdjustmentLineInput(string ItemSku, decimal Qty, decimal UnitPrice, string? TaxCodeId = null, string? Description = null);
public record ApproveAdjustmentRequest(string? Reason);

public record LogReminderRequest(string InvoiceId, string Channel, string? TemplateVersion, string Recipient);
public record CreateDisputeRequest(string InvoiceId, string Reason, DateOnly? PromiseDueDate);
public record ResolveDisputeRequest(string Outcome);

// --- deferred-screens wave: S1 quotes + S2 pricing ---

public record CreateQuoteRequest(string CustomerId, string Currency, IReadOnlyList<QuoteLineRequest>? Lines, string? Terms = null, DateOnly? ExpiresAt = null);
public record QuoteLineRequest(string ItemSku, decimal Qty, decimal? UnitPrice = null, decimal DiscountPct = 0);
public record CreateQuoteRevisionRequest(IReadOnlyList<QuoteLineRequest>? Lines, string? Rationale = null, string? Terms = null, DateOnly? ExpiresAt = null);
public record ApproveQuoteRevisionRequest(string? Reason = null);
public record AcceptQuoteRequest(string Method, string? ExternalEventId = null, string? EvidenceRef = null);
public record RevalidateQuoteRequest(DateOnly ExpiresAt, string Reason);

public record CreatePriceBookRequest(string Name, string Currency, int Precedence);
public record CreateBookEntryRequest(string ItemSku, decimal MinQty, decimal UnitPrice, DateOnly EffectiveFrom, DateOnly? EffectiveTo = null, string? Rule = null);
public record CreateContractPriceRequest(string CustomerId, string ItemSku, decimal MinQty, decimal UnitPrice, DateOnly EffectiveFrom, DateOnly? EffectiveTo = null);

// --- deferred-screens wave: S3 customer 360 ---

public record SetHierarchyRequest(string? ParentId);
public record AddOnboardingCheckRequest(string CheckCode, bool Mandatory = true);
public record MergePreviewRequest(string SurvivorId, string MergedId, string Reason);
public record MergeExecuteRequest(string? Reason = null);

// --- deferred-screens wave: S4 order lifecycle ---

public record PlaceHoldRequest(string HoldType, string Reason, string? Source = null);
public record ReleaseHoldRequest(string? Reason = null);
public record CreateAmendmentRequest(IReadOnlyList<SoLine>? Lines, string Rationale);
public record ApproveAmendmentRequest(string? Reason = null);
public record CancelOrderRequest(string Reason, string Scope);
public record CreateScheduleRequest(string CustomerId, string Frequency, DateOnly NextRunDate, IReadOnlyList<SoLine>? Lines);
public record GenerateRequest(string Period);
public record PauseRequest();

// --- deferred-screens wave: S5 forecast + S6 e-invoice/delivery ---

public record SubmitForecastRequest(string Owner, string Period, string Category, decimal? OverrideValue = null);
public record CreateSnapshotRequest(string Period);
public record SubmitEinvoiceRequest();
public record DeliveryAttemptRequest(string Channel, string Recipient);
public record DeliveryBounceRequest(string Reason);
