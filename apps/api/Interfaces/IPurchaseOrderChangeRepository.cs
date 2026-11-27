using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IPurchaseOrderChangeRepository
{
    PurchaseOrder? FindPo(string poId);
    PurchaseOrderChange? FindChange(string poId, string changeId);
    List<PurchaseOrderChange> ChangesFor(string poId);
    void AddChange(PurchaseOrderChange change);
    PurchaseOrderAcknowledgement? FindAck(string poId, string supplierId, int orderVersion);
    List<PurchaseOrderAcknowledgement> AcksFor(string poId);
    void AddAck(PurchaseOrderAcknowledgement ack);
    bool HasPendingChange(string poId);
    // records carry init-only properties — mutations go through the EF change tracker (house pattern)
    void SetField(object entity, string name, object value);
    int Save();
}
