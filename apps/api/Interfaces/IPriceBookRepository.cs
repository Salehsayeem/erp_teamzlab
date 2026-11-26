using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IPriceBookRepository
{
    List<PriceBook> Books();
    PriceBook? FindBook(string bookId);
    void AddBook(PriceBook book);
    List<CustomerContractPrice> ContractPricesFor(string customerId, string itemSku);
    void AddContractPrice(CustomerContractPrice price);
    int Save();
}
