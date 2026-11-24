namespace Erp.Api.Interfaces;

public interface ITaxCodeRepository
{
    TaxCode? Find(string taxCodeId);
    List<TaxCode> All();
    List<TaxCode> ByCode(string code);
    // 032 AC1: the revision effective at the accounting date (latest published version in range)
    TaxCode? EffectiveFor(string code, DateOnly date);
    bool Overlaps(string code, DateOnly from, DateOnly? to);
    int NextVersion(string code);
    void Add(TaxCode taxCode);
    int Save();
}
