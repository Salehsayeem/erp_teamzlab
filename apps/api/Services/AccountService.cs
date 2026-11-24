using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Chart-of-accounts basics (FINANCE-001/002): versioned accounts with type, normal balance and
// direct-posting eligibility. Journal approval enforces the flags at commit time (002 AC1).
public sealed class AccountService(IAccountRepository accounts, IAuditRepository audit, EntityContext ec) : IAccountService
{
    public object List() => accounts.All();

    public (Account? account, string? code, string? message) Create(CurrentUser user, Dtos.CreateAccountRequest request)
    {
        if (accounts.CodeExists(request.Code))
            return (null, "ACCOUNT_DUPLICATE_CODE", $"Account code {request.Code} already exists for this legal entity.");
        var account = new Account($"ACC-{request.Code}", ec.TenantId, ec.EntityId, request.Code, request.Name,
            request.Type, request.NormalBalance, request.DirectPostingAllowed, 1, user.Id, DateTimeOffset.UtcNow);
        accounts.Add(account);
        audit.Log(user, "create", "account", account.Id,
            $"Account {account.Code} {account.Name} created ({account.Type}, {account.NormalBalance}, direct posting {(account.DirectPostingAllowed ? "allowed" : "denied")})");
        audit.Emit(user, "finance.account.created", new { accountId = account.Id, code = account.Code, type = account.Type.ToString() });
        accounts.Save();
        return (account, null, null);
    }
}
