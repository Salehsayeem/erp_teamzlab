using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerService customers, ICollectionService collections) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(customers.List());

    [HttpPost]
    public IResult Create(CreateCustomerRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        // review 7b: the extension fields are guarded when supplied (absent stays defaulted)
        var err = Guards.Check(request.Name, "Name") ?? Guards.Check(request.TaxId, "TaxId") ?? Guards.CheckEmail(request.Email)
            ?? (request.CreditLimit is < 0 ? "CreditLimit must be >= 0." : null)
            ?? (request.PaymentTermNetDays is < 1 ? "PaymentTermNetDays must be >= 1." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("customers:write")) return (Envelope.FailBody("FORBIDDEN", "customers:write permission required."), 403);
        var (data, code, message) = customers.Create(u, request);
        return code is null ? (Envelope.OkBody(data), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 057: credit exposure read — credit:read gate (R-FIN seed), explicit no-data flag (057 edge)
    [HttpGet("{id}/credit-exposure")]
    public IResult CreditExposure(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("credit:read")) return Envelope.Fail("FORBIDDEN", "credit:read permission required.", 403);
        var (data, code, message) = collections.CreditExposure(id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
