using Erp.Api;

namespace Erp.Api.Interfaces;

public interface ICustomer360Repository
{
    void SetParent(Customer customer, string? parentId);
    void SetMergedInto(Customer customer, string survivorId);
    List<string> ChildrenIds(string customerId);
    List<string> UnmetControlIds(string customerId);
    List<CustomerOnboardingCheck> ChecksFor(string customerId);
    CustomerOnboardingCheck? FindCheck(string customerId, string checkCode);
    void AddCheck(CustomerOnboardingCheck check);
    void SetCheckMet(CustomerOnboardingCheck check, string metBy);
    PartyMerge? FindMerge(string mergeId);
    void AddMerge(PartyMerge merge);
    void SetMergeExecuted(PartyMerge merge, string executedBy);
    bool HasPostedInvoices(string customerId);
    int OpenOrderCount(string customerId);
    int OpenOpportunityCount(string customerId);
    int OpenCaseCount(string customerId);
    void ReassignToSurvivor(string mergedId, string survivorId);
    int Save();
}
