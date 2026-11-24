namespace Erp.Api.Dtos;

using Erp.Api;

public record CreateCustomerRequest(string Name, string TaxId, string Email,
    decimal? CreditLimit = null, int PaymentTermNetDays = 30, bool RemindersSuppressed = false);
public record CreateSoRequest(string CustomerId, IReadOnlyList<SoLine> Lines);
public record ApproveSoRequest(string? Reason);
public record ShipRequest(string ItemSku, decimal Qty);
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
