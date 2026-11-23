using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Erp.Api.Tests;

// Task-2 acceptance #6: proof the store is PostgreSQL, not memory. A supplier written through one
// factory instance must survive a NEW WebApplicationFactory pointed at the same throwaway database,
// and a raw Npgsql connection must be able to read the suppliers table directly. This test fails
// against the in-memory Db.I singleton: nothing reaches PostgreSQL.
public class PersistenceTests : IDisposable
{
    private readonly TestApiFactory _factory;
    public PersistenceTests() => _factory = new TestApiFactory();
    public void Dispose() => _factory.Dispose();

    private async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer U-002");
        return await client.PostAsJsonAsync(path, body);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Supplier_survives_a_new_factory_instance_pointed_at_the_same_database()
    {
        var res = await PostAsync("/api/v1/suppliers", new { name = "Persist Co", taxId = "TRX-PERSIST-1", email = "persist@sup.test" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // a brand-new WebApplicationFactory instance on the SAME database still serves the row
        using var second = TestApiFactory.OnDatabase(_factory.DatabaseName);
        var client = second.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer U-002");
        var list = await Json(await client.GetAsync("/api/v1/suppliers"));
        Assert.Contains(list.GetProperty("data").EnumerateArray(),
            s => s.GetProperty("id").GetString() == supplierId);

        // the row really lives in PostgreSQL — a raw connection reads the suppliers table directly
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM suppliers WHERE id = $1", conn);
        cmd.Parameters.AddWithValue(supplierId);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }
}
