using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// P3 blanket orders (054 — highest race risk in the wave): the ceiling re-check and the commit
// happen inside ONE transaction holding the blanket row (SELECT … FOR UPDATE — the pattern proven
// on sourcing lot ceilings), so concurrent releases serialize and the loser conflicts.
public sealed class BlanketOrderService(
    IBlanketOrderRepository blankets, ISupplierRepository suppliers, INumberSeries numbers,
    IAuditRepository audit, ErpDbContext db, EntityContext ec) : IBlanketOrderService
{
    public (BlanketOrder? Order, string? Code, string? Message) Create(
        CurrentUser u, string? supplierId, string? currency, decimal ceilingAmount, DateOnly validFrom, DateOnly validTo)
    {
        if (!suppliers.Exists(supplierId ?? ""))
            return (null, "SUPPLIER_NOT_FOUND", "Supplier does not exist.");
        if (ceilingAmount <= 0)
            return (null, "VALIDATION_ERROR", "Ceiling amount must be greater than 0.");
        if (validTo <= validFrom)
            return (null, "VALIDATION_ERROR", "Validity must end after it starts.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var status = validTo < today ? BlanketStatus.Expired : BlanketStatus.Active;
        var order = new BlanketOrder(numbers.Next("BKO"), ec.TenantId, ec.EntityId, supplierId!,
            string.IsNullOrWhiteSpace(currency) ? "BDT" : currency, ceilingAmount, 0m, validFrom, validTo,
            status, 1, u.Id, DateTimeOffset.UtcNow);
        blankets.Add(order);
        audit.Log(u, "create", "blanket_order", order.Id,
            $"Blanket order for {supplierId}: ceiling {ceilingAmount:0.##} {order.Currency}, valid {validFrom:yyyy-MM-dd}..{validTo:yyyy-MM-dd} ({status})");
        blankets.Save();
        return (order, null, null);
    }

    public (BlanketRelease? Release, string? Code, string? Message) Release(
        CurrentUser u, string blanketOrderId, decimal amount, string? poId)
    {
        var order = blankets.Find(blanketOrderId);
        if (order is null) return (null, "BLANKET_NOT_FOUND", "Blanket order not found.");
        if (amount <= 0)
            return (null, "VALIDATION_ERROR", "Release amount must be greater than 0.");

        // serialize per blanket — the expiry/ceiling checks and the debit commit under one row lock
        using var tx = db.Database.BeginTransaction();
        if (!LockBlanketRow(blanketOrderId))
            return (null, "BLANKET_NOT_FOUND", "Blanket order not found.");
        order = blankets.Find(blanketOrderId)!; // authoritative re-read under the lock
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (order.Status is BlanketStatus.Expired or BlanketStatus.Closed || order.ValidTo < today)
        {
            tx.Rollback();
            return (null, "BLANKET_EXPIRED",
                $"Blanket order {blanketOrderId} expired {order.ValidTo:yyyy-MM-dd}; new releases are blocked (054 AC3).");
        }
        if (order.ReleasedAmount + amount > order.CeilingAmount)
        {
            tx.Rollback();
            return (null, "BLANKET_CEILING_CONFLICT",
                $"Ceiling exceeded: {order.ReleasedAmount:0.##} already released + {amount:0.##} > {order.CeilingAmount:0.##} (054 AC1).");
        }

        var release = new BlanketRelease(numbers.Next("BLR"), ec.TenantId, ec.EntityId, blanketOrderId,
            amount, poId, u.Id, DateTimeOffset.UtcNow);
        blankets.AddRelease(release);
        blankets.SetField(order, nameof(BlanketOrder.ReleasedAmount), order.ReleasedAmount + amount);
        blankets.SetField(order, nameof(BlanketOrder.Version), order.Version + 1);
        audit.Log(u, "release", "blanket_order", blanketOrderId,
            $"Release {release.Id}: {amount:0.##} {order.Currency} debited against ceiling ({order.ReleasedAmount + amount:0.##}/{order.CeilingAmount:0.##})");
        blankets.Save();
        tx.Commit();
        return (release, null, null);
    }

    public object? Detail(string id)
    {
        var order = blankets.Find(id);
        if (order is null) return null;
        var releases = blankets.ReleasesFor(id);
        return new
        {
            order.Id, order.SupplierId, order.Currency, order.CeilingAmount, order.ReleasedAmount,
            order.ValidFrom, order.ValidTo, order.Status, order.Version, order.CreatedBy, order.CreatedAt,
            remaining = order.CeilingAmount - order.ReleasedAmount,
            releases = releases.Select(r => new { r.Id, r.Amount, r.PoId, r.ReleasedBy, r.ReleasedAt }).ToList(),
        };
    }

    private bool LockBlanketRow(string id) =>
        db.Database.SqlQuery<string>($"SELECT id AS \"Value\" FROM blanket_orders WHERE id = {id} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId} FOR UPDATE")
            .SingleOrDefault() is not null;
}
