using Erp.Api.Interfaces;

namespace Erp.Api.Data.Repositories;

// Task-2 §7: the idempotency table replaces the in-memory Db.Idempotency map. The row commits in
// its own SaveChanges after the business write — the response body/status are replayed verbatim.
public sealed class IdempotencyStore(ErpDbContext db) : IIdempotencyStore
{
    public (int Status, string Body)? TryGet(string tenantId, string entityId, string key)
    {
        var hit = db.Idempotency.Find(tenantId, entityId, key); // PK is (tenant_id, legal_entity_id, key)
        return hit is null ? null : (hit.Status, hit.Body);
    }

    public void Set(string tenantId, string entityId, string key, int status, string body)
    {
        db.Idempotency.Add(new IdempotencyRecord(tenantId, entityId, key, status, body, DateTimeOffset.UtcNow));
        db.SaveChanges();
    }
}
