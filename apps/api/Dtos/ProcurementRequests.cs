namespace Erp.Api.Dtos;

using Erp.Api;

public record CreateSupplierRequest(string Name, string TaxId, string Email);
public record CreatePoRequest(string SupplierId, IReadOnlyList<PoLine> Lines);
public record ApprovePoRequest(string? Reason);
public record CreateReceiptRequest(string PurchaseOrderId, string ItemSku, decimal Qty, string? ToleranceOverrideReason = null);

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

// ==================== deferred PROCUREMENT wave request DTOs (P1..P6) ====================

// P1 requisitions
public record CreateRequisitionRequest(string? Purpose, string? Currency, IReadOnlyList<RequisitionLineInput>? Lines);
public record RequisitionLineInput(string? CategoryId, string? Description, decimal Qty, decimal UnitPrice, string? Unit, DateOnly? NeedBy);
public record SubmitRequisitionRequest(string? OverrideReason);
public record RequisitionDecisionRequest(string? Decision, int Version, string? Reason);
public record FlagExceptionRequest(string? ReasonCode, string? Narrative, string? EvidenceRef);
public record CreateDelegationRequest(string? DelegateId, DateTimeOffset From, DateTimeOffset To, decimal? MaxAmount, string? CategoryId, string? Reason);

// P2 taxonomy + units
public record CreateCategoryRequest(string? Name, string? ParentId, string? ExternalCode, DateOnly? EffectiveFrom);
public record UpdateCategoryRequest(string? Name, string? ParentId, string? ExternalCode, DateOnly? EffectiveTo);
public record DeprecateCategoryRequest();
public record CreateUomRequest(string? Code, string? Name, UomDimension Dimension, int Precision, bool IsBase);
public record CreateConversionRequest(string? ToCode, decimal Factor, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

// P3 PO change control + blanket orders
public record CreatePoChangeRequest(string? Rationale, string? SupplierId, IReadOnlyList<PoChangeLineInput>? Lines);
public record PoChangeLineInput(string ItemSku, decimal? Qty, decimal? UnitPrice);
public record DecidePoChangeRequest(bool Approve);
public record CreatePoAckRequest(string? SupplierId, int OrderVersion, IReadOnlyList<PoAckLineInput>? Lines);
public record PoAckLineInput(string ItemSku, string Status);
public record CreateBlanketOrderRequest(string? SupplierId, string? Currency, decimal CeilingAmount, DateOnly ValidFrom, DateOnly ValidTo);
public record CreateBlanketReleaseRequest(decimal Amount, string? PoId);

// P4 receipt workspace
public record CreateDispatchRequest(int OrderVersion, DispatchState State, string? Detail);
public record CreateAsnRequest(string? PoId, string? ShipmentRef, int OrderVersion, IReadOnlyList<AsnLineInput>? Lines);
public record AsnLineInput(string? ItemSku, decimal Qty);
public record CreateInspectionRequest(string? ItemSku, decimal QtyReceived);
public record DisposeInspectionRequest(InspectionStatus Status, decimal QtyAccepted, decimal QtyRejected, string? DefectCode, string? EvidenceRef);
public record CreateSupplierReturnRequest(string? ReceiptId, string? PoId, string? ItemSku, decimal Qty, string? Reason, ClaimKind Kind);
public record CreateSupplierClaimRequest(string? ReceiptId, string? PoId, ClaimKind Kind, decimal? Amount);
public record CloseClaimRequest(string? OutcomeCode, string? ReconciliationStatus);

// P5 supplier qualification + scorecards
public record CreateQuestionnaireRequest(string? Name, IReadOnlyList<QuestionnaireQuestionInput>? Questions);
public record QuestionnaireQuestionInput(string Code, string Prompt, bool Required, bool Conditional, string? ConditionalOnCode);
public record CreateQuestionnaireResponseRequest(string? QuestionnaireId, string? AnswersJson);
public record ReviewQuestionnaireResponseRequest(string ReviewDecision, string? FieldCommentsJson);
public record AddEvidenceRequest(string? EvidenceType, string? MetadataJson, DateOnly? ExpiresOn, string? SupersedesId);
public record AddScreeningRequest(string? Source, string? SourceRef);
public record DisposeScreeningRequest(string? Disposition, string? Reason);
public record AddObservationRequest(string? Metric, decimal Value, string? Period, string? Source);
public record DisputeObservationRequest(string? Reason, decimal? AmendedValue);
public record CreateScorecardRequest(string? SupplierId, DateOnly PeriodFrom, DateOnly PeriodTo);
public record AddScorecardSupplierResponseRequest(string? Response);
public record AddCorrectiveActionRequest(string? Finding, string? Action);
public record CloseCorrectiveActionRequest(string? Outcome);
