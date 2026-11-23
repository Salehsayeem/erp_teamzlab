namespace Erp.Api.Interfaces;

public interface IPlatformService
{
    object AuditEvents();
    object Outbox();
}
