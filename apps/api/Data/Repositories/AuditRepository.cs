using System.Text.Json;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Audit events + outbox messages — same writers, same payloads as the Task-1 static helpers.
// Audit + outbox rows are added in the same SaveChanges as the business change (task-2 §4).
public sealed class AuditRepository(ErpDbContext db, INumberSeries numbers, EntityContext ec) : IAuditRepository
{
    public List<AuditEvent> AllEvents() =>
        db.AuditEvents.Where(a => a.TenantId == ec.TenantId
                && EF.Property<string>(a, "LegalEntityId") == ec.EntityId) // LegalEntityId is a shadow column
            .OrderBy(a => a.Id).ToList();

    public List<OutboxMessage> AllOutbox() =>
        db.OutboxMessages.Where(o => o.TenantId == ec.TenantId
                && EF.Property<string>(o, "LegalEntityId") == ec.EntityId)
            .OrderBy(o => o.Id).ToList();

    public void Log(CurrentUser user, string action, string entityType, string entityId, string summary)
    {
        var evt = new AuditEvent(numbers.Next("A"), ec.TenantId, user.Id, action, entityType, entityId, DateTimeOffset.UtcNow, summary);
        SetEntityScope(evt);
        db.AuditEvents.Add(evt);
    }

    public void Emit(CurrentUser user, string type, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var msg = new OutboxMessage(numbers.Next("E"), ec.TenantId, type, 1, DateTimeOffset.UtcNow, user.Id, json);
        SetEntityScope(msg);
        db.OutboxMessages.Add(msg);
    }

    public int Save() => db.SaveChanges();

    private void SetEntityScope(object row) =>
        db.Entry(row).Property("LegalEntityId").CurrentValue = ec.EntityId; // shadow column — records hold TenantId only
}
