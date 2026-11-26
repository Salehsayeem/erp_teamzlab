using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Sales performance analytics (ERP-SALES-065), read-only. Metric definitions (documented in
// docs/05_API_Spec.md §6): crm = open opportunity count/value by owner; orders = sales-order
// count/net value by status or customer; billing = posted invoice open value by customer.
// Every row carries drill-through identifiers; a caller without the domain's read permission
// gets 403 and sees no protected rows (065 AC2).
public sealed class SalesAnalyticsService(
    IOpportunityRepository opportunities, ISalesOrderRepository orders, IInvoiceRepository invoices) : ISalesAnalyticsService
{
    private static decimal OppValue(Opportunity o) =>
        o.Lines.Sum(l => decimal.Round(l.Qty * l.TargetPrice, 2, MidpointRounding.AwayFromZero));

    private static decimal OrderTotal(SalesOrder o) =>
        o.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero));

    public (object? data, string? code, string? message) Report(CurrentUser u, string domain, string? groupBy)
    {
        switch (domain)
        {
            case "crm":
                if (!u.Has("crm:read") && !u.Has("opps:read"))
                    return (null, "FORBIDDEN", "crm:read or opps:read permission required.");
                var open = opportunities.All().Where(o => o.Status == OpportunityStatus.Open).ToList();
                return (new
                {
                    domain, definition = "open opportunities; value = sum of line qty × target price (017)",
                    rows = Group(open, groupBy == "customer" ? o => o.CustomerId ?? "none" : o => o.OwnerId,
                        g => new
                        {
                            groupKey = g.Key, count = g.Count(),
                            total = decimal.Round(g.Sum(OppValue), 2, MidpointRounding.AwayFromZero),
                            docIds = g.Select(o => o.Id),
                        }),
                }, null, null);
            case "orders":
                if (!u.Has("so:write") && !u.Has("so:approve"))
                    return (null, "FORBIDDEN", "so:write or so:approve permission required.");
                var all = orders.All();
                return (new
                {
                    domain, definition = "sales orders; value = sum of line qty × unit price, all statuses",
                    rows = Group(all, groupBy == "customer" ? o => o.CustomerId : o => o.Status.ToString(),
                        g => new
                        {
                            groupKey = g.Key, count = g.Count(),
                            total = decimal.Round(g.Sum(OrderTotal), 2, MidpointRounding.AwayFromZero),
                            docIds = g.Select(o => o.Id),
                        }),
                }, null, null);
            case "billing":
                if (!u.Has("invoices:read") && !u.Has("ar:read"))
                    return (null, "FORBIDDEN", "invoices:read or ar:read permission required.");
                var posted = invoices.AllInvoices(InvoiceStatus.Posted, null);
                return (new
                {
                    domain, definition = "posted invoices; value = invoice total (tax-inclusive, 048)",
                    rows = Group(posted, i => i.CustomerId,
                        g => new
                        {
                            groupKey = g.Key, count = g.Count(),
                            total = decimal.Round(g.Sum(i => i.Total), 2, MidpointRounding.AwayFromZero),
                            docIds = g.Select(i => i.Id),
                        }),
                }, null, null);
            default:
                return (null, "VALIDATION_ERROR", "domain must be crm, orders or billing.");
        }
    }

    private static List<object> Group<T>(List<T> source, Func<T, string> key, Func<IGrouping<string, T>, object> project) =>
        source.GroupBy(key).OrderBy(g => g.Key).Select(project).Cast<object>().ToList();
}
