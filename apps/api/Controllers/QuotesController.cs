using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Quotes (ERP-SALES-021/022/023/030): revisions, maker-checker approvals, acceptance evidence.
// Reads gate on quotes:write (the brief's permission table carries no separate quotes:read).
[ApiController]
[Route("api/v1/quotes")]
public sealed class QuotesController(IQuoteService quotes) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quotes:write")) return Envelope.Fail("FORBIDDEN", "quotes:write permission required.", 403);
        return Envelope.Ok<object>(quotes.List());
    }

    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quotes:write")) return Envelope.Fail("FORBIDDEN", "quotes:write permission required.", 403);
        var (data, code, message) = quotes.Detail(id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Create(CreateQuoteRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CustomerId, "CustomerId") ?? Guards.Check(request.Currency, "Currency", 3)
            ?? (request.Lines is null || request.Lines.Count == 0 ? "At least one quote line is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quotes:write")) return (Envelope.FailBody("FORBIDDEN", "quotes:write permission required."), 403);
        var (data, code, message) = quotes.Create(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/revisions")]
    public IResult CreateRevision(string id, CreateQuoteRevisionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = request.Lines is null || request.Lines.Count == 0 ? "At least one quote line is required." : null;
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quotes:write")) return (Envelope.FailBody("FORBIDDEN", "quotes:write permission required."), 403);
        var (data, code, message) = quotes.CreateRevision(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/revisions/{revision}/approvals")]
    public IResult Approve(string id, int revision, ApproveQuoteRevisionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = quotes.ApproveRevision(u, id, revision, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/revisions/{revision}/acceptances")]
    public IResult Accept(string id, int revision, AcceptQuoteRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Method, "Method", 50);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quotes:write")) return (Envelope.FailBody("FORBIDDEN", "quotes:write permission required."), 403);
        var (data, replayed, code, message) = quotes.AcceptRevision(u, id, revision, request);
        // idempotent replay of an external acceptance event returns 200, a fresh acceptance 201
        return code is not null ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), replayed ? 200 : 201);
    });

    [HttpPost("{id}/revalidations")]
    public IResult Revalidate(string id, RevalidateQuoteRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Reason, "Reason");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = quotes.Revalidate(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
