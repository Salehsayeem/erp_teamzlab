using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// MFG deep tables — tenant+entity scope on every query (R14); ids come from the shared doc_
// sequences (BOM/MFO/ISS/CPL/SCR), child rows use deterministic natural keys. State flips go
// through the Set helper; the production_orders.status column is a concurrency token, so every
// guarded transition UPDATE carries WHERE status = <original> (stale retries write nothing).
public sealed class ManufacturingRepository(ErpDbContext db, EntityContext ec) : IManufacturingRepository
{
    private IQueryable<BomRevision> BomsScoped() =>
        db.BomRevisions.Where(b => b.TenantId == ec.TenantId && b.LegalEntityId == ec.EntityId);

    private IQueryable<BomLine> BomLinesScoped() =>
        db.BomLines.Where(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId);

    private IQueryable<ProductionOrder> OrdersScoped() =>
        db.ProductionOrders.Where(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId);

    private IQueryable<OrderAmendment> AmendmentsScoped() =>
        db.OrderAmendments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);

    private IQueryable<MaterialIssue> IssuesScoped() =>
        db.MaterialIssues.Where(i => i.TenantId == ec.TenantId && i.LegalEntityId == ec.EntityId);

    private IQueryable<CompletionReport> CompletionsScoped() =>
        db.CompletionReports.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    private IQueryable<ScrapReport> ScrapsScoped() =>
        db.ScrapReports.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    // ---- BOM revisions ----

    public List<BomRevision> AllBoms(string? outputSku, BomStatus? status)
    {
        var query = BomsScoped();
        if (outputSku is not null) query = query.Where(b => b.OutputSku == outputSku);
        if (status is not null) query = query.Where(b => b.Status == status);
        return query.OrderBy(b => b.OutputSku).ThenBy(b => b.Revision).ToList();
    }

    public BomRevision? FindBom(string bomId) => BomsScoped().FirstOrDefault(b => b.Id == bomId);

    public List<BomRevision> LiveBomsFor(string outputSku) =>
        BomsScoped().Where(b => b.OutputSku == outputSku && b.Status != BomStatus.Retired).ToList();

    public List<BomRevision> EffectiveRevisionsFor(string outputSku, DateOnly date) =>
        BomsScoped().Where(b => b.OutputSku == outputSku && b.Status == BomStatus.Approved
                && b.EffectiveFrom <= date && (b.EffectiveTo == null || b.EffectiveTo >= date))
            .OrderBy(b => b.Revision).ToList();

    public void AddBom(BomRevision bom) => db.BomRevisions.Add(bom);

    public void SetBomApproved(BomRevision bom, string approvedBy, DateTimeOffset approvedAt)
    {
        Set(bom, nameof(BomRevision.Status), BomStatus.Approved);
        Set(bom, nameof(BomRevision.ApprovedBy), approvedBy);
        Set(bom, nameof(BomRevision.ApprovedAt), approvedAt);
    }

    public void SetBomRetired(BomRevision bom) => Set(bom, nameof(BomRevision.Status), BomStatus.Retired);

    public List<BomLine> LinesForRevision(string bomRevisionId) =>
        BomLinesScoped().Where(l => l.BomRevisionId == bomRevisionId).OrderBy(l => l.ComponentSku).ToList();

    public List<BomLine> LinesForRevisions(IReadOnlyCollection<string> bomRevisionIds) =>
        BomLinesScoped().Where(l => bomRevisionIds.Contains(l.BomRevisionId))
            .OrderBy(l => l.BomRevisionId).ThenBy(l => l.ComponentSku).ToList();

    public BomLine? FindLine(string bomRevisionId, string componentSku) =>
        BomLinesScoped().FirstOrDefault(l => l.BomRevisionId == bomRevisionId && l.ComponentSku == componentSku);

    public void AddLine(BomLine line) => db.BomLines.Add(line);

    // ---- Production orders ----

    public List<ProductionOrder> AllOrders(ProductionOrderStatus? status, string? outputSku)
    {
        var query = OrdersScoped();
        if (status is not null) query = query.Where(o => o.Status == status);
        if (outputSku is not null) query = query.Where(o => o.OutputSku == outputSku);
        return query.OrderBy(o => o.Id).ToList();
    }

    public ProductionOrder? FindOrder(string orderId) => OrdersScoped().FirstOrDefault(o => o.Id == orderId);

    public void AddOrder(ProductionOrder order) => db.ProductionOrders.Add(order);

    public void SetOrderStatus(ProductionOrder order, ProductionOrderStatus status) =>
        Set(order, nameof(ProductionOrder.Status), status);

    public void SetOrderCancelled(ProductionOrder order, string reason)
    {
        Set(order, nameof(ProductionOrder.Status), ProductionOrderStatus.Cancelled);
        Set(order, nameof(ProductionOrder.CancelReason), reason);
    }

    public void SetOrderPlannedQty(ProductionOrder order, decimal plannedQty) =>
        Set(order, nameof(ProductionOrder.PlannedQty), plannedQty);

    public void SetOrderReleased(ProductionOrder order, string releasedBomRevisionId, decimal releasedQty)
    {
        Set(order, nameof(ProductionOrder.Status), ProductionOrderStatus.Released);
        Set(order, nameof(ProductionOrder.ReleasedBomRevisionId), releasedBomRevisionId);
        Set(order, nameof(ProductionOrder.ReleasedQty), releasedQty);
    }

    public void SetOrderCompletedQty(ProductionOrder order, decimal completedQty) =>
        Set(order, nameof(ProductionOrder.CompletedQty), completedQty);

    public void SetOrderClosed(ProductionOrder order, string closedBy, DateTimeOffset closedAt)
    {
        Set(order, nameof(ProductionOrder.Status), ProductionOrderStatus.Closed);
        Set(order, nameof(ProductionOrder.ClosedBy), closedBy);
        Set(order, nameof(ProductionOrder.ClosedAt), closedAt);
    }

    public void SetOrderReopened(ProductionOrder order, string reopenedBy, DateTimeOffset reopenedAt)
    {
        // 013 AC3: the prior close stays on the row (ClosedBy/ClosedAt untouched) — reopen only
        // lifts the reporting freeze and stamps who/when
        Set(order, nameof(ProductionOrder.Status), ProductionOrderStatus.InProcess);
        Set(order, nameof(ProductionOrder.ReopenedBy), reopenedBy);
        Set(order, nameof(ProductionOrder.ReopenedAt), reopenedAt);
    }

    public List<OrderAmendment> AmendmentsFor(string orderId) =>
        AmendmentsScoped().Where(a => a.OrderId == orderId).OrderBy(a => a.Id).ToList();

    public void AddAmendment(OrderAmendment amendment) => db.OrderAmendments.Add(amendment);

    // ---- Reporting rows ----

    public List<MaterialIssue> IssuesFor(string orderId) =>
        IssuesScoped().Where(i => i.OrderId == orderId).OrderBy(i => i.Id).ToList();

    public MaterialIssue? FindIssue(string orderId, string issueId) =>
        IssuesScoped().FirstOrDefault(i => i.OrderId == orderId && i.Id == issueId);

    public decimal ReturnedQtyFor(string issueId) =>
        IssuesScoped().Where(i => i.LinkedIssueId == issueId && i.Kind == MovementKind.Return)
            .Sum(i => (decimal?)i.Qty) ?? 0m;

    public void AddIssue(MaterialIssue issue) => db.MaterialIssues.Add(issue);

    public List<CompletionReport> CompletionsFor(string orderId) =>
        CompletionsScoped().Where(c => c.OrderId == orderId).OrderBy(c => c.Id).ToList();

    public void AddCompletion(CompletionReport completion) => db.CompletionReports.Add(completion);

    public List<ScrapReport> ScrapsFor(string orderId) =>
        ScrapsScoped().Where(s => s.OrderId == orderId).OrderBy(s => s.Id).ToList();

    public void AddScrap(ScrapReport scrap) => db.ScrapReports.Add(scrap);

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
