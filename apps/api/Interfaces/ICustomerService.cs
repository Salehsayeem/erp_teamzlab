using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface ICustomerService
{
    object List();
    (object data, string? code, string? message) Create(CurrentUser user, CreateCustomerRequest request);
}
