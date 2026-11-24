namespace Erp.Api.Dtos;

using Erp.Api;

public record CreateSupplierRequest(string Name, string TaxId, string Email);
public record CreatePoRequest(string SupplierId, IReadOnlyList<PoLine> Lines);
public record ApprovePoRequest(string? Reason);
public record CreateReceiptRequest(string PurchaseOrderId, string ItemSku, decimal Qty);

// ==================== T6 PROCUREMENT deep request DTOs ====================
// Validated with Guards-style checks in the controllers before any service runs. Two deliberate
// extensions to the briefed shapes (rulings logged in task-6-report.md):
//  · AnswerQaRequest carries Question + SupplierId — with portal intake deferred (R-T6a) the buyer
//    records the question too, and supplier-specific answers must name the supplier.
//  · AddSupplierGateRequest trails optional ExceptionUntil — the time-bounded exception (023 AC3,
//    R-T6e: granted under suppliers:write) is recorded on the gate row itself.

public record CreateEventRequest(string Title, SourcingEventType EventType, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, string TimeZoneName, string Currency);
public record AddLotRequest(string LotNo, string Description, string? ItemSku, decimal? TargetQty, decimal? CeilingValue);
public record AddCriterionRequest(string Code, string Name, CriterionKind Kind, decimal WeightPct, int ScaleMax);
public record PublishEventRequest();
public record InviteSuppliersRequest(IReadOnlyList<string> SupplierIds);
public record AnswerQaRequest(string Question, string Answer, QaScope Scope, string? SupplierId, bool Publish);
public record SubmitBidRequest(string SupplierId, IReadOnlyList<BidLineInput> Lines);
public record BidLineInput(string LotId, decimal Qty, decimal UnitPrice, decimal DiscountPct);
public record OpenBidsRequest();
public record AssignEvaluatorsRequest(IReadOnlyList<string> UserIds);
public record SubmitScoreRequest(string BidId, string CriterionId, decimal? RawScore, bool? PassFail, string? Comment);
public record RecommendAwardRequest(string EventId, string LotId, string SupplierId, decimal Qty, decimal UnitPrice, string Recommendation);
public record ApproveAwardRequest(string? Reason);
public record CreateContractRequest(string SupplierId, string? AwardId, string Currency, DateOnly EffectiveFrom, DateOnly EffectiveTo, int? RenewalNoticeDays, int? OptionPeriodsTotal);
public record ActivateContractRequest();
public record AddAmendmentRequest(DateOnly EffectiveDate, string Summary, IReadOnlyList<ContractPriceLineInput> PriceLines);
public record ContractPriceLineInput(string ItemSku, decimal UnitPrice, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public record AddObligationRequest(string ObligationType, string OwnerUserId, DateOnly DueDate, string? SourceClause);
public record SubmitEvidenceRequest(string EvidenceRef);
public record VerifyObligationRequest();
public record WaiveObligationRequest(string Reason);
public record ExerciseRenewalRequest(int OptionNumber);
public record SetSupplierStatusRequest(SupplierLifecycleStatus Status, string Reason);
public record AddSupplierGateRequest(string GateCode, GateSeverity Severity, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? EvidenceRef, DateTimeOffset? ExceptionUntil = null);
