using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Erp.Api.Services; // CrmService.Channels is the single channel whitelist
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// CRM: interaction timeline + cases (ERP-SALES-007/008). R-T7d: distinct from the ASSETS
// service-cases surface; cases get crm:* permissions and the R8d response-target mapping.
[ApiController]
[Route("api/v1/crm")]
public sealed class CrmController(ICrmService crm) : ControllerBase
{
    [HttpGet("interactions")]
    public IResult Interactions([FromQuery] string? partyType, [FromQuery] string? partyId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("crm:read")) return Envelope.Fail("FORBIDDEN", "crm:read permission required.", 403);
        InteractionPartyType? type = null;
        if (partyType is not null)
        {
            if (!Enum.TryParse<InteractionPartyType>(partyType, ignoreCase: true, out var parsed))
                return Envelope.Fail("VALIDATION_ERROR", $"partyType '{partyType}' is not valid (Lead, Customer, Opportunity).", 400);
            type = parsed;
        }
        return Envelope.Ok<object>(crm.Interactions(type, partyId));
    }

    [HttpPost("interactions")]
    public IResult LogInteraction(LogInteractionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        // 007: party + a known channel + a subject are required; OccurredAt drives the ordering
        var err = Guards.Check(request.PartyId, "PartyId") ?? Guards.Check(request.Subject, "Subject")
            ?? (CrmService.Channels.Contains(request.Channel) ? null : $"Channel must be one of {string.Join(", ", CrmService.Channels)}.")
            ?? (request.OccurredAt == default ? "OccurredAt is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("crm:write")) return (Envelope.FailBody("FORBIDDEN", "crm:write permission required."), 403);
        var (interaction, code, message) = crm.LogInteraction(u, request);
        return code is null ? (Envelope.OkBody(interaction!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("cases")]
    public IResult Cases([FromQuery] bool? overdue)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("crm:read")) return Envelope.Fail("FORBIDDEN", "crm:read permission required.", 403);
        return Envelope.Ok<object>(crm.Cases(overdue == true));
    }

    [HttpPost("cases")]
    public IResult CreateCase(CreateCrmCaseRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Category, "Category");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("crm:write")) return (Envelope.FailBody("FORBIDDEN", "crm:write permission required."), 403);
        var (crmCase, code, message) = crm.CreateCase(u, request);
        return code is null ? (Envelope.OkBody(crmCase!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("cases/{id}/transitions")]
    public IResult TransitionCase(string id, TransitionCrmCaseRequest request)
    {
        // 008 AC2 on the transitions path too: reaching Resolved must carry resolution text
        if (request.Status == CrmCaseStatus.Resolved && string.IsNullOrWhiteSpace(request.Resolution))
            return Envelope.Fail("VALIDATION_ERROR", "Resolution is required when transitioning to Resolved.", 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("crm:write")) return Envelope.Fail("FORBIDDEN", "crm:write permission required.", 403);
        var (crmCase, code, message) = crm.TransitionCase(u, id, request.Status, request.Resolution);
        return code is null ? Envelope.Ok<object>(crmCase!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
