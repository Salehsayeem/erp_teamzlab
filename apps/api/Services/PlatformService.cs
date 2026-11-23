using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class PlatformService(IAuditRepository audit) : IPlatformService
{
    public object AuditEvents() => audit.AllEvents();

    public object Outbox() => audit.AllOutbox(); // dev visibility only
}
