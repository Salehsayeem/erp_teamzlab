using Erp.Api;

namespace Erp.Api.Dtos;

// MFG deep slice request DTOs — validated with Guards-style checks in the controller BEFORE the
// service runs (CLAUDE.md). Free-text reasons accept Unicode; enum values stay locale-neutral.
public record CreateBomRequest(string OutputSku, int Revision, decimal OutputQtyPer, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public record AddBomLineRequest(string ComponentSku, decimal QtyPer, string Unit, IssuePolicy IssuePolicy, decimal UnitCost);

// approval and retirement share one shape — both carry an optional free-text reason
public record BomDecisionRequest(string? Reason);

public record CreateProductionOrderRequest(string OutputSku, decimal DemandedQty, string Unit, decimal OverTolerancePct);

// Status drives the transition; NewPlannedQty (with the client echoing the current Status) is the
// post-release quantity-amendment path — the two never combine in one call
public record TransitionOrderRequest(ProductionOrderStatus Status, decimal? NewPlannedQty, string? Reason);

public record ReleaseOrderRequest(string? Reason);

public record MaterialIssueRequest(string ComponentSku, decimal Qty);

public record IssueReturnRequest(string IssueId, decimal Qty);

// MF10 (065): optional lot/serial identity — required when the output sku is lot-controlled
// (mfg.lot_controlled_skus profile); the transformation event carries it forward-only
public record CompletionRequest(decimal Qty, string? Note, string? LotSerial = null);

public record ScrapRequest(string ItemSku, decimal Qty, string ReasonCode, ScrapDisposition Disposition);

// the explicit acknowledgment that unblocks a debit WIP remainder as the residual line (013 AC1)
public record CloseOrderRequest(string? ResidualAcknowledgment);

public record ReopenOrderRequest(string Reason);
