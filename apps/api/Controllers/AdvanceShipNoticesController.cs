using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P4 ASN intake (056) — quarantine queue reachable via ?status=Quarantined.
[ApiController]
[Route("api/v1/advance-ship-notices")]
public sealed class AdvanceShipNoticesController(IReceiptInspectionService receipts) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status) =>
        Envelope.Ok<object>(receipts.Asns(ParseStatus(status)));

    [HttpPost]
    public IResult Create(CreateAsnRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("receipts:write")) return (Envelope.FailBody("FORBIDDEN", "receipts:write permission required."), 403);
        var (asn, code, error) = receipts.IntakeAsn(u, request.ShipmentRef, request.PoId, request.OrderVersion, request.Lines);
        return code is null ? (Envelope.OkBody(Proj(asn!)), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    private static AsnStatus? ParseStatus(string? status) =>
        Enum.TryParse<AsnStatus>(status, ignoreCase: true, out var s) ? s : null;

    private static object Proj(AdvanceShipNotice a) => new
    {
        a.Id, a.PoId, a.OrderVersion, a.ShipmentRef, a.Status, a.ReceivedBy, a.ReceivedAt,
        lines = a.Lines.Select(l => new { l.Id, l.ItemSku, l.Qty, l.Quarantined, l.QuarantineReason }).ToList(),
    };
}

// 060/Q-P6 read handoff for FINANCE AP capture — read-only, tenant-scoped.
[ApiController]
[Route("api/v1/procurement-balances")]
public sealed class ProcurementBalancesController(IReceiptInspectionService receipts) : ControllerBase
{
    [HttpGet]
    public IResult Get([FromQuery] string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Envelope.Fail("VALIDATION_ERROR", "orderId query parameter is required.", 400);
        var balances = receipts.Balances(orderId);
        return balances is null
            ? Envelope.Fail("PO_NOT_FOUND", "Purchase order not found.", 404)
            : Envelope.Ok<object>(balances);
    }
}
