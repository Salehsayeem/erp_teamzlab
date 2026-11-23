using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// Db.I is a process-wide static singleton shared by all test classes — parallel tests would race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Erp.Api.Tests;

// Order-to-fulfill pilot flow (02_AppFlow §3.2) — SO lifecycle, shipment COGS, AR invoice.
// shortcut: shared WebApplicationFactory per class — in-memory store resets between test classes only

public class OrderToFulfillTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public OrderToFulfillTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.PostAsJsonAsync(path, body);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> GetAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await Json(await client.GetAsync(path));
    }

    [Fact]
    public async Task Order_to_fulfill_posts_shipment_cogs_and_ar_invoice()
    {
        var proc = "U-002"; var admin = "U-001"; var sales = "U-004";

        // supply first: supplier + PO 60 @ 45.50 → receive → stock 60 @ cost basis 45.50
        var res = await PostAsync("/api/v1/suppliers", new { name = "Rod Supplier", taxId = "TRX-7722", email = "rods@steel.test" }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = "STL-ROD-12", description = "Steel rod 12mm", qty = 60m, unitPrice = 45.50m } },
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        res = await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "STL-ROD-12", qty = 60m }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // customer + SO 50 @ 75.00
        res = await PostAsync("/api/v1/customers", new { name = "Buyer Co", taxId = "TRX-5533", email = "ap@buyer.test" }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var customerId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = "STL-ROD-12", description = "Steel rod 12mm", qty = 50m, unitPrice = 75.00m } },
        }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var soBody = await Json(res);
        Assert.Equal("Draft", soBody.GetProperty("data").GetProperty("status").GetString());
        var soId = soBody.GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, sales);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("PendingApproval", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        // maker-checker: creator cannot approve own SO
        res = await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("SO_APPROVAL_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        res = await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Confirmed", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        // ship 50 — stock on hand >= qty; COGS at inventory cost basis 45.50 × 50 = 2275.00
        res = await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = "STL-ROD-12", qty = 50m }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var shipData = (await Json(res)).GetProperty("data");
        Assert.Equal(2275.00m, shipData.GetProperty("journal").GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal(2275.00m, shipData.GetProperty("journal").GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        // invoice: AR journal at order total 50 × 75 = 3750.00
        res = await PostAsync($"/api/v1/sales-orders/{soId}/invoices", new { }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var invData = (await Json(res)).GetProperty("data");
        Assert.Equal("1200", invData.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(3750.00m, invData.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal(3750.00m, invData.GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        // status transitions + balanced journal evidence
        var orders = await GetAsync("/api/v1/sales-orders", sales);
        var so = orders.GetProperty("data").EnumerateArray().First(o => o.GetProperty("id").GetString() == soId);
        Assert.Equal("Invoiced", so.GetProperty("status").GetString());
        Assert.Equal(3750.00m, so.GetProperty("total").GetDecimal());

        var outbox = await GetAsync("/api/v1/outbox", admin);
        var types = outbox.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("sales.so.approved", types);
        Assert.Contains("sales.shipment.posted", types);
        Assert.Contains("sales.invoice.posted", types);
    }

    [Fact]
    public async Task Ship_without_stock_returns_insufficient_stock()
    {
        var sales = "U-004"; var admin = "U-001";

        var res = await PostAsync("/api/v1/customers", new { name = "Ghost Buyer", taxId = "TRX-0000", email = "ghost@buyer.test" }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var customerId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = "NO-STOCK", description = "Never received", qty = 5m, unitPrice = 10.00m } },
        }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var soId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, sales);
        res = await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        res = await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = "NO-STOCK", qty = 5m }, sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("INSUFFICIENT_STOCK", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("0", body.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Customer_validation_error_uses_envelope()
    {
        var res = await PostAsync("/api/v1/customers", new { name = "Bad Email Co", taxId = "TRX-1111", email = "not-an-email" }, "U-004");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json(res);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
    }
}
