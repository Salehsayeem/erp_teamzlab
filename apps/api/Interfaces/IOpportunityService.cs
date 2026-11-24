namespace Erp.Api.Interfaces;

public interface IOpportunityService
{
    object List(OpportunityStatus? status, string? ownerId);
    (Opportunity? opportunity, string? code, string? message) Create(CurrentUser user, Dtos.CreateOpportunityRequest request);
    (Opportunity? opportunity, string? code, string? message) AddLine(CurrentUser user, string opportunityId, Dtos.AddOpportunityLineRequest request);
    (Opportunity? opportunity, string? code, string? message) Transition(CurrentUser user, string opportunityId, Dtos.TransitionOpportunityRequest request);
}
