using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/sales-orders")]
public sealed class SalesOrdersController(ISalesOrderService orders) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(orders.List());

    // S4: order detail with per-line fulfillment state + lifecycle rows
    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("so:write") && !u.Has("so:approve"))
            return Envelope.Fail("FORBIDDEN", "so:write or so:approve permission required.", 403);
        var (data, code, message) = orders.Detail(id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Create(CreateSoRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("so:write")) return (Envelope.FailBody("FORBIDDEN", "so:write permission required."), 403);
        var lineErr = Guards.CheckLines(request.Lines);
        if (lineErr is not null) return (Envelope.FailBody("VALIDATION_ERROR", lineErr), 400);
        var (so, code, error) = orders.Create(u, request.CustomerId, request.Lines, request.SourceQuoteRevisionId);
        return code is null ? (Envelope.OkBody(so!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    // shortcut: SO submit has no separate permission gate — the approval gate is maker-checker
    [HttpPost("{id}/submit")]
    public IResult Submit(string id)
    {
        var (data, code, message) = orders.Submit(CurrentUser.From(HttpContext), id);
        // 013 AC2: the onboarding gate returns the unmet control ids as items
        if (code == "ONBOARDING_CONTROLS_UNMET")
            return Envelope.From(Envelope.FailBodyItems(code, message!, (object[])data!), ErrorStatus.StatusFor(code));
        return data is null ? Envelope.Fail(code!, message!, ErrorStatus.StatusFor(code!)) : Envelope.Ok<object>(data);
    }

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, ApproveSoRequest request)
    {
        var (so, code, error) = orders.Approve(CurrentUser.From(HttpContext), id, request.Reason);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(so!);
    }

    // Rejection is its own decision: requires a reason, lands the SO in Rejected.
    [HttpPost("{id}/rejections")]
    public IResult Reject(string id, RejectSoRequest request)
    {
        var (so, code, error) = orders.Reject(CurrentUser.From(HttpContext), id, request.Reason);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(so!);
    }

    [HttpPost("{id}/shipments")]
    public IResult Ship(string id, ShipRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("shipments:write")) return Envelope.Fail("FORBIDDEN", "shipments:write permission required.", 403);
        var (shipment, journal, code, error) = orders.Ship(u, id, request.ItemSku, request.Qty, request.ExpectedDate);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(new { shipment, journal }, 201);
    }

    // --- deferred-screens wave (S4): lifecycle routes ---------------------------------

    [HttpPost("{id}/holds")]
    public IResult PlaceHold(string id, PlaceHoldRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Reason, "Reason") ?? (Enum.TryParse<OrderHoldType>(request.HoldType, true, out _)
            ? null : "HoldType must be Credit, Compliance, Pricing or Manual.");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.PlaceHold(u, id, Enum.Parse<OrderHoldType>(request.HoldType, true), request.Reason, request.Source);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/holds/{holdId}/releases")]
    public IResult ReleaseHold(string holdId, ReleaseHoldRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.ReleaseHold(u, holdId, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/amendments")]
    public IResult CreateAmendment(string id, CreateAmendmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Rationale, "Rationale")
            ?? (request.Lines is null || request.Lines.Count == 0 ? "At least one amendment line is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.CreateAmendment(u, id, request.Lines!, request.Rationale);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/amendments/{amendmentId}/approvals")]
    public IResult ApproveAmendment(string amendmentId, ApproveAmendmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.ApproveAmendment(u, amendmentId, request.Reason);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/cancellations")]
    public IResult Cancel(string id, CancelOrderRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Reason, "Reason")
            ?? (Enum.TryParse<CancellationScope>(request.Scope, true, out _)
                ? null : "Scope must be Remaining or Full.");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.CancelOrder(u, id, request.Reason, Enum.Parse<CancellationScope>(request.Scope, true));
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // recurring schedules live on their own prefix; so:write's recurring:write gate is inside
    [HttpPost("/api/v1/recurring-schedules")]
    public IResult CreateSchedule(CreateScheduleRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CustomerId, "CustomerId") ?? Guards.Check(request.Frequency, "Frequency", 20)
            ?? (Enum.TryParse<RecurringFrequency>(request.Frequency, true, out _)
                ? null : "Frequency must be Weekly or Monthly.")
            ?? (request.Lines is null || request.Lines.Count == 0 ? "At least one schedule line is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.CreateSchedule(u, request.CustomerId,
            Enum.Parse<RecurringFrequency>(request.Frequency, true), request.NextRunDate, request.Lines!);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("/api/v1/recurring-schedules/{id}/pauses")]
    public IResult PauseSchedule(string id, PauseRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.PauseSchedule(u, id);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("/api/v1/recurring-schedules/{id}/generations")]
    public IResult Generate(string id, GenerateRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Period, "Period", 10);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = orders.Generate(u, id, request.Period);
        // idempotent replay of an existing (schedule, period) occurrence returns 200
        if (code is not null) return (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
        var replay = (bool)data!.GetType().GetProperty("replay")!.GetValue(data)!;
        return (Envelope.OkBody(data), replay ? 200 : 201);
    });

    // R-T7f: the journal-only /sales-orders/{id}/invoices endpoint was RETIRED — invoice
    // documents (draft → maker-checker posting) live in InvoicesController instead.

    // 057 AC2: releasing a credit-held order is a credit:override act, audited + outboxed
    [HttpPost("{id}/credit-releases")]
    public IResult ReleaseCredit(string id, ReleaseCreditRequest request)
    {
        var (so, code, error) = orders.ReleaseCreditHold(CurrentUser.From(HttpContext), id);
        return code is not null ? Envelope.Fail(code, error!, ErrorStatus.StatusFor(code)) : Envelope.Ok<object>(so!);
    }
}
