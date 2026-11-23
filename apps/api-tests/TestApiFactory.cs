using Erp.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Api.Tests;

// Task-2 §5 test isolation: every test class gets its own uniquely-named throwaway PostgreSQL
// database (erp_test_<8-hex>) on the test server, so PG persistence cannot leak state across
// classes. The factory injects its connection string into the host via ConnectionStrings:Default;
// migrations are applied programmatically once ErpDbContext exists.
//
// Credentials: LOCAL test server only (localhost:5432, user postgres) — overridable via
// TESTPGHOST/TESTPGPORT/TESTPGUSER/TESTPGPASSWORD. The repo .env points at the remote Default
// server, which is unreachable and never targeted by tests. These databases are created and
// destroyed by tests only and never contain user data; nothing ever runs against Default.
public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

    private static readonly string Host = Env("TESTPGHOST", "localhost");
    private static readonly string Port = Env("TESTPGPORT", "5432");
    private static readonly string User = Env("TESTPGUSER", "postgres");
    private static readonly string Password = Env("TESTPGPASSWORD", "postgres");

    private static string AdminConnectionString =>
        $"Host={Host};Port={Port};Database=postgres;Username={User};Password={Password};Pooling=false";

    private readonly string _connectionString;
    private readonly bool _ownsDatabase;
    private bool _dropped;

    /// Self-owned fixture: creates a fresh erp_test_&lt;8hex&gt; database, drops it on dispose.
    /// Single public constructor — xUnit class fixtures may define only one.
    public TestApiFactory() : this(databaseName: null) { }

    /// Borrowing factory: reuses an existing throwaway database (created by another instance),
    /// never drops it. Used by the persistence proof to open a NEW factory on the SAME database.
    public static TestApiFactory OnDatabase(string databaseName) => new(databaseName);

    private TestApiFactory(string? databaseName)
    {
        DatabaseName = databaseName ?? $"erp_test_{Guid.NewGuid().ToString("N")[..8]}";
        _ownsDatabase = databaseName is null;
        _connectionString = $"Host={Host};Port={Port};Database={DatabaseName};Username={User};Password={Password}";
        TestDatabase.Create(AdminConnectionString, DatabaseName);
        Migrate();
    }

    public string DatabaseName { get; }
    public string ConnectionString => _connectionString;

    // task-2 §5: apply migrations programmatically to the throwaway test database only
    private void Migrate()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_connectionString).Options;
        using var ctx = new ErpDbContext(options);
        ctx.Database.Migrate();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", _connectionString);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Drop();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Drop();
    }

    private void Drop()
    {
        if (!_ownsDatabase || _dropped) return;
        _dropped = true;
        TestDatabase.Drop(AdminConnectionString, DatabaseName);
    }
}

// Dependency-free database lifecycle helpers for the throwaway test databases only.
internal static class TestDatabase
{
    public static void Create(string adminConnectionString, string name)
    {
        using var conn = new NpgsqlConnection(adminConnectionString);
        conn.Open();
        try { using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn); cmd.ExecuteNonQuery(); }
        catch (NpgsqlException ex) when (ex.SqlState == "42P04") { /* already exists — borrowing factory */ }
    }

    public static void Drop(string adminConnectionString, string name)
    {
        using var conn = new NpgsqlConnection(adminConnectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
        cmd.ExecuteNonQuery();
    }
}
