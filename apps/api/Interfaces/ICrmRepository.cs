namespace Erp.Api.Interfaces;

// CRM interaction timeline + cases (ERP-SALES-007/008) — separate from ASSETS ServiceCases (R-T7d)
public interface ICrmRepository
{
    void AddInteraction(Interaction interaction);
    List<Interaction> AllInteractions();
    List<Interaction> InteractionsFor(InteractionPartyType partyType, string partyId);
    CrmCase? FindCase(string caseId);
    List<CrmCase> AllCases();
    void AddCase(CrmCase crmCase);
    void SetCaseStatus(CrmCase crmCase, CrmCaseStatus status, string? resolution);
    int Save();
}
