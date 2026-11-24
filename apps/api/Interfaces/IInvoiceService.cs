namespace Erp.Api.Interfaces;

public interface IInvoiceService
{
    object List(InvoiceStatus? status, string? customerId);
    (Invoice? invoice, string? code, string? message) DraftFromShipments(CurrentUser user, string soId, DateOnly? invoiceDate);
    (object? data, string? code, string? message) Post(CurrentUser approver, string invoiceId);
    (AdjustmentDocument? adjustment, string? code, string? message) CreateAdjustment(CurrentUser user, Dtos.CreateAdjustmentRequest request);
    (AdjustmentDocument? adjustment, string? code, string? message) ApproveAdjustment(CurrentUser approver, string adjustmentId, string? reason);
}
