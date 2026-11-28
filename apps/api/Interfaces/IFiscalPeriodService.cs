namespace Erp.Api.Interfaces;

public interface IFiscalPeriodService
{
    object List(PeriodStatus? status, DateOnly? from, DateOnly? to);
    (FiscalPeriod? period, string? code, string? message) Create(CurrentUser user, Dtos.CreatePeriodRequest request);
    // waiveBlockers=true is the 063 AC2 waiver variant — close:approve is enforced at the controller
    (FiscalPeriod? period, string? code, string? message) Close(CurrentUser user, string periodId, string? reason, bool waiveBlockers = false);
    (FiscalPeriod? period, string? code, string? message) Reopen(CurrentUser user, string periodId, string reason);
}
