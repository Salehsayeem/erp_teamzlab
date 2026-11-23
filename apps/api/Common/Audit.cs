using Erp.Api.Data;

namespace Erp.Api;

// Cross-cutting audit/outbox writers — static by design: stateless helpers over Db (spec assumption #4)
public static class Audit
{
    public static void Log(Db db, CurrentUser user, string action, string entityType, string entityId, string summary) =>
        db.Audit.Add(new AuditEvent(db.NextDoc("A"), db.Tenant.Id, user.Id, action, entityType, entityId, DateTimeOffset.UtcNow, summary));

    public static void Emit(Db db, CurrentUser user, string type, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        db.Outbox.Add(new OutboxMessage(db.NextDoc("E"), db.Tenant.Id, type, 1, DateTimeOffset.UtcNow, user.Id, json));
    }
}
