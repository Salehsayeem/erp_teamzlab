using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/accounts")]
public sealed class AccountsController(IAccountService accounts) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("accounts:read")
            ? Envelope.Ok<object>(accounts.List())
            : Envelope.Fail("FORBIDDEN", "accounts:read permission required.", 403);
    }

    [HttpPost]
    public IResult Create(CreateAccountRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code", 50) ?? Guards.Check(request.Name, "Name");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("accounts:write")) return (Envelope.FailBody("FORBIDDEN", "accounts:write permission required."), 403);
        var (account, code, error) = accounts.Create(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(account!), 201);
    });
}
