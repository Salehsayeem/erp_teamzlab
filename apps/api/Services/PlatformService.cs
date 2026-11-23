using Erp.Api.Interfaces;
using Erp.Api.Data;

namespace Erp.Api.Services;

public sealed class PlatformService(Db db) : IPlatformService
{
    public object AuditEvents() => db.WithLock(() => db.Audit);

    public object Outbox() => db.WithLock(() => db.Outbox); // dev visibility only
}
