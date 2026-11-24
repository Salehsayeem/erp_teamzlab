using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

public interface ISupplierService
{
    object List();
    // eligibility-gate read for the supplier screen (023/044)
    (object? data, string? code, string? message) Gates(string supplierId);
    (object data, string? code, string? message) Create(CurrentUser user, CreateSupplierRequest request);
    // task-6 (ERP-PROCUREMENT-047/023): lifecycle status + effective-dated eligibility gates
    (object? data, string? code, string? message) SetStatus(CurrentUser user, string supplierId, SetSupplierStatusRequest request);
    (object? data, string? code, string? message) AddGate(CurrentUser user, string supplierId, AddSupplierGateRequest request);
}
