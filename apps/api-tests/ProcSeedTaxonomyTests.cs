using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Erp.Api.Tests;

// Fresh-DB seed gap (click-through fu-2): a brand-new database (migrations only, no dev-DB
// extras) must carry the taxonomy a requisition submission consumes — the "EA" purchasing
// unit the web form defaults to, plus at least one active procurement category. This class
// owns its throwaway DB via the class fixture, so nothing but seeds can satisfy these tests.
public class ProcSeedTaxonomyTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public ProcSeedTaxonomyTests(TestApiFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.PostAsJsonAsync(path, body);
    }

    private async Task<JsonElement> GetAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        var res = await client.GetAsync(path);
        Assert.True(res.IsSuccessStatusCode, $"{path} → {(int)res.StatusCode}");
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Fresh_db_seeds_ea_unit_and_active_category_so_default_requisition_submits()
    {
        var units = await GetAsync("/api/v1/unit-of-measures", "U-002");
        var ea = units.EnumerateArray().FirstOrDefault(u => u.GetProperty("code").GetString() == "EA");
        Assert.False(ea.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined,
            "fresh DB must seed the EA purchasing unit (web requisition form defaults to it)");
        Assert.True(ea.GetProperty("isBase").GetBoolean());
        Assert.Equal(0, ea.GetProperty("precision").GetInt32());

        var cats = await GetAsync("/api/v1/procurement-categories", "U-002");
        Assert.True(cats.GetArrayLength() >= 1, "fresh DB must seed at least one procurement category");
        var cat = cats.EnumerateArray().First(c => c.GetProperty("status").GetString() == "Active");

        var create = await PostAsync("/api/v1/requisitions", new
        {
            purpose = "seed probe",
            currency = "BDT",
            lines = new[]
            {
                new
                {
                    categoryId = cat.GetProperty("id").GetString(),
                    description = "seed probe line",
                    qty = 2m,
                    unitPrice = 10m,
                    unit = "EA",
                    needBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                },
            },
        }, "U-002");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        await using var createStream = await create.Content.ReadAsStreamAsync();
        using var createDoc = await JsonDocument.ParseAsync(createStream);
        var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var submit = await PostAsync($"/api/v1/requisitions/{id}/submission", new { }, "U-002");
        Assert.True(submit.IsSuccessStatusCode,
            $"submission on a fresh DB → {(int)submit.StatusCode}: {await submit.Content.ReadAsStringAsync()}");

        var detail = await GetAsync($"/api/v1/requisitions/{id}", "U-002");
        var line = detail.GetProperty("lines")[0];
        Assert.Equal("EA", line.GetProperty("baseUnit").GetString()); // EA is base — normalizes to itself
        Assert.Equal(2m, line.GetProperty("baseQty").GetDecimal());
    }

    // Click-through fu-7: the requisitions list DTO omitted line quantities, so the web Qty
    // column rendered a permanent "—" — list rows now carry totalQty (sum of line qty).
    [Fact]
    public async Task Requisition_list_rows_carry_totalQty_sum_of_line_qty()
    {
        var cats = await GetAsync("/api/v1/procurement-categories", "U-002");
        var cat = cats.EnumerateArray().First(c => c.GetProperty("status").GetString() == "Active");
        var needBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        var create = await PostAsync("/api/v1/requisitions", new
        {
            purpose = "qty summary probe",
            currency = "BDT",
            lines = new[]
            {
                new { categoryId = cat.GetProperty("id").GetString(), description = "line one", qty = 2m, unitPrice = 10m, unit = "EA", needBy },
                new { categoryId = cat.GetProperty("id").GetString(), description = "line two", qty = 3m, unitPrice = 5m, unit = "EA", needBy },
            },
        }, "U-002");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        await using var createStream = await create.Content.ReadAsStreamAsync();
        using var createDoc = await JsonDocument.ParseAsync(createStream);
        var id = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var list = await GetAsync("/api/v1/requisitions", "U-002");
        var row = list.EnumerateArray().First(r => r.GetProperty("id").GetString() == id);
        Assert.Equal(5m, row.GetProperty("totalQty").GetDecimal());
        Assert.Equal(35m, row.GetProperty("total").GetDecimal());
    }
}
