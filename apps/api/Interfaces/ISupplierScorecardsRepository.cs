using Erp.Api;

namespace Erp.Api.Interfaces;

public interface ISupplierScorecardsRepository
{
    SupplierScorecard? Find(string id);
    List<SupplierScorecard> ForSupplier(string supplierId);
    void Add(SupplierScorecard scorecard);
    void AddAction(CorrectiveAction action);
    CorrectiveAction? FindAction(string id);
    void SetField(object entity, string name, object value);
    int Save();
}
