namespace Erp.Api;

public record AuditEvent(string Id, string TenantId, string ActorUserId, string Action, string EntityType, string EntityId, DateTimeOffset At, string Summary);

public record OutboxMessage(string Id, string TenantId, string Type, int Version, DateTimeOffset OccurredAt, string ActorUserId, string PayloadJson);

// Task-2 §7: Idempotency-Key → stored response (tenant+entity+key unique) replayed verbatim on repeat
public record IdempotencyRecord(string TenantId, string LegalEntityId, string Key, int Status, string Body, DateTimeOffset CreatedAt);
