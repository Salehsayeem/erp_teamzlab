namespace Erp.Api.Interfaces;

public interface IFiscalPeriodRepository
{
    FiscalPeriod? Find(string periodId);
    List<FiscalPeriod> All();
    List<FiscalPeriod> OpenCovering(DateOnly date);
    bool NameExists(string name);
    bool Overlaps(DateOnly startDate, DateOnly endDate);
    void Add(FiscalPeriod period);
    void MarkClosed(FiscalPeriod period, string closedBy);
    void MarkReopened(FiscalPeriod period, string reason);
    List<Journal> PendingInRange(DateOnly start, DateOnly end);
    int Save();
}
