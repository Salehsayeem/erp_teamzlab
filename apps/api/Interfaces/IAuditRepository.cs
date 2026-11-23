namespace Erp.Api.Interfaces;

// Audit events + outbox messages (they used to be the static Common/Audit.cs helpers)
public interface IAuditRepository
{
    List<AuditEvent> AllEvents();
    List<OutboxMessage> AllOutbox();
    void Log(CurrentUser user, string action, string entityType, string entityId, string summary);
    void Emit(CurrentUser user, string type, object payload);
    int Save();
}
