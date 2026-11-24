namespace Erp.Api.Interfaces;

// Opportunities + lines (ERP-SALES-015/016/017)
public interface IOpportunityRepository
{
    Opportunity? Find(string opportunityId);
    List<Opportunity> All();
    void Add(Opportunity opportunity);
    void AddLine(Opportunity opportunity, OpportunityLine line);
    void MarkWon(Opportunity opportunity, string orderId, string stage);
    void MarkLost(Opportunity opportunity, string lossReason, string? competitor, string stage);
    int Save();
}
