namespace Erp.Api.Interfaces;

public interface IFiscalPeriodService
{
    object List(PeriodStatus? status, DateOnly? from, DateOnly? to);
    (FiscalPeriod? period, string? code, string? message) Create(CurrentUser user, Dtos.CreatePeriodRequest request);
    (FiscalPeriod? period, string? code, string? message) Close(CurrentUser user, string periodId, string? reason);
    (FiscalPeriod? period, string? code, string? message) Reopen(CurrentUser user, string periodId, string reason);
}
