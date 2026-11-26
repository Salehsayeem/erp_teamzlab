using Erp.Api;

namespace Erp.Api.Interfaces;

public interface ISalesReturnService
{
    (object? data, string? code, string? message) Create(CurrentUser u, string orderId, IReadOnlyList<Dtos.ReturnLineInput> lines, string? overrideJustification);
    (object? data, string? code, string? message) Receive(CurrentUser u, string returnId, decimal receivedQty);
    (object? data, string? code, string? message) Detail(string returnId);
    (object? data, string? code, string? message) List();
}
