namespace Erp.Api;

public record AuditEvent(string Id, string TenantId, string ActorUserId, string Action, string EntityType, string EntityId, DateTimeOffset At, string Summary);

public record OutboxMessage(string Id, string TenantId, string Type, int Version, DateTimeOffset OccurredAt, string ActorUserId, string PayloadJson);

// Task-2 §7: Idempotency-Key → stored response (tenant+entity+key unique) replayed verbatim on repeat.
// Task-5 (011 AC2): RequestHash (nullable, SHA-256 of the request payload) — when both sides carry a
// hash and they differ, the same key returns 409 IDEMPOTENCY_CONFLICT instead of replaying. Rows
// written before this column existed (hash null) keep the replay-anyway behavior.
public record IdempotencyRecord(string TenantId, string LegalEntityId, string Key, int Status, string Body, DateTimeOffset CreatedAt, string? RequestHash = null);
