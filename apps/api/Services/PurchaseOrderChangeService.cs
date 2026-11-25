using System.Text.Json;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P3 (052/053): versioned change orders with re-approval on released POs, and supplier acks pinned
// to an exact order version — superseded acks stored + flagged, discrepancies raise buyer tasks
// without mutating the order.
public sealed class PurchaseOrderChangeService(
    IPurchaseOrderChangeRepository orders, ISupplierRepository suppliers,
    IAuditRepository audit, EntityContext ec) : IPurchaseOrderChangeService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public (PurchaseOrderChange? Change, string? Code, string? Message) Create(
        CurrentUser u, string poId, string? rationale, string? supplierId, IReadOnlyList<PoChangeLineInput>? lines)
    {
        var po = orders.FindPo(poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (po.Status is not (PoStatus.Approved or PoStatus.Sent))
            return (null, "PO_STATE_ERROR", $"Only released POs take change orders (current: {po.Status}).");
        if (string.IsNullOrWhiteSpace(rationale))
            return (null, "VALIDATION_ERROR", "A rationale is required for every change order (052 AC3).");
        if (supplierId is { } && !suppliers.Exists(supplierId))
            return (null, "SUPPLIER_NOT_FOUND", "The change names a supplier that does not exist.");

        var before = Snapshot(po);
        var afterSupplier = supplierId ?? po.SupplierId;
        var afterLines = new List<PoLineSnapshot>();
        foreach (var l in po.Lines)
            afterLines.Add(new PoLineSnapshot(l.ItemSku, l.Qty, l.UnitPrice));
        foreach (var input in lines ?? [])
        {
            var match = afterLines.FirstOrDefault(x => x.ItemSku == input.ItemSku);
            if (match is null)
                return (null, "VALIDATION_ERROR", $"Line '{input.ItemSku}' is not on this purchase order.");
            afterLines.Remove(match);
            afterLines.Add(new PoLineSnapshot(input.ItemSku, input.Qty ?? match.Qty, input.UnitPrice ?? match.UnitPrice));
        }
        var after = new PoSnapshot(afterSupplier, afterLines.OrderBy(x => x.ItemSku).ToList());
        // line records compare by value — any qty/price/supplier drift is material (052 AC1)
        var isMaterial = afterSupplier != po.SupplierId || !after.Lines.SequenceEqual(before.Lines);
        if (!isMaterial)
            return (null, "VALIDATION_ERROR", "No material field change requested; description-only corrections are not a change order.");

        var changeNo = orders.ChangesFor(poId).Count + 1;
        var change = new PurchaseOrderChange($"{poId}:C{changeNo}", ec.TenantId, ec.EntityId, poId, changeNo,
            PoChangeStatus.Pending, JsonSerializer.Serialize(before, JsonOpts), JsonSerializer.Serialize(after, JsonOpts),
            rationale.Trim(), true, po.Version, u.Id, DateTimeOffset.UtcNow);
        orders.AddChange(change);
        audit.Log(u, "request-change", "purchase_order", poId,
            $"Change order {change.Id} requested ({(supplierId is null ? "lines" : "supplier+lines")}) on v{po.Version}: {rationale}");
        orders.Save();
        return (change, null, null);
    }

    public (PurchaseOrderChange? Change, string? Code, string? Message) Decide(
        CurrentUser actor, string poId, string changeId, bool approve)
    {
        var change = orders.FindChange(poId, changeId);
        if (change is null) return (null, "PO_CHANGE_NOT_FOUND", "Change order not found.");
        if (change.Status is not PoChangeStatus.Pending)
            return (null, "PO_CHANGE_STATE_ERROR", "Only pending change orders can be decided.");
        if (actor.Id == change.RequestedBy)
            return (null, "PO_CHANGE_APPROVAL_ERROR", "Maker-checker: the requester cannot decide their own change order.");
        if (!actor.Has("po:approve"))
            return (null, "FORBIDDEN", "po:approve permission required.");

        var po = orders.FindPo(poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");

        if (approve)
        {
            // 052 AC1: the after-snapshot becomes the order's new state; version bumps so supplier
            // acks (053) and dispatches (055) pin to the new version
            var after = JsonSerializer.Deserialize<PoSnapshot>(change.AfterJson, JsonOpts);
            if (after is null) return (null, "PO_CHANGE_STATE_ERROR", "Change snapshot is unreadable.");
            if (after.SupplierId != po.SupplierId) orders.SetField(po, nameof(PurchaseOrder.SupplierId), after.SupplierId);
            foreach (var l in after.Lines)
            {
                var line = po.Lines.FirstOrDefault(x => x.ItemSku == l.ItemSku);
                if (line is null) continue;
                if (line.Qty != l.Qty) orders.SetField(line, nameof(PoLine.Qty), l.Qty);
                if (line.UnitPrice != l.UnitPrice) orders.SetField(line, nameof(PoLine.UnitPrice), l.UnitPrice);
            }
            orders.SetField(po, nameof(PurchaseOrder.Version), po.Version + 1);
            audit.Emit(actor, "procurement.po.change.applied", new { poId, changeId, newVersion = po.Version + 1 });
        }
        orders.SetField(change, nameof(PurchaseOrderChange.Status), approve ? PoChangeStatus.Approved : PoChangeStatus.Rejected);
        orders.SetField(change, nameof(PurchaseOrderChange.ApprovedBy), actor.Id);
        orders.SetField(change, nameof(PurchaseOrderChange.ApprovedAt), DateTimeOffset.UtcNow);
        audit.Log(actor, approve ? "approve-change" : "reject-change", "purchase_order_change", changeId,
            $"Change order {changeId} {(approve ? "approved and applied" : "rejected")} by {actor.Email}");
        orders.Save();
        return (change, null, null);
    }

    public object Changes(string poId) => orders.ChangesFor(poId).Select(c => new
    {
        c.Id, c.PoId, c.ChangeNo, c.Status, c.Rationale, c.IsMaterial, c.BaseOrderVersion,
        before = JsonDocument.Parse(c.BeforeJson).RootElement.Clone(),
        after = JsonDocument.Parse(c.AfterJson).RootElement.Clone(),
        c.RequestedBy, c.RequestedAt, c.ApprovedBy, c.ApprovedAt,
    }).ToList();

    public (PurchaseOrderAcknowledgement? Ack, string? Code, string? Message) Acknowledge(
        CurrentUser u, string poId, string? supplierId, int orderVersion, IReadOnlyList<PoAckLineInput>? lines)
    {
        var po = orders.FindPo(poId);
        if (po is null) return (null, "PO_NOT_FOUND", "Purchase order not found.");
        if (string.IsNullOrWhiteSpace(supplierId) || supplierId != po.SupplierId)
            return (null, "VALIDATION_ERROR", "The acknowledgement must come from the order's supplier.");
        if (orderVersion <= 0 || orderVersion > po.Version)
            return (null, "VALIDATION_ERROR", $"Order version must be between 1 and the current version ({po.Version}).");

        var status = orderVersion == po.Version ? AckStatus.Current : AckStatus.Superseded;
        var hasDiscrepancy = (lines ?? []).Any(l => !string.Equals(l.Status, "Accepted", StringComparison.OrdinalIgnoreCase));
        var ack = new PurchaseOrderAcknowledgement($"{poId}:{supplierId}:A{orderVersion}", ec.TenantId, ec.EntityId,
            poId, orderVersion, supplierId, status,
            JsonSerializer.Serialize(lines ?? [], JsonOpts), hasDiscrepancy, u.Id, DateTimeOffset.UtcNow);
        if (orders.FindAck(poId, supplierId, orderVersion) is not null)
            return (null, "PO_ACK_STATE_ERROR", $"Supplier {supplierId} already acknowledged version {orderVersion}.");
        orders.AddAck(ack);
        audit.Log(u, "acknowledge", "purchase_order", poId,
            $"Supplier ack for {poId} v{orderVersion} recorded ({status}{(hasDiscrepancy ? ", discrepancy flagged" : "")})");
        if (hasDiscrepancy)
            audit.Emit(u, "procurement.po.ack.discrepancy", new { poId, orderVersion, supplierId, ackId = ack.Id }); // buyer task, no mutation
        orders.Save();
        return (ack, null, null);
    }

    public bool HasPendingChange(string poId) => orders.HasPendingChange(poId);

    public object? PoDetail(string poId)
    {
        var po = orders.FindPo(poId);
        return po is null ? null : new
        {
            po.Id, po.SupplierId, po.Status, po.Version, po.CreatedBy, po.CreatedAt,
            po.ApprovedBy, po.ApprovedAt, po.RejectionReason,
            lines = po.Lines.Select(l => new { l.Id, l.ItemSku, l.Description, l.Qty, l.UnitPrice }).ToList(),
            total = po.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
        };
    }

    private static PoSnapshot Snapshot(PurchaseOrder po) => new(po.SupplierId,
        po.Lines.Select(l => new PoLineSnapshot(l.ItemSku, l.Qty, l.UnitPrice)).OrderBy(x => x.ItemSku).ToList());

    private sealed record PoSnapshot(string SupplierId, List<PoLineSnapshot> Lines);
    private sealed record PoLineSnapshot(string ItemSku, decimal Qty, decimal UnitPrice);
}
