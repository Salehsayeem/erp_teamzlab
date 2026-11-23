using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface ISupplierService
{
    object List();
    (object data, string? code, string? message) Create(CurrentUser user, CreateSupplierRequest request);
}
