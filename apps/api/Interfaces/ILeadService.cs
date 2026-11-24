namespace Erp.Api.Interfaces;

public interface ILeadService
{
    object List(LeadStatus? status);
    (Lead lead, string? code, string? message) Create(CurrentUser user, Dtos.CreateLeadRequest request);
    (Lead lead, string? code, string? message) Transition(CurrentUser user, string leadId, LeadStatus to, string? reasonCode);
    (object? data, string? code, string? message) Convert(CurrentUser user, string leadId);
}
