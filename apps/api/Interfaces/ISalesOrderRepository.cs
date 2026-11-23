namespace Erp.Api.Interfaces;

// Sales orders + shipments
public interface ISalesOrderRepository
{
    SalesOrder? Find(string soId);
    List<SalesOrder> All();
    void Add(SalesOrder so);
    void MarkSubmitted(SalesOrder so);
    void MarkConfirmed(SalesOrder so, string approverId);
    void MarkRejected(SalesOrder so, string approverId, string reason);
    void MarkShipped(SalesOrder so);
    void MarkInvoiced(SalesOrder so);
    void AddShipment(Shipment shipment);
    Dictionary<string, decimal> ShippedBySku(string soId);
    int Save();
}
