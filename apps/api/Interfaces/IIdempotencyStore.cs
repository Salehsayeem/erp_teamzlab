namespace Erp.Api.Interfaces;

// Task-2 §7: stored HTTP responses keyed (tenant, entity, key) — replayed verbatim on repeat
public interface IIdempotencyStore
{
    (int Status, string Body)? TryGet(string tenantId, string entityId, string key);
    void Set(string tenantId, string entityId, string key, int status, string body);
}
