using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P6 (063): every metric states its numerator/definition inline; rows reconcile to the filtered
// detail; amounts are NEVER converted — multi-currency rows list separately (063 edge). Scope
// comes from the tenant-scoped repository, so other tenants' rows cannot leak (063 AC3).
public sealed class ProcurementAnalyticsService(IProcurementAnalyticsRepository analytics) : IProcurementAnalyticsService
{
    private const string SpendDefinition = "sum of qty × unitPrice over approved+ purchase orders in the window";
    private const string DemandDefinition = "sum of qty × unitPrice over submitted/approved requisitions, listed per currency without conversion";

    public object Spend(string? groupBy, DateOnly? from, DateOnly? to)
    {
        var spend = analytics.ApprovedPoSpend(from, to);
        var rows = groupBy switch
        {
            "period" => spend.GroupBy(s => s.CreatedAt.ToString("yyyy-MM"))
                .Select(g => new { key = g.Key, amount = g.Sum(x => x.Amount), currency = (string?)null, count = g.Count() })
                .OrderBy(r => r.key).ToList(),
            _ => spend.GroupBy(s => s.SupplierId)
                .Select(g => new { key = g.Key, amount = g.Sum(x => x.Amount), currency = (string?)null, count = g.Count() })
                .OrderBy(r => r.key).ToList(),
        };
        return new
        {
            metric = "spend", groupBy = groupBy switch { "period" => "period", _ => "supplier" },
            definition = SpendDefinition, from, to, total = spend.Sum(s => s.Amount), rows,
        };
    }

    public object Demand(string? groupBy)
    {
        var demand = analytics.SubmittedRequisitionDemand();
        var rows = demand.GroupBy(d => d.Currency ?? "unspecified")
            .Select(g => new { key = g.Key, amount = g.Sum(x => x.Amount), currency = g.Key, count = g.Count() })
            .OrderBy(r => r.key).ToList();
        // the 063 edge made explicit: no cross-currency total is emitted at all
        return new
        {
            metric = "demand", groupBy = "currency", definition = DemandDefinition,
            total = (decimal?)null, rows,
        };
    }

    public object Sourcing()
    {
        var counts = analytics.SourcingEventCounts();
        var rows = counts.Select(c => new { key = c.Status, count = c.Count }).OrderBy(r => r.key).ToList();
        return new
        {
            metric = "sourcing", groupBy = "event_status",
            definition = "count of sourcing events by lifecycle status",
            total = counts.Sum(c => c.Count), rows,
        };
    }
}
