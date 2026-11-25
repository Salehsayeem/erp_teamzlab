using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// P6 read-only queries over existing tables — tenant+entity scoped like every other repo (R14).
public sealed class ProcurementAnalyticsRepository(ErpDbContext db, EntityContext ec) : IProcurementAnalyticsRepository
{
    public List<(string SupplierId, decimal Amount, DateTimeOffset CreatedAt)> ApprovedPoSpend(DateOnly? from, DateOnly? to)
    {
        var query = db.PurchaseOrders
            .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId
                && (p.Status == PoStatus.Approved || p.Status == PoStatus.Sent || p.Status == PoStatus.Received))
            .Include(p => p.Lines).AsQueryable();
        if (from is { } f) query = query.Where(p => p.CreatedAt >= new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (to is { } t) query = query.Where(p => p.CreatedAt <= new DateTimeOffset(t.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
        return query.OrderBy(p => p.Id).ToList()
            .Select(p => (p.SupplierId, p.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)), p.CreatedAt))
            .ToList();
    }

    public List<(string? Currency, decimal Amount, DateTimeOffset CreatedAt)> SubmittedRequisitionDemand()
    {
        var requisitions = db.Requisitions
            .Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId
                && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved))
            .Include(r => r.Lines)
            .OrderBy(r => r.Id).ToList();
        return requisitions
            .Select(r => (r.Currency, r.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)), r.CreatedAt))
            .ToList();
    }

    public List<(string Status, int Count)> SourcingEventCounts() =>
        db.SourcingEvents
            .Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId)
            .GroupBy(e => e.Status)
            .Select(g => new ValueTuple<string, int>(g.Key.ToString(), g.Count()))
            .ToList();
}
