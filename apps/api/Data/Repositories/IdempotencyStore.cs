using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Api.Data.Repositories;

// Task-2 §7 + review C1: reservation and completion run inside the caller's transaction, so the
// idempotency row commits atomically WITH the business write — no second-SaveChanges crash window.
public sealed class IdempotencyStore(ErpDbContext db) : IIdempotencyStore
{
    public (int Status, string Body)? TryGet(string tenantId, string entityId, string key)
    {
        var hit = db.Idempotency.Find(tenantId, entityId, key); // PK is (tenant_id, legal_entity_id, key)
        return hit is null ? null : (hit.Status, hit.Body);
    }

    public void Reserve(string tenantId, string entityId, string key)
    {
        // status 0 / empty body = pending; always replaced by Complete before the tx commits
        db.Idempotency.Add(new IdempotencyRecord(tenantId, entityId, key, 0, string.Empty, DateTimeOffset.UtcNow));
        try { db.SaveChanges(); }
        catch (DbUpdateException ex) when (ex.InnerException is NpgsqlException { SqlState: "23505" })
        {
            // PostgreSQL blocked this insert on the concurrent uncommitted same-key row; by the
            // time the violation surfaces, the winner has committed and is visible to replay.
            throw new IdempotencyKeyTakenException(tenantId, entityId, key, ex);
        }
    }

    public void Complete(string tenantId, string entityId, string key, int status, string body)
    {
        var pending = db.Idempotency.Find(tenantId, entityId, key);
        if (pending is not null) db.Idempotency.Remove(pending);
        db.Idempotency.Add(new IdempotencyRecord(tenantId, entityId, key, status, body, DateTimeOffset.UtcNow));
        db.SaveChanges(); // EF batches the same-PK delete before the insert
    }
}
