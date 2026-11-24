using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface IServiceCaseService
{
    object List();
    (object? data, string? code, string? message) Create(CurrentUser user, CreateCaseRequest request);
    (object? data, string? code, string? message) Assign(CurrentUser user, string caseId, string assigneeId);
    (object? data, string? code, string? message) Transition(CurrentUser user, string caseId, CaseStatus to, string? reason, string? resolutionCode);
    (object? data, string? code, string? message) Resolve(CurrentUser user, string caseId, string resolutionCode);
}
