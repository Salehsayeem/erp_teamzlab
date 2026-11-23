namespace Erp.Api;

public record AuditEvent(string Id, string TenantId, string ActorUserId, string Action, string EntityType, string EntityId, DateTimeOffset At, string Summary);

public record OutboxMessage(string Id, string TenantId, string Type, int Version, DateTimeOffset OccurredAt, string ActorUserId, string PayloadJson);
