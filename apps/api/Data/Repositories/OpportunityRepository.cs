using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

public sealed class OpportunityRepository(ErpDbContext db, EntityContext ec) : IOpportunityRepository
{
    private IQueryable<Opportunity> Scoped() =>
        db.Opportunities.Where(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId);

    private List<Opportunity> Ordered(IQueryable<Opportunity> query) =>
        query.Include(o => o.Lines).OrderBy(o => o.Id).ToList(); // opportunity_lines is an owned table

    public Opportunity? Find(string opportunityId) =>
        Ordered(Scoped().Where(o => o.Id == opportunityId)).FirstOrDefault();

    public List<Opportunity> All() => Ordered(Scoped());

    public void Add(Opportunity opportunity)
    {
        db.Opportunities.Add(opportunity);
        foreach (var line in opportunity.Lines)
        {
            db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }

    public void AddLine(Opportunity opportunity, OpportunityLine line)
    {
        db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
        db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
        opportunity.Lines.Add(line);
    }

    public void MarkWon(Opportunity opportunity, string orderId, string stage)
    {
        db.Entry(opportunity).Property(nameof(Opportunity.Status)).CurrentValue = OpportunityStatus.Won;
        db.Entry(opportunity).Property(nameof(Opportunity.OrderId)).CurrentValue = orderId;
        db.Entry(opportunity).Property(nameof(Opportunity.Stage)).CurrentValue = stage;
    }

    public void MarkLost(Opportunity opportunity, string lossReason, string? competitor, string stage)
    {
        db.Entry(opportunity).Property(nameof(Opportunity.Status)).CurrentValue = OpportunityStatus.Lost;
        db.Entry(opportunity).Property(nameof(Opportunity.LossReason)).CurrentValue = lossReason;
        db.Entry(opportunity).Property(nameof(Opportunity.Competitor)).CurrentValue = competitor;
        db.Entry(opportunity).Property(nameof(Opportunity.Stage)).CurrentValue = stage;
    }

    public int Save() => db.SaveChanges();
}
