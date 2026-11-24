namespace Erp.Api.Interfaces;

public interface ICustomerRepository
{
    List<Customer> All();
    Customer? Find(string customerId);
    bool Exists(string customerId);
    Customer? FindByNormalizedTaxId(string taxId);
    void Add(Customer customer);
    int Save();
}
