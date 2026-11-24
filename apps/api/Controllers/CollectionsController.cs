using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Collections (ERP-SALES-058/059): worklist, manual reminders (R-T7g), disputes with
// promise-to-pay. Credit exposure lives on the customer surface (credit:read).
[ApiController]
[Route("api/v1/collections")]
public sealed class CollectionsController(ICollectionService collections) : ControllerBase
{
    [HttpGet("worklist")]
    public IResult Worklist()
    {
        var u = CurrentUser.From(HttpContext);
        return u.Has("collections:read")
            ? Envelope.Ok<object>(collections.Worklist())
            : Envelope.Fail("FORBIDDEN", "collections:read permission required.", 403);
    }

    [HttpPost("reminders")]
    public IResult LogReminder(LogReminderRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.InvoiceId, "InvoiceId") ?? Guards.Check(request.Channel, "Channel", 50)
            ?? Guards.Check(request.Recipient, "Recipient");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("collections:write")) return (Envelope.FailBody("FORBIDDEN", "collections:write permission required."), 403);
        var (reminder, code, message) = collections.LogReminder(u, request);
        return code is null ? (Envelope.OkBody(reminder!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("disputes")]
    public IResult CreateDispute(CreateDisputeRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.InvoiceId, "InvoiceId") ?? Guards.Check(request.Reason, "Reason");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("collections:write")) return (Envelope.FailBody("FORBIDDEN", "collections:write permission required."), 403);
        var (dispute, code, message) = collections.CreateDispute(u, request);
        return code is null ? (Envelope.OkBody(dispute!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("disputes/{id}/resolutions")]
    public IResult ResolveDispute(string id, ResolveDisputeRequest request)
    {
        // 059: an outcome is mandatory — no silent closures
        var err = Guards.Check(request.Outcome, "Outcome");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("collections:write")) return Envelope.Fail("FORBIDDEN", "collections:write permission required.", 403);
        var (dispute, code, message) = collections.ResolveDispute(u, id, request.Outcome);
        return code is null ? Envelope.Ok<object>(dispute!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
