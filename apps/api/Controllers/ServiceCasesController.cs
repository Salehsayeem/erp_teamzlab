using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Service cases: intake (channel idempotency via Idempotency-Key), queue view with breach flag,
// assignment, transitions and resolution (ERP-ASSETS-023/024/026/037)
[ApiController]
[Route("api/v1/service-cases")]
public sealed class ServiceCasesController(IServiceCaseService cases) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("cases:read")
            ? Envelope.Ok<object>(cases.List())
            : Envelope.Fail("FORBIDDEN", "cases:read permission required.", 403);
    }

    [HttpPost]
    public IResult Create(CreateCaseRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.Title, "Title") ?? Guards.Check(request.Channel, "Channel")
            ?? Guards.Check(request.RequesterId, "RequesterId");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("cases:write")) return (Envelope.FailBody("FORBIDDEN", "cases:write permission required."), 403);
        var (data, code, message) = cases.Create(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/assignment")]
    public IResult Assign(string id, AssignCaseRequest request)
    {
        var err = Guards.Check(request.AssigneeId, "AssigneeId");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("cases:write")) return Envelope.Fail("FORBIDDEN", "cases:write permission required.", 403);
        var (data, code, message) = cases.Assign(u, id, request.AssigneeId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/transitions")]
    public IResult Transition(string id, TransitionCaseRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("cases:write")) return Envelope.Fail("FORBIDDEN", "cases:write permission required.", 403);
        var (data, code, message) = cases.Transition(u, id, request.Status, request.Reason);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/resolution")]
    public IResult Resolve(string id, ResolveCaseRequest request)
    {
        var err = Guards.Check(request.ResolutionCode, "ResolutionCode");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("cases:write")) return Envelope.Fail("FORBIDDEN", "cases:write permission required.", 403);
        var (data, code, message) = cases.Resolve(u, id, request.ResolutionCode);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
