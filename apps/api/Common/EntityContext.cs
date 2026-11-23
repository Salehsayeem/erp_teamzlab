namespace Erp.Api;

// Task-2 §3: scoped tenant/legal-entity accessor fed from HttpContext (AuthMiddleware).
// Every repository query filters TenantId/EntityId from here — server-side scoping stays
// (CLAUDE.md security rule). Auth paths that skip the user check keep the seed scope.
public sealed class EntityContext
{
    // Pilot seed scope — byte-identical to the Task-1 in-memory Db.Tenant / Db.Entity
    public const string SeedTenantId = "T-001";
    public const string SeedEntityId = "LE-001";
    public static readonly Tenant SeedTenant = new(SeedTenantId, "TeamZlab Demo");
    public static readonly LegalEntity SeedEntity = new(SeedEntityId, SeedTenantId, "TeamZlab Ltd (BD)", "BD", "BDT", "en-US");

    public string TenantId { get; private set; } = SeedTenantId;
    public string EntityId { get; private set; } = SeedEntityId;

    public void Set(string tenantId, string entityId)
    {
        TenantId = tenantId;
        EntityId = entityId;
    }
}
