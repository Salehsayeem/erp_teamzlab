using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/receipts")]
public sealed class ReceiptsController(IReceiptService receipts, IReceiptInspectionService inspections) : ControllerBase
{
    // deferred PROCUREMENT wave: tenant-scoped receipt list (the P4 workspace substrate)
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(receipts.List());

    [HttpPost]
    public IResult Create(CreateReceiptRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts:write")) return (Envelope.FailBody("FORBIDDEN", "receipts:write permission required."), 403);
        var (receipt, journal, code, error) = receipts.Receive(u, request.PurchaseOrderId, request.ItemSku, request.Qty, request.ToleranceOverrideReason);
        return code is not null
            ? (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(new { receipt, journal }), 201);
    });

    // -------- deferred PROCUREMENT wave: inspection workspace (058, Q-P7 SoD) --------

    [HttpPost("{id}/inspections")]
    public IResult CreateInspection(string id, CreateInspectionRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts:inspect")) return Envelope.Fail("FORBIDDEN", "receipts:inspect permission required.", 403);
        var (inspection, code, error) = inspections.CreateInspection(u, id, request.ItemSku, request.QtyReceived);
        return code is null ? Envelope.Ok<object>(inspection!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/inspections/{inspectionId}/dispositions")]
    public IResult DisposeInspection(string id, string inspectionId, DisposeInspectionRequest request)
    {
        var (inspection, code, error) = inspections.DisposeInspection(CurrentUser.From(HttpContext), id, inspectionId,
            request.Status, request.QtyAccepted, request.QtyRejected, request.DefectCode, request.EvidenceRef);
        return code is null ? Envelope.Ok<object>(inspection!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }
}
