namespace Erp.Api.Interfaces;

public interface ISupplierRepository
{
    List<Supplier> All();
    bool Exists(string supplierId);
    void Add(Supplier supplier);
    int Save();
}
