using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IForecastService
{
    (object? data, bool resubmitted, string? code, string? message) Submit(CurrentUser u, string owner, string period, ForecastCategory category, decimal? overrideValue);
    (object? data, string? code, string? message) Report(CurrentUser u, string period);
    (object? data, string? code, string? message) CreateSnapshot(CurrentUser u, string period);
    (object? data, string? code, string? message) Snapshots(string period);
}
