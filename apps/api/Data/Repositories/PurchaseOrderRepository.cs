using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Purchase orders + receipts + weighted-average costing (used to be Common/Costing.cs).
// Mutations are tracked-entity writes (task-2 §2) — no `with`-replacement on aggregates.
public sealed class PurchaseOrderRepository(ErpDbContext db, EntityContext ec) : IPurchaseOrderRepository
{
    private IQueryable<PurchaseOrder> Scoped() =>
        db.PurchaseOrders.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);

    private IQueryable<Receipt> ReceiptsScoped() =>
        db.Receipts.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    private List<PurchaseOrder> Ordered(IQueryable<PurchaseOrder> query) =>
        query.Include(p => p.Lines).OrderBy(p => p.Id).ToList(); // owned lines live in po_lines — include on every read

    public PurchaseOrder? Find(string poId) =>
        Ordered(Scoped().Where(p => p.Id == poId)).FirstOrDefault();

    public List<PurchaseOrder> All() => Ordered(Scoped());

    public void Add(PurchaseOrder po)
    {
        db.PurchaseOrders.Add(po);
        foreach (var line in po.Lines) SetLineTenant(line);
    }

    public void MarkSubmitted(PurchaseOrder po) =>
        Set(po, nameof(PurchaseOrder.Status), PoStatus.PendingApproval);

    public void MarkApproved(PurchaseOrder po, string approverId)
    {
        Set(po, nameof(PurchaseOrder.Status), PoStatus.Approved);
        Set(po, nameof(PurchaseOrder.ApprovedBy), approverId);
        Set(po, nameof(PurchaseOrder.ApprovedAt), DateTimeOffset.UtcNow);
    }

    public void MarkRejected(PurchaseOrder po, string approverId, string reason)
    {
        Set(po, nameof(PurchaseOrder.Status), PoStatus.Rejected);
        Set(po, nameof(PurchaseOrder.ApprovedBy), approverId);
        Set(po, nameof(PurchaseOrder.ApprovedAt), DateTimeOffset.UtcNow);
        Set(po, nameof(PurchaseOrder.RejectionReason), reason);
    }

    public void MarkReceived(PurchaseOrder po) =>
        Set(po, nameof(PurchaseOrder.Status), PoStatus.Received);

    public void AddReceipt(Receipt receipt) => db.Receipts.Add(receipt);

    public decimal ReceivedQty(string poId, string itemSku) =>
        ReceiptsScoped().Where(r => r.PurchaseOrderId == poId && r.ItemSku == itemSku).Sum(r => (decimal?)r.Qty) ?? 0m;

    public Dictionary<string, decimal> ReceivedBySku(string poId)
    {
        var sums = ReceiptsScoped().Where(r => r.PurchaseOrderId == poId)
            .GroupBy(r => r.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        // receipts added earlier in this request are tracked but unsaved — include them like the
        // in-memory list did
        foreach (var r in db.Receipts.Local.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.PurchaseOrderId == poId))
            sums[r.ItemSku] = sums.GetValueOrDefault(r.ItemSku) + r.Qty;
        return sums;
    }

    public Receipt? FindReceiptReferencedBy(string tenantId, string legalEntityId, string memo) =>
        db.Receipts.Where(r => r.TenantId == tenantId && r.LegalEntityId == legalEntityId)
            .ToList().FirstOrDefault(r => memo.Contains(r.Id, StringComparison.Ordinal));

    public decimal AverageUnitCost(string itemSku)
    {
        var receipts = ReceiptsScoped().Where(r => r.ItemSku == itemSku).ToList();
        var totalQty = receipts.Sum(r => r.Qty);
        return totalQty > 0
            ? decimal.Round(receipts.Sum(r => r.Qty * ReceiptUnitCost(r.PurchaseOrderId, itemSku)) / totalQty, 4, MidpointRounding.AwayFromZero)
            : 0m;
    }

    private decimal ReceiptUnitCost(string poId, string itemSku) =>
        db.PurchaseOrders.Include(p => p.Lines)
            .First(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId && p.Id == poId)
            .Lines.First(l => l.ItemSku == itemSku).UnitPrice;

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;

    // line tables carry tenant_id + legal_entity_id as shadow columns so the records keep their shape
    private void SetLineTenant(object line)
    {
        db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
        db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
    }
}
