namespace Erp.Api.Interfaces;

public interface ICollectionService
{
    object Worklist();
    (CollectionReminder? reminder, string? code, string? message) LogReminder(CurrentUser user, Dtos.LogReminderRequest request);
    (Dispute? dispute, string? code, string? message) CreateDispute(CurrentUser user, Dtos.CreateDisputeRequest request);
    (Dispute? dispute, string? code, string? message) ResolveDispute(CurrentUser user, string disputeId, string outcome);
    (object? data, string? code, string? message) CreditExposure(string customerId);
}
