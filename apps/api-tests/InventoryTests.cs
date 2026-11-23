using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// Inventory deep slice: locations, transfers, counts with maker-checker, weighted-average valuation.
// Each test seeds its own SKU — Db.I is shared across the test process (sequential, no parallelization).

namespace Erp.Api.Tests;

public class InventoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public InventoryTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.PostAsJsonAsync(path, body);
    }

    private async Task<JsonElement> GetAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await Json(await client.GetAsync(path));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    // seeds supplier + approved PO + receipt so qty of the SKU sits in the default location LOC-A1
    private async Task<string> SeedStock(string sku, decimal qty, decimal unitPrice)
    {
        var proc = "U-002"; var admin = "U-001";
        var res = await PostAsync("/api/v1/suppliers", new { name = $"Sup {sku}", taxId = $"TRX-{sku}", email = $"{sku.ToLowerInvariant()}@sup.test" }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice } },
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, proc)).StatusCode);
        return poId;
    }

    private JsonElement StockRow(JsonElement stock, string sku, string locationCode) =>
        stock.GetProperty("data").EnumerateArray()
            .First(s => s.GetProperty("itemSku").GetString() == sku && s.GetProperty("locationCode").GetString() == locationCode);

    [Fact]
    public async Task Transfer_moves_stock_between_locations_and_blocks_overdraw()
    {
        var proc = "U-002"; var admin = "U-001";
        await SeedStock("TRF-T1", 10m, 5.00m);

        // happy path: 4 units A1 → B1
        var res = await PostAsync("/api/v1/stock/transfers", new
        {
            fromLocationId = "LOC-A1", toLocationId = "LOC-B1", itemSku = "TRF-T1", qty = 4m,
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("Posted", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        var stock = await GetAsync("/api/v1/stock", admin);
        Assert.Equal(6m, StockRow(stock, "TRF-T1", "A1").GetProperty("qtyOnHand").GetDecimal());
        Assert.Equal(4m, StockRow(stock, "TRF-T1", "B1").GetProperty("qtyOnHand").GetDecimal());

        // insufficient source: 6 on hand at A1, 10 requested → 409, nothing moved
        res = await PostAsync("/api/v1/stock/transfers", new
        {
            fromLocationId = "LOC-A1", toLocationId = "LOC-B1", itemSku = "TRF-T1", qty = 10m,
        }, proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("INSUFFICIENT_STOCK", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("6", body.GetProperty("error").GetProperty("message").GetString());

        // same source and destination → 400; unknown location → 404
        res = await PostAsync("/api/v1/stock/transfers", new
        {
            fromLocationId = "LOC-A1", toLocationId = "LOC-A1", itemSku = "TRF-T1", qty = 1m,
        }, proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        res = await PostAsync("/api/v1/stock/transfers", new
        {
            fromLocationId = "LOC-NOPE", toLocationId = "LOC-B1", itemSku = "TRF-T1", qty = 1m,
        }, proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("LOCATION_NOT_FOUND", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        var outbox = await GetAsync("/api/v1/outbox", admin);
        Assert.Contains("inventory.transfer.posted", outbox.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList());
    }

    [Fact]
    public async Task Count_with_variance_posts_balanced_adjustment_journal()
    {
        var proc = "U-002"; var admin = "U-001";
        await SeedStock("CNT-T1", 10m, 2.00m);

        // proc counts 12 against system 10 → +2 variance
        var res = await PostAsync("/api/v1/stock/counts", new
        {
            locationId = "LOC-A1",
            items = new[] { new { itemSku = "CNT-T1", countedQty = 12m } },
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var countData = (await Json(res)).GetProperty("data");
        Assert.Equal("Open", countData.GetProperty("status").GetString());
        Assert.Equal(10m, countData.GetProperty("items")[0].GetProperty("systemQty").GetDecimal());
        var countId = countData.GetProperty("id").GetString()!;

        // a different user (admin) closes → adjustment + balanced journal
        res = await PostAsync($"/api/v1/stock/counts/{countId}/close", new { }, admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("Closed", (await Json(res)).GetProperty("data").GetProperty("count").GetProperty("status").GetString());

        // stock corrected to 12 at A1
        var stock = await GetAsync("/api/v1/stock", admin);
        Assert.Equal(12m, StockRow(stock, "CNT-T1", "A1").GetProperty("qtyOnHand").GetDecimal());

        // journal: variance 2 × cost 2.00 = 4.00 — Dr 1400 Inventory / Cr 3999 Count Adjustment
        var journals = await GetAsync("/api/v1/journals", admin);
        var jrn = journals.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("memo").GetString()!.Contains(countId, StringComparison.Ordinal));
        Assert.Equal("1400", jrn.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(4.00m, jrn.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("3999", jrn.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(4.00m, jrn.GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        var outbox = await GetAsync("/api/v1/outbox", admin);
        Assert.Contains("inventory.adjustment.posted", outbox.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList());
    }

    [Fact]
    public async Task Counter_cannot_close_own_count()
    {
        var proc = "U-002"; var admin = "U-001";
        await SeedStock("CNT-T2", 6m, 1.00m);

        var res = await PostAsync("/api/v1/stock/counts", new
        {
            locationId = "LOC-A1",
            items = new[] { new { itemSku = "CNT-T2", countedQty = 6m } },
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var countId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // maker-checker: the counter cannot close their own count
        res = await PostAsync($"/api/v1/stock/counts/{countId}/close", new { }, proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("COUNT_CLOSE_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Maker-checker", body.GetProperty("error").GetProperty("message").GetString());

        // count stayed open — another user can still close it
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/stock/counts/{countId}/close", new { }, admin)).StatusCode);
        // and a closed count cannot be closed again
        res = await PostAsync($"/api/v1/stock/counts/{countId}/close", new { }, admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Valuation_returns_qty_times_weighted_average_cost()
    {
        var proc = "U-002"; var fin = "U-003";
        await SeedStock("VAL-T1", 8m, 3.00m);

        var res = await GetAsync("/api/v1/stock/valuation", fin);
        var row = res.GetProperty("data").EnumerateArray()
            .First(v => v.GetProperty("itemSku").GetString() == "VAL-T1");
        Assert.Equal(8m, row.GetProperty("qtyOnHand").GetDecimal());
        Assert.Equal(3.00m, row.GetProperty("unitCost").GetDecimal());
        Assert.Equal(24.00m, row.GetProperty("value").GetDecimal());

        // valuation is finance-grade data — journals:read required
        var rawClient = _factory.CreateClient();
        rawClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {proc}");
        var forbidden = await rawClient.GetAsync("/api/v1/stock/valuation");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("FORBIDDEN", (await Json(forbidden)).GetProperty("error").GetProperty("code").GetString());
    }
}
