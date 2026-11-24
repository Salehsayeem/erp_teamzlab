namespace Erp.Api.Interfaces;

public interface IJournalService
{
    object List();
    // task-5: manual draft journals (011 source idempotency lives in the controller's Idem.Wrap)
    (Journal? journal, string? code, string? message) Create(CurrentUser user, Dtos.CreateJournalRequest request);
    // 013 workflow + 009 balance + 002 posting eligibility + 005 period lock + 035 tax computation
    (Journal? journal, string? code, string? message) Approve(CurrentUser approver, string journalId, string? reason);
    (Journal? reversal, string? code, string? message) Reverse(CurrentUser user, string journalId);
}
