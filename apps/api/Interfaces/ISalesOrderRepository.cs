namespace Erp.Api.Interfaces;

// Sales orders + shipments
public interface ISalesOrderRepository
{
    SalesOrder? Find(string soId);
    List<SalesOrder> All();
    List<SalesOrder> OpenForCustomer(string customerId); // Confirmed or CreditHold (057 exposure basis)
    void Add(SalesOrder so);
    void MarkSubmitted(SalesOrder so);
    void MarkConfirmed(SalesOrder so, string approverId);
    void MarkRejected(SalesOrder so, string approverId, string reason);
    void MarkShipped(SalesOrder so);
    void MarkInvoiced(SalesOrder so);
    void MarkCreditHold(SalesOrder so, string approverId);
    void AddShipment(Shipment shipment);
    List<Shipment> ShipmentsFor(string soId);
    Dictionary<string, decimal> ShippedBySku(string soId);
    int Save();
}
