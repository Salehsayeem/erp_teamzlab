namespace Erp.Api.Data;

// Task-2 §1: connection string name Default — assembled from PGHOST/PGPORT/PGDATABASE/PGUSER/
// PGPASSWORD into Host=…;Port=…;Database=…;Username=…;Password=…. An explicit
// ConnectionStrings:Default (set by the test fixtures) always wins.
public static class DbConfig
{
    public static string Assemble(Func<string, string?> get) =>
        $"Host={get("PGHOST") ?? "localhost"};Port={get("PGPORT") ?? "5432"};Database={get("PGDATABASE") ?? "erp_teamzlab"};Username={get("PGUSER") ?? "postgres"};Password={get("PGPASSWORD") ?? ""}";
}
