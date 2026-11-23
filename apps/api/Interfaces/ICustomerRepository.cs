namespace Erp.Api.Interfaces;

public interface ICustomerRepository
{
    List<Customer> All();
    bool Exists(string customerId);
    void Add(Customer customer);
    int Save();
}
