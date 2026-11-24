using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Invoice documents (ERP-SALES-042..046/048/052/053): drafts from shipment source events,
// maker-checker posting, credit/debit adjustments. The draft route hangs off the sales-order
// surface; everything else lives under /invoices and /adjustments.
[ApiController]
[Route("api/v1")]
public sealed class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    // drafts are drafted from the order — the editor side of the 043 separation
    [HttpPost("sales-orders/{soId}/invoice-drafts")]
    public IResult Draft(string soId, CreateInvoiceDraftRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("invoices:write")) return (Envelope.FailBody("FORBIDDEN", "invoices:write permission required."), 403);
        var (invoice, code, message) = invoices.DraftFromShipments(u, soId, request.InvoiceDate);
        return code is null ? (Envelope.OkBody(invoice!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("invoices")]
    public IResult List([FromQuery] string? status, [FromQuery] string? customerId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("invoices:read")) return Envelope.Fail("FORBIDDEN", "invoices:read permission required.", 403);
        InvoiceStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed))
                return Envelope.Fail("VALIDATION_ERROR", $"status '{status}' is not a valid invoice status (Draft, Posted).", 400);
            statusFilter = parsed;
        }
        return Envelope.Ok<object>(invoices.List(statusFilter, customerId));
    }

    // 045: the posting side of the 043 separation — invoices:approve, approver ≠ drafter
    [HttpPost("invoices/{id}/postings")]
    public IResult Post(string id, PostInvoiceRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = invoices.Post(u, id);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("adjustments")]
    public IResult CreateAdjustment(CreateAdjustmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Reason, "Reason") ?? Guards.Check(request.InvoiceId, "InvoiceId");
        if (err is null && (request.Lines is null || request.Lines.Count == 0))
            err = "At least one adjustment line is required.";
        if (err is null && request.Lines!.Any(l => l.Qty <= 0 || l.UnitPrice < 0))
            err = "Line qty must be > 0 and unit price must be >= 0.";
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("invoices:write")) return (Envelope.FailBody("FORBIDDEN", "invoices:write permission required."), 403);
        var (adjustment, code, message) = invoices.CreateAdjustment(u, request);
        return code is null ? (Envelope.OkBody(adjustment!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // 046 elevated approval — same invoices:approve gate as invoice posting, approver ≠ drafter
    [HttpPost("adjustments/{id}/approvals")]
    public IResult ApproveAdjustment(string id, ApproveAdjustmentRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        var (adjustment, code, message) = invoices.ApproveAdjustment(u, id, request.Reason);
        return code is null ? Envelope.Ok<object>(adjustment!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    // review 1b: the drafter's (or admin's) escape hatch for a mis-drafted draft — terminal,
    // frees the shipment source event, burns the INV number (audited)
    [HttpPost("invoices/{id}/discards")]
    public IResult Discard(string id)
    {
        var u = CurrentUser.From(HttpContext);
        var (invoice, code, message) = invoices.Discard(u, id);
        return code is null ? Envelope.Ok<object>(invoice!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
