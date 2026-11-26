using Erp.Api;

namespace Erp.Api.Interfaces;

// 065: role-filtered CRM/order/billing metrics with documented definitions and drill-through ids
public interface ISalesAnalyticsService
{
    (object? data, string? code, string? message) Report(CurrentUser u, string domain, string? groupBy);
}
