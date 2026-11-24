using Erp.Api.Interfaces;

namespace Erp.Api.Interfaces;

// MFG deep aggregate — BOM revisions + lines, production orders + amendments, material issues,
// completion reports and scrap reports. Every query filters tenant+entity from the scoped
// EntityContext (R14); state flips go through the Set helper so EF writes positional-record
// properties via backing fields. ProductionOrder.Status is a concurrency token — a stale second
// transition updates zero rows and surfaces as DbUpdateConcurrencyException (T7 review 1a).
public interface IManufacturingRepository
{
    // BOM revisions
    List<BomRevision> AllBoms(string? outputSku, BomStatus? status);
    BomRevision? FindBom(string bomId);
    List<BomRevision> LiveBomsFor(string outputSku); // Draft + Approved — the overlap guard set
    List<BomRevision> EffectiveRevisionsFor(string outputSku, DateOnly date); // Approved, interval covers date
    void AddBom(BomRevision bom);
    void SetBomApproved(BomRevision bom, string approvedBy, DateTimeOffset approvedAt);
    void SetBomRetired(BomRevision bom);

    List<BomLine> LinesForRevision(string bomRevisionId);
    List<BomLine> LinesForRevisions(IReadOnlyCollection<string> bomRevisionIds);
    BomLine? FindLine(string bomRevisionId, string componentSku);
    void AddLine(BomLine line);

    // Production orders
    List<ProductionOrder> AllOrders(ProductionOrderStatus? status, string? outputSku);
    ProductionOrder? FindOrder(string orderId);
    void AddOrder(ProductionOrder order);
    void SetOrderStatus(ProductionOrder order, ProductionOrderStatus status);
    void SetOrderCancelled(ProductionOrder order, string reason);
    void SetOrderPlannedQty(ProductionOrder order, decimal plannedQty);
    void SetOrderReleased(ProductionOrder order, string releasedBomRevisionId, decimal releasedQty);
    void SetOrderCompletedQty(ProductionOrder order, decimal completedQty);
    void SetOrderClosed(ProductionOrder order, string closedBy, DateTimeOffset closedAt);
    void SetOrderReopened(ProductionOrder order, string reopenedBy, DateTimeOffset reopenedAt);

    List<OrderAmendment> AmendmentsFor(string orderId);
    void AddAmendment(OrderAmendment amendment);

    // Reporting rows (all immutable once written)
    List<MaterialIssue> IssuesFor(string orderId);
    MaterialIssue? FindIssue(string orderId, string issueId);
    decimal ReturnedQtyFor(string issueId);
    void AddIssue(MaterialIssue issue);

    List<CompletionReport> CompletionsFor(string orderId);
    void AddCompletion(CompletionReport completion);

    List<ScrapReport> ScrapsFor(string orderId);
    void AddScrap(ScrapReport scrap);

    int Save();
}
