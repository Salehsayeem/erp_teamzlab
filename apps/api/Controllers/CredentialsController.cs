using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM9 — training & credentials (058): credentials:write (R-HR) makes; the waiver is a second
// officer's act (maker-checker) naming reason; the assignment gate consumes the effective status.
[ApiController]
[Route("api/v1/credentials")]
public sealed class CredentialsController(ICredentialService credentials) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? employmentId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(credentials.List(u, employmentId));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateCredentialRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.EmploymentId, "EmploymentId") ?? Guards.Check(request.Code, "Code", 50)
            ?? Guards.Check(request.Name, "Name");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("credentials:write")) return (Envelope.FailBody("FORBIDDEN", "credentials:write permission required."), 403);
        var (credential, code, message) = credentials.Create(u, request);
        return code is null ? (Envelope.OkBody(credential!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/waivers")]
    public IResult Waive(string id, Dtos.WaiveCredentialRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (Envelope.FailBody("VALIDATION_ERROR", "A waiver requires a Reason (058 AC2)."), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("credentials:write")) return (Envelope.FailBody("FORBIDDEN", "credentials:write permission required."), 403);
        var (credential, code, message) = credentials.Waive(u, id, request);
        return code is null ? (Envelope.OkBody(credential!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
