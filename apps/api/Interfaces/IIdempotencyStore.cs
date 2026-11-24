namespace Erp.Api.Interfaces;

// Task-2 §7 + review C1: stored HTTP responses keyed (tenant, entity, key) — replayed verbatim on
// repeat. Reserve/Complete bracket the business write inside ONE transaction (opened by Idem.Wrap),
// so the reservation, the business write and the stored response commit atomically — the crash
// window between "work done" and "key recorded" is gone.
// Task-5 (011 AC2): TryGet returns the row (hash included) and Reserve/Complete take the request
// payload hash; rows without a hash keep the replay-anyway semantics.
public interface IIdempotencyStore
{
    IdempotencyRecord? TryGet(string tenantId, string entityId, string key);

    /// Inserts a pending reservation (uncommitted). Throws IdempotencyKeyTakenException when a
    /// concurrent same-key request already committed — the caller replays the winner's response.
    void Reserve(string tenantId, string entityId, string key, string? requestHash = null);

    /// Replaces the pending reservation with the finished (status, body) — same transaction.
    void Complete(string tenantId, string entityId, string key, int status, string body, string? requestHash = null);
}

// C1: the key race was lost, which is not a failure — a concurrent same-key request committed
// first; the caller re-reads and replays the winner's stored response.
public sealed class IdempotencyKeyTakenException(string tenantId, string entityId, string key, Exception inner)
    : Exception($"Idempotency key '{key}' already taken for {tenantId}/{entityId}.", inner);
