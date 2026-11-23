using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface IInventoryService
{
    object ListStock();
    object ListWarehouses();
    object ListLocations();
    (Warehouse? warehouse, string? code, string? message) CreateWarehouse(CurrentUser user, string code, string name);
    (StockLocation? location, string? code, string? message) CreateLocation(CurrentUser user, string warehouseId, string code, string name);
    (StockTransfer? transfer, string? code, string? message) Transfer(CurrentUser user, string fromLocationId, string toLocationId, string itemSku, decimal qty);
    (StockCount? count, string? code, string? message) OpenCount(CurrentUser user, string locationId, IReadOnlyList<CountItemRequest> items);
    (StockCount? count, IReadOnlyList<StockAdjustment>? adjustments, string? code, string? message) Close(CurrentUser closer, string countId);
    object Valuation();
}
