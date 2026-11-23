using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/suppliers")]
public sealed class SuppliersController(ISupplierService suppliers) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(suppliers.List());

    [HttpPost]
    public IResult Create(CreateSupplierRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.Name, "Name") ?? Guards.Check(request.TaxId, "TaxId") ?? Guards.CheckEmail(request.Email);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (data, code, message) = suppliers.Create(u, request);
        return code is null ? (Envelope.OkBody(data), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
