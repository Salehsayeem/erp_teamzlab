namespace Erp.Api.Interfaces;

// Leads (ERP-SALES-003/004/005) — tenant+entity scoped lookups only
public interface ILeadRepository
{
    Lead? Find(string leadId);
    List<Lead> All(LeadStatus? status);
    void Add(Lead lead);
    void SetStatus(Lead lead, LeadStatus status, string? reasonCode);
    int Save();
}
