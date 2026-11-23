using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("auth/token-exchange")]
    public IResult TokenExchange(TokenExchangeRequest request)
    {
        var err = Guards.CheckEmail(request.Email);
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err);
        var (data, code, message) = auth.Exchange(request.Email);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpGet("me")]
    public IResult Me() => Envelope.Ok<object>(auth.Me(CurrentUser.From(HttpContext)));

    [HttpGet("entities")]
    public IResult Entities() => Envelope.Ok<object>(auth.ListEntities());
}
