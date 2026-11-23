using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/journals")]
public sealed class JournalsController(IJournalService journals) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("journals:read")
            ? Envelope.Ok<object>(journals.List())
            : Envelope.Fail("FORBIDDEN", "journals:read permission required.", 403);
    }

    [HttpPost("{id}/reversals")]
    public IResult Reverse(string id)
    {
        var (rev, code, error) = journals.Reverse(CurrentUser.From(HttpContext), id);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(rev!, 201);
    }
}
