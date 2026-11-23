namespace Erp.Api.Interfaces;

public interface ISalesOrderService
{
    object List();
    (SalesOrder? order, string? code, string? message) Create(CurrentUser user, string customerId, IReadOnlyList<SoLine> lines);
    (SalesOrder? order, string? code, string? message) Submit(CurrentUser user, string id);
    (SalesOrder? order, string? code, string? message) Approve(CurrentUser approver, string id, string? reason);
    (Shipment? shipment, Journal? journal, string? code, string? message) Ship(CurrentUser user, string id, string itemSku, decimal qty);
    (Journal? journal, string? code, string? message) Invoice(CurrentUser user, string id);
}
