using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Leads (ERP-SALES-003/004/005): capture with server receipt time, qualification lifecycle,
// idempotent conversion to customer + opportunity, duplicate detection on the normalized tax id.
public sealed class LeadService(
    ILeadRepository leads, ICustomerRepository customers, IOpportunityRepository opportunities,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec) : ILeadService
{
    // 004: forward flow + the Disqualified → Recycled → Working salvage path; Qualified and
    // Converted are terminal in the map (conversion moves Qualified → Converted)
    private static readonly IReadOnlyDictionary<LeadStatus, LeadStatus[]> Legal = new Dictionary<LeadStatus, LeadStatus[]>
    {
        [LeadStatus.New] = [LeadStatus.Working],
        [LeadStatus.Working] = [LeadStatus.Qualified, LeadStatus.Disqualified],
        [LeadStatus.Qualified] = [],
        [LeadStatus.Disqualified] = [LeadStatus.Recycled],
        [LeadStatus.Recycled] = [LeadStatus.Working],
        [LeadStatus.Converted] = [],
    };

    public object List(LeadStatus? status) =>
        leads.All(status).Select(l => new
        {
            l.Id, l.Company, l.ContactName, l.Email, l.Phone, l.Source, l.SourceRef, l.Status,
            l.ReasonCode, l.OwnerId, l.ReceivedAt, l.TaxId, l.CreatedBy, l.CreatedAt,
        }).ToList();

    public (Lead lead, string? code, string? message) Create(CurrentUser user, Dtos.CreateLeadRequest request)
    {
        // 003 AC1: the server records the receipt time — client-supplied timestamps are ignored
        var lead = new Lead(numbers.Next("LEAD"), ec.TenantId, ec.EntityId, request.Company, request.ContactName,
            request.Email, request.Phone, request.Source, request.SourceRef, LeadStatus.New, null, user.Id,
            DateTimeOffset.UtcNow, user.Id, DateTimeOffset.UtcNow, request.TaxId);
        leads.Add(lead);
        audit.Log(user, "create", "lead", lead.Id,
            $"Lead captured via {request.Source} for '{request.Company ?? request.ContactName ?? "anonymous inquiry"}'");
        leads.Save();
        return (lead, null, null);
    }

    public (Lead lead, string? code, string? message) Transition(CurrentUser user, string leadId, LeadStatus to, string? reasonCode)
    {
        var lead = leads.Find(leadId);
        if (lead is null) return (null, "LEAD_NOT_FOUND", "Lead not found.");
        var from = lead.Status;
        if (!Legal[from].Contains(to))
            return (null, "LEAD_TRANSITION_ERROR", $"Illegal transition {from} → {to}.");

        leads.SetStatus(lead, to, to == LeadStatus.Disqualified ? reasonCode : null);
        audit.Log(user, "transition", "lead", leadId,
            $"Lead {leadId}: {from} → {to}{(reasonCode is null ? "" : $" ({reasonCode})")}");
        leads.Save();
        return (lead, null, null);
    }

    // 004 AC2: one conversion creates the customer AND the opportunity, stamped with the lead;
    // 005: a normalized tax-id match against an existing customer refuses conversion (never
    // auto-merge). Idempotency-Key replay (controller) returns the same records on retry.
    public (object? data, string? code, string? message) Convert(CurrentUser user, string leadId)
    {
        var lead = leads.Find(leadId);
        if (lead is null) return (null, "LEAD_NOT_FOUND", "Lead not found.");
        if (lead.Status != LeadStatus.Qualified)
            return (null, "LEAD_CONVERSION_ERROR", $"Only qualified leads can be converted (current: {lead.Status}).");

        if (lead.TaxId is not null && customers.FindByNormalizedTaxId(lead.TaxId) is { } candidate)
            return (null, "CUSTOMER_DUPLICATE",
                $"A customer with this tax id already exists ({candidate.Id}, '{candidate.Name}'); merge manually — conversion never auto-merges.");

        var name = lead.Company ?? lead.ContactName ?? $"Lead {lead.Id}";
        var customer = new Customer(numbers.Next("CUS"), ec.TenantId, ec.EntityId, name, lead.TaxId ?? "",
            lead.Email ?? "", DateTimeOffset.UtcNow, user.Id);
        customers.Add(customer);

        var opportunity = new Opportunity(numbers.Next("OPP"), ec.TenantId, ec.EntityId, customer.Id, lead.Id,
            name, 0, EntityContext.SeedEntity.Currency, OpportunityStatus.Open, "Qualified",
            null, lead.OwnerId, null, null, null, user.Id, DateTimeOffset.UtcNow);
        opportunities.Add(opportunity);

        leads.SetStatus(lead, LeadStatus.Converted, null);
        audit.Log(user, "convert", "lead", leadId,
            $"Lead converted: customer {customer.Id} + opportunity {opportunity.Id} created");
        audit.Emit(user, "sales.lead.converted", new { leadId, customerId = customer.Id, opportunityId = opportunity.Id });
        leads.Save(); // customer + opportunity + lead flip commit together
        return (new { customer, opportunity }, null, null);
    }
}
