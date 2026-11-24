using Erp.Api.Interfaces;

namespace Erp.Api.Interfaces;

// BOM/recipe revisions (ERP-MFG-014/015): immutable approved documents with effective intervals,
// exactly-one-effective selection, maker-checked approval (approver ≠ author).
public interface IBomService
{
    object List(string? outputSku, BomStatus? status);
    (object? data, string? code, string? message) Create(CurrentUser user, Dtos.CreateBomRequest request);
    (object? data, string? code, string? message) AddLine(CurrentUser user, string bomId, Dtos.AddBomLineRequest request);
    (object? data, string? code, string? message) Approve(CurrentUser approver, string bomId, string? reason);
    (object? data, string? code, string? message) Retire(CurrentUser user, string bomId, string? reason);
}
