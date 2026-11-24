namespace Erp.Api.Interfaces;

public interface ICrmService
{
    object Interactions(InteractionPartyType? partyType, string? partyId);
    (Interaction? interaction, string? code, string? message) LogInteraction(CurrentUser user, Dtos.LogInteractionRequest request);
    object Cases(bool overdueOnly);
    (CrmCase? crmCase, string? code, string? message) CreateCase(CurrentUser user, Dtos.CreateCrmCaseRequest request);
    (CrmCase? crmCase, string? code, string? message) TransitionCase(CurrentUser user, string caseId, CrmCaseStatus to, string? resolution);
}
