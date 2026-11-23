namespace Erp.Api.Data;

// In-memory singleton store + seed (tenant T-001, entity LE-001, 3 users, pilot reference data).
// shortcut: whole store is volatile, upgrade path = EF Core repositories behind the same services

public sealed class Db
{
    public static readonly Db I = new();

    public Tenant Tenant { get; } = new("T-001", "TeamZlab Demo");
    public LegalEntity Entity { get; } = new("LE-001", "T-001", "TeamZlab Ltd (BD)", "BD", "BDT", "en-US");

    public List<User> Users { get; } = [];
    public List<Role> Roles { get; } = [];
    public List<Membership> Memberships { get; } = [];
    public List<Supplier> Suppliers { get; } = [];
    public List<PurchaseOrder> PurchaseOrders { get; } = [];
    public List<Receipt> Receipts { get; } = [];
    public List<StockLevel> Stock { get; } = [];
    public List<Journal> Journals { get; } = [];
    public List<AuditEvent> Audit { get; } = [];
    public List<OutboxMessage> Outbox { get; } = [];

    private readonly object _lock = new();
    private int _seq;

    private Db() => Seed();

    public T WithLock<T>(Func<T> action) { lock (_lock) return action(); }
    public void WithLock(Action action) { lock (_lock) action(); }

    public string NextDoc(string prefix) => WithLock(() => $"{prefix}-{++_seq:D4}");

    private void Seed()
    {
        Roles.AddRange(
            new Role("R-ADMIN", "tenant_admin", ["*"]),
            new Role("R-PROC", "procurement_officer", ["suppliers:write", "po:write", "receipts:write"]),
            new Role("R-FIN", "finance_officer", ["journals:read", "journals:approve", "ap:read"]));

        Users.AddRange(
            new User("U-001", "admin@teamzlab.dev", "Tenant Admin", true),
            new User("U-002", "procurve@teamzlab.dev", "Procure Officer", true),
            new User("U-003", "finance@teamzlab.dev", "Finance Officer", true));

        Memberships.AddRange(Users.Select(u => new Membership(u.Id, Tenant.Id, Entity.Id, u.Id switch
        {
            "U-001" => "R-ADMIN",
            "U-002" => "R-PROC",
            _ => "R-FIN",
        })));

        Audit.Add(new AuditEvent("A-0001", Tenant.Id, "system", "seed", "tenant", Tenant.Id,
            DateTimeOffset.UtcNow, "Seeded demo tenant, entity, users, roles"));
    }
}
