using Erp.Api.Dtos;
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

    // FINANCE-011: the Idempotency-Key carries the source identity — same key + same payload
    // replays the reference, same key + different payload → 409 IDEMPOTENCY_CONFLICT (payload
    // hash compared inside Idem.Wrap on top of the standard replay)
    [HttpPost]
    public IResult Create(CreateJournalRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Memo, "Memo")
            ?? (request.Lines is null || request.Lines.Count == 0 ? "At least one journal line is required." : null)
            ?? LineErrors(request.Lines);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("journals:write")) return (Envelope.FailBody("FORBIDDEN", "journals:write permission required."), 403);
        var (journal, code, error) = journals.Create(u, request);
        return code is not null ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code)) : (Envelope.OkBody(journal!), 201);
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, [FromBody] ApproveJournalRequest? request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("journals:approve")) return Envelope.Fail("FORBIDDEN", "journals:approve permission required.", 403);
        var (journal, code, error) = journals.Approve(u, id, request?.Reason);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(journal!);
    }

    [HttpPost("{id}/reversals")]
    public IResult Reverse(string id)
    {
        var (rev, code, error) = journals.Reverse(CurrentUser.From(HttpContext), id);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(rev!, 201);
    }

    private static string? LineErrors(IReadOnlyList<JournalLineInput> lines)
    {
        foreach (var line in lines)
        {
            var err = Guards.Check(line.AccountCode, "AccountCode", 50) ?? Guards.Check(line.AccountName, "AccountName");
            if (err is not null) return err;
            if (line.Debit < 0 || line.Credit < 0) return "Line debit and credit must be >= 0.";
            if (line.Debit > 0 && line.Credit > 0) return "A line cannot carry both debit and credit amounts.";
            if (line.Debit == 0 && line.Credit == 0) return "Each line needs a debit or a credit amount.";
        }
        return null;
    }
}
