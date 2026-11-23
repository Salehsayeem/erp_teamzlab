namespace Erp.Api.Data;

// In-memory singleton store + seed (tenant T-001, entity LE-001, 4 users, pilot reference data).
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
    public List<Customer> Customers { get; } = [];
    public List<SalesOrder> SalesOrders { get; } = [];
    public List<Shipment> Shipments { get; } = [];
    public List<Warehouse> Warehouses { get; } = [];
    public List<StockLocation> Locations { get; } = [];
    public List<StockLevel> Stock { get; } = [];
    public List<StockTransfer> Transfers { get; } = [];
    public List<StockCount> Counts { get; } = [];
    public List<StockAdjustment> Adjustments { get; } = [];
    public List<Journal> Journals { get; } = [];
    public List<AuditEvent> Audit { get; } = [];
    public List<OutboxMessage> Outbox { get; } = [];

    // finding 3: Idempotency-Key → stored response (tenant+entity scoped) replayed verbatim on repeat
    public Dictionary<string, (int Status, string Body)> Idempotency { get; } = [];

    private readonly object _lock = new();
    private int _seq;

    private Db() => Seed();

    public T WithLock<T>(Func<T> action) { lock (_lock) return action(); }
    public void WithLock(Action action) { lock (_lock) action(); }

    public string NextDoc(string prefix) => WithLock(() => $"{prefix}-{++_seq:D4}");

    // receipts land here (seed order = [A1, B1])
    public StockLocation DefaultLocation => Locations[0];

    private void Seed()
    {
        Roles.AddRange(
            new Role("R-ADMIN", "tenant_admin", ["*"]),
            new Role("R-PROC", "procurement_officer", ["suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer"]),
            new Role("R-FIN", "finance_officer", ["journals:read", "journals:approve", "ap:read", "po:approve"]),
            new Role("R-SALES", "sales_officer", ["customers:write", "so:write", "shipments:write"]));

        Users.AddRange(
            new User("U-001", "admin@teamzlab.dev", "Tenant Admin", true),
            new User("U-002", "procurve@teamzlab.dev", "Procure Officer", true),
            new User("U-003", "finance@teamzlab.dev", "Finance Officer", true),
            new User("U-004", "sales@teamzlab.dev", "Sales Officer", true));

        Memberships.AddRange(Users.Select(u => new Membership(u.Id, Tenant.Id, Entity.Id, u.Id switch
        {
            "U-001" => "R-ADMIN",
            "U-002" => "R-PROC",
            "U-004" => "R-SALES",
            _ => "R-FIN",
        })));

        Audit.Add(new AuditEvent("A-0001", Tenant.Id, "system", "seed", "tenant", Tenant.Id,
            DateTimeOffset.UtcNow, "Seeded demo tenant, entity, users, roles"));

        Warehouses.Add(new Warehouse("WH-001", Tenant.Id, Entity.Id, "WH-001", "Main Warehouse"));
        Locations.AddRange(
            new StockLocation("LOC-A1", Tenant.Id, Entity.Id, "WH-001", "A1", "Rack A1"),
            new StockLocation("LOC-B1", Tenant.Id, Entity.Id, "WH-001", "B1", "Rack B1"));
    }
}
