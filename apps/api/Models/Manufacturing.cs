namespace Erp.Api;

// --- MFG deep slice (ERP-MFG-007/008/010/013/014/015/022/041/042/043/067/069, R-MFG1..11) ---
// Trims by ruling: single-output BOMs, stock-backed lines only (R-MFG2); order-level completion,
// no operations (R-MFG4); BOM-line UnitCost is entered tenant data — completions post at the
// planned unit cost derived from the released revision, the residual reconciles at close (R-MFG5);
// backflush is the per-line IssuePolicy flag (R-MFG7); movements are synchronous same-DB (R-MFG8).

// 014: approved revisions are immutable — a change is a NEW revision (successor). EF maps enums
// by int: every enum here is append-only, never insert a value mid-list.
public enum BomStatus { Draft, Approved, Retired }

// R-MFG7 (043 trim): backflush lives on the BOM line — no item/site-level profile
public enum IssuePolicy { Manual, Backflush }

// 007 lifecycle: Draft→Planned→Released→InProcess→Completed→Closed + Cancelled (pre-execution only).
// Status is a concurrency token on production_orders — APPEND-ONLY, renumbering would rewrite history.
public enum ProductionOrderStatus { Draft, Planned, Released, InProcess, Completed, Closed, Cancelled }

public enum MovementKind { Issue, Return }

public enum ScrapDisposition { Disposal, ReturnToStock }

// 014: effective intervals must not overlap a live revision of the same output sku — exactly one
// live revision is selectable per sku on a date (AC2). Approved rows are never mutated.
public record BomRevision(string Id, string TenantId, string LegalEntityId, string OutputSku, int Revision,
    BomStatus Status, decimal OutputQtyPer, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string? ApprovedBy, DateTimeOffset? ApprovedAt, string CreatedBy, DateTimeOffset CreatedAt);

// 015: component lines of a revision — QtyPer is per batch basis (OutputQtyPer); UnitCost is the
// entered cost that feeds the 069 handoff and the planned unit cost (R-MFG5, Q5 trim)
public record BomLine(string Id, string TenantId, string LegalEntityId, string BomRevisionId,
    string ComponentSku, decimal QtyPer, string Unit, IssuePolicy IssuePolicy, decimal UnitCost);

// 008: the five distinct quantity values (demanded/planned/released/completed + computed remaining,
// never stored twice); 022: PlannedBomRevisionId resolved at create, ReleasedBomRevisionId frozen
// at release. ClosedBy/ClosedAt survive a reopen (013 AC3 — the prior close is never erased).
public record ProductionOrder(string Id, string TenantId, string LegalEntityId, string OrderNo, string OutputSku,
    string Unit, decimal DemandedQty, decimal PlannedQty, decimal ReleasedQty, decimal CompletedQty,
    ProductionOrderStatus Status, decimal OverTolerancePct, string? PlannedBomRevisionId, string? ReleasedBomRevisionId,
    string? CancelReason, string? ClosedBy, DateTimeOffset? ClosedAt, string? ReopenedBy, DateTimeOffset? ReopenedAt,
    string CreatedBy, DateTimeOffset CreatedAt);

// 008 AC3: post-release quantity amendments as versioned change rows (pre-release edits mutate in place)
public record OrderAmendment(string Id, string TenantId, string LegalEntityId, string OrderId,
    string Field, string OldValue, string NewValue, string Reason, string ChangedBy, DateTimeOffset ChangedAt);

// 042: immutable movement rows — corrections are new linked movements (a Return links its issue and
// never exceeds the issue's remaining returnable qty). JournalId is the movement reference recorded
// against the order (042 AC2); LinkedCompletionId ties backflush auto-issues to their completion (043 AC2).
public record MaterialIssue(string Id, string TenantId, string LegalEntityId, string OrderId, string ComponentSku,
    MovementKind Kind, decimal Qty, decimal UnitCost, decimal LineValue, string? LinkedIssueId, string? JournalId,
    string ReportedBy, DateTimeOffset ReportedAt, string? LinkedCompletionId = null);

// 041 trim: order-level completion reporting; CompletedQty accumulates from these rows
public record CompletionReport(string Id, string TenantId, string LegalEntityId, string OrderId,
    decimal Qty, string? Note, string? JournalId, string ReportedBy, DateTimeOffset ReportedAt);

// 067: required reason code; Disposal moves physical stock (Dr 5200/Cr 1400), ReturnToStock posts
// nothing; every row enters close reconciliation (067 AC3)
public record ScrapReport(string Id, string TenantId, string LegalEntityId, string OrderId,
    string ItemSku, decimal Qty, decimal UnitCost, string ReasonCode, ScrapDisposition Disposition,
    string? JournalId, string ReportedBy, DateTimeOffset ReportedAt);
