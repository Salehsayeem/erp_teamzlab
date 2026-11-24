using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Leads (ERP-SALES-003/004/005): capture (server receipt time), qualification lifecycle,
// idempotent conversion. Routes lowercase-hyphen under /api/v1.
[ApiController]
[Route("api/v1/leads")]
public sealed class LeadsController(ILeadService leads) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leads:read")) return Envelope.Fail("FORBIDDEN", "leads:read permission required.", 403);
        var (ok, parsed) = Query.Enum<LeadStatus>(status);
        return ok ? Envelope.Ok<object>(leads.List(parsed))
            : Envelope.Fail("VALIDATION_ERROR", $"status '{status}' is not a valid lead status.", 400);
    }

    [HttpPost]
    public IResult Create(CreateLeadRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        // 003: Source is the only required capture field — anonymous inquiries are valid
        var err = Guards.Check(request.Source, "Source", 50);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leads:write")) return (Envelope.FailBody("FORBIDDEN", "leads:write permission required."), 403);
        var (lead, code, message) = leads.Create(u, request);
        return code is null ? (Envelope.OkBody(lead!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/transitions")]
    public IResult Transition(string id, TransitionLeadRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leads:write")) return Envelope.Fail("FORBIDDEN", "leads:write permission required.", 403);
        if (request.Status == LeadStatus.Disqualified && string.IsNullOrWhiteSpace(request.ReasonCode))
            return Envelope.Fail("VALIDATION_ERROR", "ReasonCode is required when disqualifying a lead.", 400);
        var (lead, code, message) = leads.Transition(u, id, request.Status, request.ReasonCode);
        return code is null ? Envelope.Ok<object>(lead!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/conversions")]
    public IResult Convert(string id, ConvertLeadRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("leads:write")) return (Envelope.FailBody("FORBIDDEN", "leads:write permission required."), 403);
        var (data, code, message) = leads.Convert(u, id);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
