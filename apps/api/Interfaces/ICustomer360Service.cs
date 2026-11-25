using Erp.Api;

namespace Erp.Api.Interfaces;

public interface ICustomer360Service
{
    (object? data, string? code, string? message) Detail(CurrentUser u, string customerId);
    (object? data, string? code, string? message) ChecksList(string customerId);
    (object? data, string? code, string? message) SetHierarchy(CurrentUser u, string customerId, string? parentId);
    (object? data, string? code, string? message) AddOnboardingCheck(CurrentUser u, string customerId, string checkCode, bool mandatory);
    (object? data, string? code, string? message) MarkCheckMet(CurrentUser u, string customerId, string checkCode);
    (object? data, string? code, string? message) MergePreview(CurrentUser u, string survivorId, string mergedId, string reason);
    (object? data, string? code, string? message) MergeExecute(CurrentUser u, string previewId);
}
