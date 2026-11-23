namespace Erp.Api.Interfaces;

public interface IJournalService
{
    object List();
    (Journal? reversal, string? code, string? message) Reverse(CurrentUser user, string journalId);
}
