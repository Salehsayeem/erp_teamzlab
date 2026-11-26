using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Customer 360 aggregates (012/013/014): hierarchy writes, onboarding checks, party merges,
// and the cross-aggregate reassignments a merge performs. Tenant+entity scoped throughout.
public sealed class Customer360Repository(ErpDbContext db, EntityContext ec) : ICustomer360Repository
{
    private IQueryable<T> Scoped<T>() where T : class =>
        db.Set<T>().Where(e => EF.Property<string>(e, "TenantId") == ec.TenantId
            && EF.Property<string>(e, "LegalEntityId") == ec.EntityId);

    public void SetParent(Customer customer, string? parentId) =>
        db.Entry(customer).Property(nameof(Customer.ParentCustomerId)).CurrentValue = parentId;

    public void SetMergedInto(Customer customer, string survivorId) =>
        db.Entry(customer).Property(nameof(Customer.MergedIntoId)).CurrentValue = survivorId;

    public List<string> ChildrenIds(string customerId) =>
        Scoped<Customer>().Where(c => c.ParentCustomerId == customerId).Select(c => c.Id).ToList();

    // 013 AC2: mandatory onboarding controls not yet met for the account
    public List<string> UnmetControlIds(string customerId) =>
        Scoped<CustomerOnboardingCheck>()
            .Where(ch => ch.CustomerId == customerId && ch.Mandatory && ch.Status != OnboardingCheckStatus.Met)
            .Select(ch => ch.CheckCode).ToList();

    public List<CustomerOnboardingCheck> ChecksFor(string customerId) =>
        Scoped<CustomerOnboardingCheck>().Where(ch => ch.CustomerId == customerId)
            .OrderBy(ch => ch.CheckCode).ToList();

    public CustomerOnboardingCheck? FindCheck(string customerId, string checkCode) =>
        Scoped<CustomerOnboardingCheck>().FirstOrDefault(ch => ch.CustomerId == customerId && ch.CheckCode == checkCode);

    public void AddCheck(CustomerOnboardingCheck check) => db.CustomerOnboardingChecks.Add(check);

    public void SetCheckMet(CustomerOnboardingCheck check, string metBy)
    {
        db.Entry(check).Property(nameof(CustomerOnboardingCheck.Status)).CurrentValue = OnboardingCheckStatus.Met;
        db.Entry(check).Property(nameof(CustomerOnboardingCheck.MetAt)).CurrentValue = DateTimeOffset.UtcNow;
        db.Entry(check).Property(nameof(CustomerOnboardingCheck.MetBy)).CurrentValue = metBy;
    }

    public PartyMerge? FindMerge(string mergeId) =>
        Scoped<PartyMerge>().FirstOrDefault(m => m.Id == mergeId);

    public void AddMerge(PartyMerge merge) => db.PartyMerges.Add(merge);

    public void SetMergeExecuted(PartyMerge merge, string executedBy)
    {
        db.Entry(merge).Property(nameof(PartyMerge.Status)).CurrentValue = PartyMergeStatus.Executed;
        db.Entry(merge).Property(nameof(PartyMerge.ExecutedBy)).CurrentValue = executedBy;
        db.Entry(merge).Property(nameof(PartyMerge.ExecutedAt)).CurrentValue = DateTimeOffset.UtcNow;
    }

    // 014 edge: a posted balance on the merged account blocks the merge outright
    public bool HasPostedInvoices(string customerId) =>
        Scoped<Invoice>().Any(i => i.CustomerId == customerId && i.Status == InvoiceStatus.Posted);

    public int OpenOrderCount(string customerId) =>
        Scoped<SalesOrder>().Count(o => o.CustomerId == customerId
            && o.Status != SoStatus.Rejected && o.Status != SoStatus.Invoiced && o.Status != SoStatus.Cancelled);

    public int OpenOpportunityCount(string customerId) =>
        Scoped<Opportunity>().Count(o => o.CustomerId == customerId && o.Status == OpportunityStatus.Open);

    public int OpenCaseCount(string customerId) =>
        Scoped<CrmCase>().Count(c => c.CustomerId == customerId && c.Status != CrmCaseStatus.Closed);

    // the merge itself: open orders/opportunities/cases/checks move to the survivor; posted
    // invoices stay on the merged account as historic snapshots (blocked earlier anyway)
    public void ReassignToSurvivor(string mergedId, string survivorId)
    {
        foreach (var so in Scoped<SalesOrder>().Where(o => o.CustomerId == mergedId).ToList())
            db.Entry(so).Property(nameof(SalesOrder.CustomerId)).CurrentValue = survivorId;
        foreach (var opp in Scoped<Opportunity>().Where(o => o.CustomerId == mergedId).ToList())
            db.Entry(opp).Property(nameof(Opportunity.CustomerId)).CurrentValue = survivorId;
        foreach (var check in Scoped<CustomerOnboardingCheck>().Where(c => c.CustomerId == mergedId).ToList())
            db.Entry(check).Property(nameof(CustomerOnboardingCheck.CustomerId)).CurrentValue = survivorId;
        foreach (var caseRow in Scoped<CrmCase>().Where(c => c.CustomerId == mergedId).ToList())
            db.Entry(caseRow).Property(nameof(CrmCase.CustomerId)).CurrentValue = survivorId;
    }

    public int Save() => db.SaveChanges();
}
