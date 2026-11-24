using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class LeadRepository(ErpDbContext db, EntityContext ec) : ILeadRepository
{
    private IQueryable<Lead> Scoped() =>
        db.Leads.Where(l => l.TenantId == ec.TenantId && l.LegalEntityId == ec.EntityId);

    public Lead? Find(string leadId) => Scoped().FirstOrDefault(l => l.Id == leadId);

    public List<Lead> All(LeadStatus? status) =>
        Scoped().Where(l => status == null || l.Status == status).OrderBy(l => l.Id).ToList();

    public void Add(Lead lead) => db.Leads.Add(lead);

    // tracked-entity status mutation — same pattern as the SO repository flips
    public void SetStatus(Lead lead, LeadStatus status, string? reasonCode)
    {
        db.Entry(lead).Property(nameof(Lead.Status)).CurrentValue = status;
        db.Entry(lead).Property(nameof(Lead.ReasonCode)).CurrentValue = reasonCode;
    }

    public int Save() => db.SaveChanges();
}
