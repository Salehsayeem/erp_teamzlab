using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Sales orders + shipments — same tracked-entity mutation pattern as the PO side
public sealed class SalesOrderRepository(ErpDbContext db, EntityContext ec) : ISalesOrderRepository
{
    private IQueryable<SalesOrder> Scoped() =>
        db.SalesOrders.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    private List<SalesOrder> Ordered(IQueryable<SalesOrder> query) =>
        query.Include(s => s.Lines).OrderBy(s => s.Id).ToList(); // owned lines live in so_lines

    public SalesOrder? Find(string soId) =>
        Ordered(Scoped().Where(s => s.Id == soId)).FirstOrDefault();

    public List<SalesOrder> All() => Ordered(Scoped());

    // 057 exposure basis (R-T7e): Confirmed and CreditHold orders are open commitments
    public List<SalesOrder> OpenForCustomer(string customerId) =>
        Ordered(Scoped().Where(s => s.CustomerId == customerId
            && (s.Status == SoStatus.Confirmed || s.Status == SoStatus.CreditHold)));

    public void Add(SalesOrder so)
    {
        db.SalesOrders.Add(so);
        foreach (var line in so.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public void MarkSubmitted(SalesOrder so) =>
        Set(so, nameof(SalesOrder.Status), SoStatus.PendingApproval);

    public void MarkConfirmed(SalesOrder so, string approverId)
    {
        Set(so, nameof(SalesOrder.Status), SoStatus.Confirmed);
        Set(so, nameof(SalesOrder.ApprovedBy), approverId);
        Set(so, nameof(SalesOrder.ApprovedAt), DateTimeOffset.UtcNow);
    }

    public void MarkRejected(SalesOrder so, string approverId, string reason)
    {
        Set(so, nameof(SalesOrder.Status), SoStatus.Rejected);
        Set(so, nameof(SalesOrder.ApprovedBy), approverId);
        Set(so, nameof(SalesOrder.ApprovedAt), DateTimeOffset.UtcNow);
        Set(so, nameof(SalesOrder.RejectionReason), reason);
    }

    public void MarkShipped(SalesOrder so) => Set(so, nameof(SalesOrder.Status), SoStatus.Shipped);

    public void MarkInvoiced(SalesOrder so) => Set(so, nameof(SalesOrder.Status), SoStatus.Invoiced);

    // 057 AC2: approval happened but the exposure gate held the order — ApprovedBy stamps the
    // officer who made the hold decision (release is a separate audited act)
    public void MarkCreditHold(SalesOrder so, string approverId)
    {
        Set(so, nameof(SalesOrder.Status), SoStatus.CreditHold);
        Set(so, nameof(SalesOrder.ApprovedBy), approverId);
        Set(so, nameof(SalesOrder.ApprovedAt), DateTimeOffset.UtcNow);
    }

    public void AddShipment(Shipment shipment) => db.Shipments.Add(shipment);

    // source events for billing — earliest first (invoice drafts pick the oldest unbilled one)
    public List<Shipment> ShipmentsFor(string soId) =>
        db.Shipments.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId)
            .OrderBy(sh => sh.Id).ToList();

    public Dictionary<string, decimal> ShippedBySku(string soId)
    {
        var rows = db.Shipments.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId)
            .Select(sh => new { sh.Id, sh.ItemSku, sh.Qty }).ToList();
        var sums = rows.GroupBy(sh => sh.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        // same hazard as ReceivedBySku: the query materializes committed shipments into .Local —
        // merge only rows it did not already return or later shipments flip the SO Shipped early
        var seen = rows.Select(sh => sh.Id).ToHashSet();
        foreach (var sh in db.Shipments.Local.Where(sh => sh.TenantId == ec.TenantId && sh.LegalEntityId == ec.EntityId && sh.SalesOrderId == soId))
            if (seen.Add(sh.Id))
                sums[sh.ItemSku] = sums.GetValueOrDefault(sh.ItemSku) + sh.Qty;
        return sums;
    }

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;

    // --- deferred-screens wave (S4): lifecycle rows ---------------------------------

    public void SetSoStatus(SalesOrder so, SoStatus status) =>
        Set(so, nameof(SalesOrder.Status), status);

    public void SetLineExpectedDate(SalesOrder so, string itemSku, DateOnly expectedDate)
    {
        var line = so.Lines.FirstOrDefault(l => l.ItemSku == itemSku);
        if (line is not null) Set(line, nameof(SoLine.ExpectedDate), expectedDate);
    }

    // 034 AC1 approval applies the amendment's lines onto the order (prior revision preserved
    // on the amendment row itself). Shared-type owned rows must be reached through the OWNER's
    // collection entry — a direct db.Entry(line) throws on untracked shared-type instances.
    public void ReplaceLines(SalesOrder so, IReadOnlyList<SoLine> lines)
    {
        so.Lines = [.. lines];
        db.ChangeTracker.DetectChanges();
        var collection = db.Entry(so).Collection(s => s.Lines);
        foreach (var line in so.Lines)
        {
            var entry = collection.FindEntry(line);
            if (entry is null) continue;
            entry.Property("TenantId").CurrentValue = ec.TenantId;
            entry.Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public void AddHold(OrderHold hold) => db.OrderHolds.Add(hold);

    public OrderHold? FindHold(string holdId) =>
        db.OrderHolds.FirstOrDefault(h => h.TenantId == ec.TenantId && h.LegalEntityId == ec.EntityId && h.Id == holdId);

    public List<OrderHold> HoldsFor(string soId) =>
        db.OrderHolds.Where(h => h.TenantId == ec.TenantId && h.LegalEntityId == ec.EntityId && h.SalesOrderId == soId)
            .OrderBy(h => h.SetAt).ToList();

    public int ActiveHoldCount(string soId) =>
        db.OrderHolds.Count(h => h.TenantId == ec.TenantId && h.LegalEntityId == ec.EntityId
            && h.SalesOrderId == soId && h.Status == OrderHoldStatus.Active);

    public void SetHoldCleared(OrderHold hold, string clearedBy)
    {
        Set(hold, nameof(OrderHold.Status), OrderHoldStatus.Cleared);
        Set(hold, nameof(OrderHold.ClearedBy), clearedBy);
        Set(hold, nameof(OrderHold.ClearedAt), DateTimeOffset.UtcNow);
    }

    public void AddAmendment(SalesOrderAmendment amendment)
    {
        db.SalesOrderAmendments.Add(amendment);
        foreach (var line in amendment.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public SalesOrderAmendment? FindAmendment(string amendmentId) =>
        db.SalesOrderAmendments.Include(a => a.Lines)
            .FirstOrDefault(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId && a.Id == amendmentId);

    public List<SalesOrderAmendment> AmendmentsFor(string soId) =>
        db.SalesOrderAmendments.Include(a => a.Lines)
            .Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId && a.SalesOrderId == soId)
            .OrderBy(a => a.Revision).ToList();

    public void SetAmendmentStatus(SalesOrderAmendment amendment, AmendmentStatus status, string? approverId)
    {
        Set(amendment, nameof(SalesOrderAmendment.Status), status);
        if (approverId is not null)
        {
            Set(amendment, nameof(SalesOrderAmendment.ApprovedBy), approverId);
            Set(amendment, nameof(SalesOrderAmendment.ApprovedAt), DateTimeOffset.UtcNow);
        }
    }

    public void AddCancellation(OrderCancellation cancellation) => db.OrderCancellations.Add(cancellation);

    public List<OrderCancellation> CancellationsFor(string soId) =>
        db.OrderCancellations.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId && c.SalesOrderId == soId)
            .OrderBy(c => c.CancelledAt).ToList();

    public void AddSchedule(RecurringSchedule schedule)
    {
        db.RecurringSchedules.Add(schedule);
        foreach (var line in schedule.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public RecurringSchedule? FindSchedule(string scheduleId) =>
        db.RecurringSchedules.Include(s => s.Lines)
            .FirstOrDefault(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Id == scheduleId);

    public List<RecurringSchedule> Schedules() =>
        db.RecurringSchedules.Include(s => s.Lines)
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId)
            .OrderBy(s => s.Id).ToList();

    public void SetScheduleStatus(RecurringSchedule schedule, RecurringScheduleStatus status) =>
        Set(schedule, nameof(RecurringSchedule.Status), status);

    public RecurringOccurrence? FindOccurrence(string scheduleId, string periodKey) =>
        db.RecurringOccurrences.FirstOrDefault(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId
            && o.ScheduleId == scheduleId && o.PeriodKey == periodKey);

    public void AddOccurrence(RecurringOccurrence occurrence) => db.RecurringOccurrences.Add(occurrence);
}
