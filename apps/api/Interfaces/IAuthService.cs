using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface IAuthService
{
    (object? data, string? code, string? message) Exchange(string email);
    object Me(CurrentUser user);
    object ListEntities();
}
