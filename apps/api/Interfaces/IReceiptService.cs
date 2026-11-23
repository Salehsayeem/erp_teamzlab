namespace Erp.Api.Interfaces;

public interface IReceiptService
{
    (Receipt? receipt, Journal? journal, string? code, string? message) Receive(CurrentUser user, string purchaseOrderId, string itemSku, decimal qty);
}
