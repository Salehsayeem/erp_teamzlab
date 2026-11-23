namespace Erp.Api.Dtos;

public record CreateWarehouseRequest(string Code, string Name);
public record CreateLocationRequest(string WarehouseId, string Code, string Name);
public record CreateTransferRequest(string FromLocationId, string ToLocationId, string ItemSku, decimal Qty);
public record CountItemRequest(string ItemSku, decimal CountedQty);
public record CreateCountRequest(string LocationId, IReadOnlyList<CountItemRequest> Items);
