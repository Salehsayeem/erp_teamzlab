using System.Text.Json;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P4 receipt workspace (055–059): dispatch evidence (055), ASN intake with quarantine (056 — the
// notice never touches received qty), quality inspections separating accepted from invoice-
// matchable (058, Q-P7 SoD), linked returns/claims (059), and the read-only balances handoff for
// FINANCE AP (060/Q-P6). None of these post journals, so no period guard fires here.
public sealed class ReceiptInspectionService(
    IReceiptInspectionRepository receipts, IPurchaseOrderChangeRepository orders,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IReceiptInspectionService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public (Dispatch? Dispatch, string? Code, string? Message) SendDispatch(
        CurrentUser u, string poId, int orderVersion, DispatchState state, string? detail)
    {
        var po = orders.FindPo(poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (orders.HasPendingChange(poId))
            return (null, "PO_CHANGE_PENDING", "A change order is pending; the PO cannot be transmitted until approval completes (052 AC2).");
        if (orderVersion != po.Version)
            return (null, "PO_ORDER_VERSION_CONFLICT", $"Dispatch names version {orderVersion} but the order is at version {po.Version}.");
        if (!Enum.IsDefined(state))
            return (null, "VALIDATION_ERROR", "Dispatch state must be Sent, Failed or Unknown (055 AC3).");

        var dispatch = new Dispatch($"{poId}:dsp{DateTimeOffset.UtcNow.Ticks}", ec.TenantId, ec.EntityId,
            poId, orderVersion, state, detail, u.Id, DateTimeOffset.UtcNow);
        receipts.AddDispatch(dispatch);
        if (state is DispatchState.Sent && po.Status is PoStatus.Approved)
            orders.SetField(po, nameof(PurchaseOrder.Status), PoStatus.Sent);
        audit.Log(u, "dispatch", "purchase_order", poId, $"Dispatch evidence {dispatch.Id}: {state} (order v{orderVersion})");
        audit.Emit(u, "procurement.po.dispatched", new { poId, orderVersion, state, dispatchId = dispatch.Id });
        receipts.Save();
        return (dispatch, null, null);
    }

    // 056: match to open lines; unmatched/over lines quarantine, and the notice NEVER increases
    // received qty — no receipt rows, no stock, no journals are touched here.
    public (AdvanceShipNotice? Asn, string? Code, string? Message) IntakeAsn(
        CurrentUser u, string? shipmentRef, string poId, int orderVersion, IReadOnlyList<AsnLineInput>? lines)
    {
        var po = orders.FindPo(poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (orderVersion != po.Version)
            return (null, "PO_ORDER_VERSION_CONFLICT", $"ASN names version {orderVersion} but the order is at version {po.Version}.");
        if (lines is null || lines.Count == 0)
            return (null, "VALIDATION_ERROR", "At least one ASN line is required.");

        var receivedBySku = receipts.ReceiptsForPo(poId)
            .GroupBy(r => r.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var asnLines = new List<AsnLine>();
        foreach (var (input, i) in lines.Select((l, i) => (l, i + 1)))
        {
            var line = po.Lines.FirstOrDefault(l => l.ItemSku == input.ItemSku);
            var open = line is null ? 0m : line.Qty - receivedBySku.GetValueOrDefault(line.ItemSku);
            var quarantined = line is null || input.Qty > open;
            var reason = line is null
                ? $"line {i}: item '{input.ItemSku}' is not on order {poId}"
                : quarantined ? $"line {i}: {input.Qty:0.##} exceeds the open qty {open:0.##} for '{input.ItemSku}'" : null;
            asnLines.Add(new AsnLine($"{asnLineId(poId)}:l{i}", input.ItemSku ?? "", input.Qty, quarantined, reason));
        }
        var asn = new AdvanceShipNotice(numbers.Next("ASN"), ec.TenantId, ec.EntityId, poId, orderVersion,
            shipmentRef ?? "", asnLines.Any(l => l.Quarantined) ? AsnStatus.Quarantined : AsnStatus.Matched,
            u.Id, DateTimeOffset.UtcNow)
        { Lines = asnLines };
        receipts.AddAsn(asn);
        audit.Log(u, "intake", "advance_ship_notice", asn.Id,
            $"ASN {asn.Id} for {poId}: {asnLines.Count(l => !l.Quarantined)} matched, {asnLines.Count(l => l.Quarantined)} quarantined");
        receipts.Save();
        return (asn, null, null);
    }

    public object Asns(AsnStatus? status) => receipts.Asns(status).Select(a => ProjAsn(a)).ToList();

    public (ReceiptInspection? Inspection, string? Code, string? Message) CreateInspection(
        CurrentUser u, string receiptId, string? itemSku, decimal qtyReceived)
    {
        var receipt = receipts.FindReceipt(receiptId);
        if (receipt is null) return (null, "RECEIPT_NOT_FOUND", "Receipt not found.");
        if (receipt.ItemSku != itemSku)
            return (null, "VALIDATION_ERROR", $"Inspection must name the receipt's item ({receipt.ItemSku}).");
        if (qtyReceived <= 0 || qtyReceived > receipt.Qty)
            return (null, "VALIDATION_ERROR", $"Inspection qty must be between 0 and the received qty ({receipt.Qty:0.##}).");
        var inspection = new ReceiptInspection(numbers.Next("INS"), ec.TenantId, ec.EntityId, receiptId,
            receipt.PurchaseOrderId, receipt.ItemSku, qtyReceived, 0m, 0m, InspectionStatus.Pending,
            null, null, u.Id, DateTimeOffset.UtcNow);
        receipts.AddInspection(inspection);
        audit.Log(u, "create", "receipt_inspection", inspection.Id, $"Inspection lot {inspection.Id} opened for receipt {receiptId}");
        receipts.Save();
        return (inspection, null, null);
    }

    public (ReceiptInspection? Inspection, string? Code, string? Message) DisposeInspection(
        CurrentUser actor, string receiptId, string inspectionId, InspectionStatus status, decimal qtyAccepted,
        decimal qtyRejected, string? defectCode, string? evidenceRef)
    {
        var inspection = receipts.FindInspection(inspectionId);
        if (inspection is null || inspection.ReceiptId != receiptId)
            return (null, "INSPECTION_NOT_FOUND", "Inspection not found for this receipt.");
        var receipt = receipts.FindReceipt(receiptId);
        if (receipt is null) return (null, "RECEIPT_NOT_FOUND", "Receipt not found.");
        if (!Enum.IsDefined(status) || status is InspectionStatus.Pending)
            return (null, "VALIDATION_ERROR", "Disposition must be Accepted, Partial or Rejected.");
        if (qtyAccepted < 0 || qtyRejected < 0 || qtyAccepted + qtyRejected != inspection.QtyReceived)
            return (null, "VALIDATION_ERROR", $"Accepted + rejected must equal the inspected qty ({inspection.QtyReceived:0.##}).");
        if (status is InspectionStatus.Accepted && qtyRejected > 0) return (null, "VALIDATION_ERROR", "Accepted lots cannot carry rejected qty.");
        if (status is InspectionStatus.Rejected && qtyAccepted > 0) return (null, "VALIDATION_ERROR", "Rejected lots cannot carry accepted qty.");
        if (status is InspectionStatus.Partial && qtyAccepted <= 0 || status is InspectionStatus.Partial && qtyRejected <= 0)
            return (null, "VALIDATION_ERROR", "Partial dispositions need both accepted and rejected qty.");
        // Q-P7 SoD: the receiver never disposes their own receipt (receipts:dispose is R-QC-only)
        if (receipt.ReceivedBy == actor.Id)
            return (null, "DISPOSITION_SOD_ERROR", "SoD: the receiver cannot disposition their own receipt.");
        if (status is InspectionStatus.Rejected or InspectionStatus.Partial && string.IsNullOrWhiteSpace(defectCode))
            return (null, "VALIDATION_ERROR", "Rejected/partial dispositions record a defect code (058 AC3).");

        receipts.SetField(inspection, nameof(ReceiptInspection.Status), status);
        receipts.SetField(inspection, nameof(ReceiptInspection.QtyAccepted), qtyAccepted);
        receipts.SetField(inspection, nameof(ReceiptInspection.QtyRejected), qtyRejected);
        receipts.SetField(inspection, nameof(ReceiptInspection.DefectCode), defectCode);
        receipts.SetField(inspection, nameof(ReceiptInspection.EvidenceRef), evidenceRef);
        receipts.SetField(inspection, nameof(ReceiptInspection.DispositionedBy), actor.Id);
        receipts.SetField(inspection, nameof(ReceiptInspection.DispositionedAt), DateTimeOffset.UtcNow);
        audit.Log(actor, "dispose", "receipt_inspection", inspection.Id,
            $"Inspection {inspectionId}: {qtyAccepted:0.##} accepted / {qtyRejected:0.##} rejected ({status}, defect {defectCode ?? "-"}) by {actor.Email}");
        receipts.Save();
        return (inspection, null, null);
    }

    // 059 AC1: a return can only draw down received-minus-rejected minus already-returned
    public (SupplierReturn? Return, string? Code, string? Message) CreateReturn(
        CurrentUser u, string? receiptId, string? poId, string? itemSku, decimal qty, string? reason, ClaimKind kind)
    {
        var receipt = receipts.FindReceipt(receiptId ?? "");
        if (receipt is null) return (null, "RECEIPT_NOT_FOUND", "The return must reference an existing receipt.");
        if (poId != receipt.PurchaseOrderId || itemSku != receipt.ItemSku)
            return (null, "VALIDATION_ERROR", "The return must reference the receipt's own order line.");
        if (qty <= 0) return (null, "VALIDATION_ERROR", "Return qty must be greater than 0.");
        if (string.IsNullOrWhiteSpace(reason)) return (null, "VALIDATION_ERROR", "A return reason is required.");

        var accepted = AcceptedFor(receipt.PurchaseOrderId, receipt.ItemSku);
        var alreadyReturned = receipts.ReturnsForPo(receipt.PurchaseOrderId)
            .Where(r => r.ItemSku == receipt.ItemSku).Sum(r => (decimal?)r.Qty) ?? 0m;
        if (qty > accepted - alreadyReturned)
            return (null, "RETURN_OVER_RECEIVED",
                $"Return exceeds accepted qty: {accepted:0.##} accepted − {alreadyReturned:0.##} already returned, cannot return {qty:0.##} (059 AC1).");

        var ret = new SupplierReturn(numbers.Next("RTN"), ec.TenantId, ec.EntityId, receiptId!, poId!, itemSku!,
            qty, reason.Trim(), kind, u.Id, DateTimeOffset.UtcNow);
        receipts.AddReturn(ret);
        audit.Log(u, "create", "supplier_return", ret.Id, $"Return {ret.Id}: {qty:0.##} × {itemSku} against receipt {receiptId} ({kind})");
        receipts.Save();
        return (ret, null, null);
    }

    public (SupplierClaim? Claim, string? Code, string? Message) CreateClaim(
        CurrentUser u, string? receiptId, string? poId, ClaimKind kind, decimal? amount)
    {
        if (string.IsNullOrWhiteSpace(poId) || orders.FindPo(poId) is null)
            return (null, "PO_NOT_FOUND", "The claim must reference an existing purchase order.");
        if (receiptId is { } && receipts.FindReceipt(receiptId) is null)
            return (null, "RECEIPT_NOT_FOUND", "The referenced receipt does not exist.");
        if (kind is ClaimKind.Credit && amount is null or <= 0)
            return (null, "VALIDATION_ERROR", "Credit claims must carry a positive amount.");
        var claim = new SupplierClaim(numbers.Next("CLM"), ec.TenantId, ec.EntityId, receiptId, poId!, kind, amount,
            ClaimStatus.Open, null, null, u.Id, DateTimeOffset.UtcNow);
        receipts.AddClaim(claim);
        audit.Log(u, "create", "supplier_claim", claim.Id, $"Claim {claim.Id} opened ({kind}) against {poId}");
        receipts.Save();
        return (claim, null, null);
    }

    // 059 AC3: closure needs an outcome code + reconciliation status, and never by the claim's creator
    public (SupplierClaim? Claim, string? Code, string? Message) CloseClaim(
        CurrentUser actor, string claimId, string? outcomeCode, string? reconciliationStatus)
    {
        var claim = receipts.FindClaim(claimId);
        if (claim is null) return (null, "CLAIM_NOT_FOUND", "Claim not found.");
        if (!actor.Has("suppliers:review"))
            return (null, "FORBIDDEN", "suppliers:review permission required to close a claim.");
        if (claim.CreatedBy == actor.Id)
            return (null, "CLAIM_STATE_ERROR", "Maker-checker: the claim creator cannot close their own claim.");
        if (claim.Status is ClaimStatus.Closed)
            return (null, "CLAIM_STATE_ERROR", "Claim is already closed.");
        if (string.IsNullOrWhiteSpace(outcomeCode) || string.IsNullOrWhiteSpace(reconciliationStatus))
            return (null, "CLAIM_OUTCOME_REQUIRED", "Closure requires an outcome code and a reconciliation status (059 AC3).");

        receipts.SetField(claim, nameof(SupplierClaim.Status), ClaimStatus.Closed);
        receipts.SetField(claim, nameof(SupplierClaim.OutcomeCode), outcomeCode.Trim());
        receipts.SetField(claim, nameof(SupplierClaim.ReconciliationStatus), reconciliationStatus.Trim());
        receipts.SetField(claim, nameof(SupplierClaim.ClosedBy), actor.Id);
        receipts.SetField(claim, nameof(SupplierClaim.ClosedAt), DateTimeOffset.UtcNow);
        audit.Log(actor, "close", "supplier_claim", claimId, $"Claim {claimId} closed: {outcomeCode} / {reconciliationStatus} by {actor.Email}");
        receipts.Save();
        return (claim, null, null);
    }

    // 060/Q-P6 read handoff: ordered/received/rejected/accepted(invoice-matchable)/remaining with
    // calc time + source version. POs carry no currency column yet — surfaced as null (documented).
    public object? Balances(string orderId)
    {
        var po = orders.FindPo(orderId);
        if (po is null) return null;
        var ordered = po.Lines.Sum(l => l.Qty);
        var received = receipts.ReceiptsForPo(orderId).Sum(r => r.Qty);
        var rejected = receipts.InspectionsForPo(orderId).Sum(i => i.QtyRejected);
        var accepted = received - rejected;
        return new
        {
            orderId, ordered, received, rejected, accepted, remaining = ordered - accepted,
            currency = (string?)null, calculatedAt = DateTimeOffset.UtcNow, sourceVersion = po.Version,
        };
    }

    // receipt-workspace detail: the receipt, its inspection lots and its returns + the PO balances
    public object? ReceiptDetail(string receiptId)
    {
        var receipt = receipts.FindReceipt(receiptId);
        if (receipt is null) return null;
        return new
        {
            receipt = new { receipt.Id, receipt.PurchaseOrderId, receipt.ItemSku, receipt.Qty, receipt.ReceivedAt, receipt.ReceivedBy },
            inspections = receipts.InspectionsForReceipt(receiptId).Select(i => new
            {
                i.Id, i.ItemSku, i.QtyReceived, i.QtyAccepted, i.QtyRejected, i.Status,
                i.DefectCode, i.EvidenceRef, i.DispositionedBy, i.DispositionedAt, i.CreatedBy, i.CreatedAt,
            }).ToList(),
            returns = receipts.ReturnsForReceipt(receiptId).Select(r => new
            {
                r.Id, r.ItemSku, r.Qty, r.Reason, r.Kind, r.CreatedBy, r.CreatedAt,
            }).ToList(),
            balances = Balances(receipt.PurchaseOrderId),
        };
    }

    private decimal AcceptedFor(string poId, string itemSku)
    {
        var received = receipts.ReceiptsForPo(poId).Where(r => r.ItemSku == itemSku).Sum(r => r.Qty);
        var rejected = receipts.InspectionsForPo(poId).Where(i => i.ItemSku == itemSku).Sum(i => i.QtyRejected);
        return received - rejected;
    }

    private static object ProjAsn(AdvanceShipNotice a) => new
    {
        a.Id, a.PoId, a.OrderVersion, a.ShipmentRef, a.Status, a.ReceivedBy, a.ReceivedAt,
        lines = a.Lines.Select(l => new { l.Id, l.ItemSku, l.Qty, l.Quarantined, l.QuarantineReason }).ToList(),
    };

    private static string asnLineId(string poId) => $"{poId}:{DateTimeOffset.UtcNow.Ticks}";
}
