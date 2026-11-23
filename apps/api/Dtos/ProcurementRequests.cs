namespace Erp.Api.Dtos;

using Erp.Api;

public record CreateSupplierRequest(string Name, string TaxId, string Email);
public record CreatePoRequest(string SupplierId, IReadOnlyList<PoLine> Lines);
public record ApprovePoRequest(string? Reason);
public record CreateReceiptRequest(string PurchaseOrderId, string ItemSku, decimal Qty);
