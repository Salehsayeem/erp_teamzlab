using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface IAssetService
{
    object List(AssetStatus? status);
    (object? data, string? code, string? message) Create(CurrentUser user, CreateAssetRequest request);
    (object? data, string? code, string? message) ProposeCapitalization(CurrentUser user, string assetId, string? sourceProjectId, decimal amount);
    (object? data, string? code, string? message) ApproveCapitalization(CurrentUser approver, string assetId, string eventId);
    (object? data, string? code, string? message) RunDepreciation(CurrentUser user, string? idempotencyKey, string periodId);
    (object? data, string? code, string? message) ProposeTransfer(CurrentUser user, string assetId, string toLocationId, DateTimeOffset effectiveAt);
    (object? data, string? code, string? message) ProposeImpairment(CurrentUser user, string assetId, decimal amount, string reason);
    (object? data, string? code, string? message) ApproveImpairment(CurrentUser approver, string assetId, string eventId);
    (object? data, string? code, string? message) ProposeDisposal(CurrentUser user, string assetId, decimal proceeds, string reason);
    (object? data, string? code, string? message) ApproveDisposal(CurrentUser approver, string assetId, string eventId);
}
