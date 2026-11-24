using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Erp.Api.Tests;

// T2 review finding C1 (Important): the idempotency row committed in a SECOND SaveChanges after
// the business write. Two same-key requests could both pass TryGet before either stored a row →
// the business write ran twice AND the loser crashed with a raw unique-violation 500. The fix
// reserves the key inside the same transaction as the business write, so the race resolves to
// replays of the winner's response — never duplicate writes, never 500s.
public class IdempotencyConcurrencyTests : IDisposable
{
    private readonly TestApiFactory _factory;
    public IdempotencyConcurrencyTests() => _factory = new TestApiFactory();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Concurrent_same_key_creates_execute_once_and_all_receive_the_same_response()
    {
        const int concurrent = 10;
        var key = $"idem-conc-{Guid.NewGuid():N}";
        var taxId = $"TRX-CONC-{Guid.NewGuid():N}";

        var tasks = Enumerable.Range(0, concurrent).Select(async _ =>
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer U-002");
            client.DefaultRequestHeaders.Add("Idempotency-Key", key);
            var res = await client.PostAsJsonAsync("/api/v1/suppliers",
                new { name = "Conc Co", taxId, email = "conc@sup.test" });
            var body = await res.Content.ReadAsStringAsync();
            return (Status: res.StatusCode, Body: body);
        });
        var results = (await Task.WhenAll(tasks)).ToList();

        // no request crashes: the key race must resolve to replays, not raw 500s
        Assert.All(results, r => Assert.True(
            (int)r.Status is >= 200 and < 300, $"got {r.Status}: {r.Body}"));

        // every caller sees the winner's stored response, byte-identical
        var distinctBodies = results.Select(r => r.Body).Distinct(StringComparer.Ordinal).ToList();
        var winnerBody = Assert.Single(distinctBodies);
        var winnerId = JsonDocument.Parse(winnerBody).RootElement
            .GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(winnerId));
        Assert.All(results, r => Assert.Contains(winnerId, r.Body));

        // the business write happened exactly once — one supplier row in PostgreSQL
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM suppliers WHERE tax_id = $1", conn);
        cmd.Parameters.AddWithValue(taxId);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Stored_response_survives_and_replays_after_the_reserving_transaction_committed()
    {
        // sequential replay still works through the reserve/complete path (C1 fix must not
        // regress ReviewFixTests.Idempotency_key_replays_receipt_without_second_write semantics)
        var key = $"idem-seq-{Guid.NewGuid():N}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer U-002");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var first = await client.PostAsJsonAsync("/api/v1/suppliers",
            new { name = "Seq Co", taxId = "TRX-SEQ", email = "seq@sup.test" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();

        // the reservation row committed with the finished response — a fresh context replays it
        var replay = await client.PostAsJsonAsync("/api/v1/suppliers",
            new { name = "Seq Co OTHER BODY", taxId = "TRX-SEQ", email = "seq@sup.test" });
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync());
    }
}
