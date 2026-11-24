namespace Erp.Api.Interfaces;

public interface ISalesOrderService
{
    object List();
    (SalesOrder? order, string? code, string? message) Create(CurrentUser user, string customerId, IReadOnlyList<SoLine> lines);
    (SalesOrder? order, string? code, string? message) Submit(CurrentUser user, string id);
    (SalesOrder? order, string? code, string? message) Approve(CurrentUser approver, string id, string? reason);
    (Shipment? shipment, Journal? journal, string? code, string? message) Ship(CurrentUser user, string id, string itemSku, decimal qty);
    // R-T7f: the journal-only Invoice shortcut was RETIRED — invoice documents live in IInvoiceService.
    // R-T7i: approval is gated on so:approve; over-limit approvals land CreditHold (057 AC2).
    (SalesOrder? order, string? code, string? message) ReleaseCreditHold(CurrentUser user, string id);
}
