namespace Erp.Api.Interfaces;

public interface ITaxCodeService
{
    object List();
    (TaxCode? taxCode, string? code, string? message) Create(CurrentUser user, Dtos.CreateTaxCodeRequest request, string jurisdiction);
}
