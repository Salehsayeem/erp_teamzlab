namespace Erp.Api.Interfaces;

public interface IAccountService
{
    object List();
    (Account? account, string? code, string? message) Create(CurrentUser user, Dtos.CreateAccountRequest request);
}
