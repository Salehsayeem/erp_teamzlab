using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Returns/RMA (ERP-SALES-061): authorizations capped at sold − prior returns; received
// tracked separately from authorized; receipts bring stock back via the existing ApplyStock
// semantics (Q-S4: no formal reservation contract).
public sealed class SalesReturnService(
    ISalesOrderRepository orders, ISalesDeferredRepository deferred, IInventoryRepository inventory,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : ISalesReturnService
{
    public (object? data, string? code, string? message) Create(CurrentUser u, string orderId, IReadOnlyList<Dtos.ReturnLineInput> lines, string? overrideJustification)
    {
        var so = orders.Find(orderId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (lines is null || lines.Count == 0)
            return (null, "VALIDATION_ERROR", "At least one return line is required.");
        if (!u.Has("returns:write")) return (null, "FORBIDDEN", "returns:write permission required.");
        var sold = orders.ShippedBySku(orderId);

        var priorBySku = deferred.ReturnsForOrder(orderId)
            .SelectMany(r => r.Lines).Where(l => l.Qty > 0)
            .GroupBy(l => l.ItemSku).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var newLines = new List<ReturnLine>();
        var needsOverride = false;
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l.ItemSku)) return (null, "VALIDATION_ERROR", "Each return line needs an item SKU.");
            if (l.Qty <= 0) return (null, "VALIDATION_ERROR", "Return qty must be greater than 0.");
            var soldQty = sold.TryGetValue(l.ItemSku, out var s) ? s : 0;
            if (soldQty == 0)
                return (null, "RETURN_STATE_ERROR", $"Nothing was ever shipped for {l.ItemSku} on this order.");
            var prior = priorBySku.GetValueOrDefault(l.ItemSku);
            // 061 AC1: authorized ≤ sold − prior returns unless an approved override
            if (prior + l.Qty > soldQty) needsOverride = true;
            newLines.Add(new ReturnLine(l.ItemSku, l.Qty, l.Reason ?? "not given", l.Condition ?? "Resellable"));
        }
        if (needsOverride)
        {
            if (string.IsNullOrWhiteSpace(overrideJustification))
                return (null, "RETURN_QTY_EXCEEDED", "Return exceeds sold − prior authorized returns; an override justification and a non-maker so:approve officer are required.");
            // the override officer must hold so:approve and not be the order's maker
            if (!u.Has("so:approve"))
                return (null, "FORBIDDEN", "Return override requires so:approve.");
            if (u.Id == so.CreatedBy)
                return (null, "RETURN_QTY_EXCEEDED", "Return override requires an approver who is not the order's maker.");
            audit.Log(u, "return_override", "sales_return", orderId, overrideJustification);
        }

        var ret = new SalesReturn(numbers.Next("RMA"), ec.TenantId, ec.EntityId, orderId, so.CustomerId,
            ReturnStatus.Authorized, overrideJustification ?? "customer return", u.Id, DateTimeOffset.UtcNow, newLines);
        deferred.AddReturn(ret);
        audit.Log(u, "authorize", "sales_return", ret.Id, $"Authorized {newLines.Count} line(s) against {orderId}");
        deferred.Save();
        return (View(ret), null, null);
    }

    // 061 AC2: received recorded separately from authorized; stock returns via ApplyStock
    public (object? data, string? code, string? message) Receive(CurrentUser u, string returnId, decimal receivedQty)
    {
        var ret = deferred.FindReturn(returnId);
        if (ret is null) return (null, "RETURN_NOT_FOUND", "Sales return not found.");
        if (!u.Has("returns:write")) return (null, "FORBIDDEN", "returns:write permission required.");
        if (receivedQty <= 0) return (null, "VALIDATION_ERROR", "Received qty must be greater than 0.");
        var authorized = ret.Lines.Sum(l => l.Qty);
        var alreadyReceived = deferred.ReceiptsFor(returnId).Sum(r => r.ReceivedQty);
        if (alreadyReceived + receivedQty > authorized)
            return (null, "RETURN_RECEIPT_EXCEEDED", $"Received exceeds authorized: authorized {authorized:0.##}, already received {alreadyReceived:0.##}, requested {receivedQty:0.##}.");

        var receipt = new ReturnReceipt($"{returnId}:R{deferred.ReceiptsFor(returnId).Count + 1}",
            ec.TenantId, ec.EntityId, returnId, receivedQty, u.Id, DateTimeOffset.UtcNow);
        deferred.AddReturnReceipt(receipt);
        // restock at the default receiving location (DrawStock drains LOC-A1 first, so this
        // mirrors the outbound flow; Q-S4: plain ApplyStock semantics, no reservation contract)
        foreach (var line in ret.Lines)
            inventory.ApplyStock("WH-001", "LOC-A1", line.ItemSku, receivedQty * line.Qty / authorized);

        var totalReceived = alreadyReceived + receivedQty;
        deferred.SetReturnStatus(ret, totalReceived >= authorized ? ReturnStatus.Completed : ReturnStatus.PartiallyReceived);
        audit.Log(u, "receive", "sales_return", returnId, $"Received {receivedQty:0.##} of {authorized:0.##} authorized");
        audit.Emit(u, "sales.return.received", new { returnId, receivedQty, totalReceived, authorized });
        deferred.Save();
        return (View(deferred.FindReturn(returnId)!), null, null);
    }

    public (object? data, string? code, string? message) Detail(string returnId)
    {
        var ret = deferred.FindReturn(returnId);
        if (ret is null) return (null, "RETURN_NOT_FOUND", "Sales return not found.");
        return (View(ret), null, null);
    }

    public (object? data, string? code, string? message) List() =>
        (deferred.Returns().Select(View).ToList(), null, null);

    private object View(SalesReturn ret) => new
    {
        ret.Id, ret.SalesOrderId, ret.CustomerId, ret.Status, ret.Reason, ret.CreatedBy, ret.CreatedAt,
        lines = ret.Lines.Select(l => new { l.ItemSku, l.Qty, l.Reason, l.Condition }),
        authorizedQty = ret.Lines.Sum(l => l.Qty),
        receivedQty = deferred.ReceiptsFor(ret.Id).Sum(r => r.ReceivedQty),
        receipts = deferred.ReceiptsFor(ret.Id).Select(r => new { r.Id, r.ReceivedQty, r.ReceivedBy, r.ReceivedAt }),
    };
}
