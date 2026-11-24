using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Opportunities (ERP-SALES-015/016/017): fixed stage map Open → Won/Lost (R-T7i trim), line sums,
// closed-won requires an order link, lost requires a reason. Quotes are deferred (R-T7a) — the
// bridge to revenue is opportunity → sales order directly.
public sealed class OpportunityService(
    IOpportunityRepository opportunities, ISalesOrderRepository orders, IIdentityRepository identities,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IOpportunityService
{
    public object List(OpportunityStatus? status, string? ownerId) =>
        opportunities.All()
            .Where(o => status == null || o.Status == status)
            .Where(o => ownerId == null || o.OwnerId == ownerId)
            .Select(Proj).ToList();

    public (Opportunity opportunity, string? code, string? message) Create(CurrentUser user, Dtos.CreateOpportunityRequest request)
    {
        if (identities.FindActiveUserById(request.OwnerId) is null)
            return (null, "VALIDATION_ERROR", "Owner must be an active user.");
        var opportunity = new Opportunity(numbers.Next("OPP"), ec.TenantId, ec.EntityId, request.CustomerId,
            request.LeadId, request.Title, 0, request.Currency, OpportunityStatus.Open, "Prospecting",
            request.ExpectedCloseDate, request.OwnerId, null, null, null, user.Id, DateTimeOffset.UtcNow);
        opportunities.Add(opportunity);
        audit.Log(user, "create", "opportunity", opportunity.Id,
            $"Opportunity '{request.Title}' opened ({request.Currency}), owner {request.OwnerId}");
        opportunities.Save();
        return (opportunity, null, null);
    }

    // 017 AC1: the total is the sum of the lines; 015 AC2 analog — adding a line moves the
    // opportunity value, so the audit summary records the previous → new amount
    public (Opportunity opportunity, string? code, string? message) AddLine(CurrentUser user, string opportunityId, Dtos.AddOpportunityLineRequest request)
    {
        var opportunity = opportunities.Find(opportunityId);
        if (opportunity is null) return (null, "OPPORTUNITY_NOT_FOUND", "Opportunity not found.");
        if (opportunity.Status != OpportunityStatus.Open)
            return (null, "OPPORTUNITY_STATE_ERROR", $"Lines can only be added while the opportunity is open (current: {opportunity.Status}).");

        var before = Total(opportunity);
        opportunities.AddLine(opportunity, new OpportunityLine(request.ItemSku, request.Description, request.Qty, request.TargetPrice));
        var after = Total(opportunity);
        audit.Log(user, "add_line", "opportunity", opportunityId,
            $"Line {request.Qty:0.##} x {request.ItemSku} added; amount {before:0.00} → {after:0.00} {opportunity.Currency}");
        opportunities.Save();
        return (opportunity, null, null);
    }

    public (Opportunity opportunity, string? code, string? message) Transition(CurrentUser user, string opportunityId, Dtos.TransitionOpportunityRequest request)
    {
        var opportunity = opportunities.Find(opportunityId);
        if (opportunity is null) return (null, "OPPORTUNITY_NOT_FOUND", "Opportunity not found.");
        if (opportunity.Status != OpportunityStatus.Open)
            return (null, "OPPORTUNITY_TRANSITION_ERROR", $"A {opportunity.Status} opportunity is terminal (no transitions out).");

        switch (request.Status)
        {
            case OpportunityStatus.Won:
                return Win(user, opportunity, request);
            case OpportunityStatus.Lost:
                opportunities.MarkLost(opportunity, request.LossReason!, request.Competitor, StageOrDefault(request));
                audit.Log(user, "lose", "opportunity", opportunityId, $"Opportunity lost: {request.LossReason}");
                opportunities.Save();
                return (opportunity, null, null);
            default:
                return (null, "OPPORTUNITY_TRANSITION_ERROR", $"Illegal transition {opportunity.Status} → {request.Status}.");
        }
    }

    // 016 AC2: closed-won must link a Confirmed-or-later sales order
    private (Opportunity, string?, string?) Win(CurrentUser user, Opportunity opportunity, Dtos.TransitionOpportunityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrderId))
            return (null, "OPPORTUNITY_WON_REQUIRES_ORDER", "Closing an opportunity as won requires an OrderId linking a sales order.");
        var so = orders.Find(request.OrderId);
        if (so is null || so.Status is not (SoStatus.Confirmed or SoStatus.CreditHold or SoStatus.Shipped or SoStatus.Invoiced))
            return (null, "OPPORTUNITY_WON_REQUIRES_ORDER",
                $"OrderId must link a confirmed-or-later sales order ({request.OrderId} is {(so is null ? "unknown" : so.Status.ToString())}).");

        opportunities.MarkWon(opportunity, request.OrderId, StageOrDefault(request));
        audit.Log(user, "win", "opportunity", opportunity.Id, $"Opportunity won via {request.OrderId}");
        opportunities.Save();
        return (opportunity, null, null);
    }

    private static string StageOrDefault(Dtos.TransitionOpportunityRequest request) =>
        string.IsNullOrWhiteSpace(request.Stage) ? "Closed" : request.Stage;

    private static decimal Total(Opportunity o) =>
        o.Lines.Sum(l => decimal.Round(l.Qty * l.TargetPrice, 2, MidpointRounding.AwayFromZero));

    private static object Proj(Opportunity o) => new
    {
        o.Id, o.CustomerId, o.LeadId, o.Title, o.Amount, o.Currency, o.Status, o.Stage,
        o.ExpectedCloseDate, o.OwnerId, o.LossReason, o.Competitor, o.OrderId, o.CreatedBy, o.CreatedAt,
        total = Total(o), // 017 AC1: total = Σ Round(Qty × TargetPrice, 2)
        lines = o.Lines,
    };
}
