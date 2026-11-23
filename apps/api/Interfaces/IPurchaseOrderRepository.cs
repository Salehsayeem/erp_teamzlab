namespace Erp.Api.Interfaces;

// Purchase orders + receipts + weighted-average costing (receipt PO unit prices)
public interface IPurchaseOrderRepository
{
    PurchaseOrder? Find(string poId);
    List<PurchaseOrder> All();
    void Add(PurchaseOrder po);
    void MarkSubmitted(PurchaseOrder po);
    void MarkApproved(PurchaseOrder po, string approverId);
    void MarkRejected(PurchaseOrder po, string approverId, string reason);
    void MarkReceived(PurchaseOrder po);
    void AddReceipt(Receipt receipt);
    decimal ReceivedQty(string poId, string itemSku);
    Dictionary<string, decimal> ReceivedBySku(string poId);
    Receipt? FindReceiptReferencedBy(string tenantId, string legalEntityId, string memo);
    decimal AverageUnitCost(string itemSku);
    int Save();
}
