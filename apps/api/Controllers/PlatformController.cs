using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class PlatformController(IPlatformService platform) : ControllerBase
{
    [HttpGet("audit-events")]
    public IResult AuditEvents()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("*")
            ? Envelope.Ok<object>(platform.AuditEvents())
            : Envelope.Fail("FORBIDDEN", "admin permission required.", 403);
    }

    [HttpGet("outbox")]
    public IResult Outbox() // dev visibility only
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("*")
            ? Envelope.Ok<object>(platform.Outbox())
            : Envelope.Fail("FORBIDDEN", "admin permission required.", 403);
    }
}
