using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// CRM interactions + cases — R-T7d: deliberately NOT the ASSETS service_cases table
public sealed class CrmRepository(ErpDbContext db, EntityContext ec) : ICrmRepository
{
    private IQueryable<Interaction> InteractionScope() =>
        db.Interactions.Where(i => i.TenantId == ec.TenantId && i.LegalEntityId == ec.EntityId);

    private IQueryable<CrmCase> CaseScope() =>
        db.CrmCases.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    public void AddInteraction(Interaction interaction) => db.Interactions.Add(interaction);

    public List<Interaction> AllInteractions() =>
        InteractionScope().OrderBy(i => i.OccurredAt).ThenBy(i => i.Id).ToList();

    // 007 AC1: the timeline is ordered by OccurredAt (when it happened), never by receipt order
    public List<Interaction> InteractionsFor(InteractionPartyType partyType, string partyId) =>
        InteractionScope().Where(i => i.PartyType == partyType && i.PartyId == partyId)
            .OrderBy(i => i.OccurredAt).ThenBy(i => i.Id).ToList();

    public CrmCase? FindCase(string caseId) => CaseScope().FirstOrDefault(c => c.Id == caseId);

    public List<CrmCase> AllCases() => CaseScope().OrderBy(c => c.Id).ToList();

    public void AddCase(CrmCase crmCase) => db.CrmCases.Add(crmCase);

    public void SetCaseStatus(CrmCase crmCase, CrmCaseStatus status, string? resolution)
    {
        db.Entry(crmCase).Property(nameof(CrmCase.Status)).CurrentValue = status;
        db.Entry(crmCase).Property(nameof(CrmCase.Resolution)).CurrentValue = resolution; // history preserved in audit_events
    }

    public int Save() => db.SaveChanges();
}
