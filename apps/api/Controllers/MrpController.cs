using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// MF4 — MRP runs, recommendations, conversions, publications (ERP-MFG-033..040).
// mfg:mrp on every write; mfg:read on reads. Conversion + publication are maker-checker gated
// at the service (actor ≠ run maker).
[ApiController]
[Route("api/v1/mrp-runs")]
public sealed class MrpController(IMrpService mrp) : ControllerBase
{
    [HttpPost]
    public IResult Create(CreateMrpRunRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:mrp")) return (Envelope.FailBody("FORBIDDEN", "mfg:mrp permission required."), 403);
        var (data, code, message) = mrp.CreateRun(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpGet("{id}/recommendations")]
    public IResult Recommendations(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(mrp.Recommendations(id));
    }

    [HttpGet("{id}/action-messages")]
    public IResult ActionMessages(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(mrp.ActionMessages(id));
    }

    [HttpPost("{id}/publications")]
    public IResult Publish(string id, [FromBody] MrpPublicationRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:mrp")) return (Envelope.FailBody("FORBIDDEN", "mfg:mrp permission required."), 403);
        var (data, code, message) = mrp.Publish(u, id, request ?? new MrpPublicationRequest(null, null));
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/mrp-lines")]
public sealed class MrpLinesController(IMrpService mrp) : ControllerBase
{
    [HttpPost("{id}/conversions")]
    public IResult Convert(string id, [FromBody] MrpConversionRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:mrp")) return (Envelope.FailBody("FORBIDDEN", "mfg:mrp permission required."), 403);
        var (data, code, message) = mrp.Convert(u, id, request ?? new MrpConversionRequest(null));
        return code switch
        {
            null => (Envelope.OkBody(data!), 201),
            "MRP_LINE_REPLAY" => (Envelope.OkBody(data!), 200), // idempotent retry — same linked order
            _ => (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code)),
        };
    });

    [HttpPost("{id}/decisions")]
    public IResult Decide(string id, MrpLineDecisionRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:mrp")) return Envelope.Fail("FORBIDDEN", "mfg:mrp permission required.", 403);
        var (data, code, message) = mrp.DecideLine(u, id, request);
        return code is not null
            ? Envelope.Fail(code, message!, ErrorStatus.StatusFor(code))
            : Envelope.Ok<object>(data!);
    }
}
