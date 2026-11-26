namespace Erp.Api.Interfaces;

public interface IProcurementAnalyticsRepository
{
    List<(string SupplierId, decimal Amount, DateTimeOffset CreatedAt)> ApprovedPoSpend(DateOnly? from, DateOnly? to);
    List<(string? Currency, decimal Amount, DateTimeOffset CreatedAt)> SubmittedRequisitionDemand();
    List<(string Status, int Count)> SourcingEventCounts();
}

// P6 (063): authorized read-only metrics with documented, reconcilable definitions. Multi-currency
// amounts are never converted or summed across currencies — rows list separately per currency.
public interface IProcurementAnalyticsService
{
    object Spend(string? groupBy, DateOnly? from, DateOnly? to);
    object Demand(string? groupBy);
    object Sourcing();
}
