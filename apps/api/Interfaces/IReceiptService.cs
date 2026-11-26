namespace Erp.Api.Interfaces;

public interface IReceiptService
{
    // deferred PROCUREMENT wave: tenant-scoped receipt list for the receipt workspace
    object List();
    (Receipt? receipt, Journal? journal, string? code, string? message) Receive(CurrentUser user, string purchaseOrderId, string itemSku, decimal qty, string? toleranceOverrideReason = null);
}
