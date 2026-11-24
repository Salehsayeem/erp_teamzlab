using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// Critic-review backlog fixes (docs/06_Plan.md findings 1-10): SoD, over-receipt, idempotency,
// reversal stock unwind, error codes, permission gates, malformed JSON envelope.
// Each test seeds its own supplier/PO with a unique SKU — Db.I is shared across the test process.

namespace Erp.Api.Tests;

public class ReviewFixTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public ReviewFixTests(TestApiFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? token, string? idemKey = null)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        if (idemKey is not null) client.DefaultRequestHeaders.Add("Idempotency-Key", idemKey);
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

    // seeds supplier + approved PO for one SKU; returns (poId, supplierId)
    private async Task<(string poId, string supplierId)> SeedApprovedPo(string sku, decimal qty, decimal unitPrice)
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
        return (poId, supplierId);
    }

    private static async Task<decimal> StockOf(JsonElement stock, string sku) =>
        stock.GetProperty("data").EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("itemSku").GetString() == sku)
            .GetProperty("qtyOnHand").GetDecimal();

    [Fact]
    public async Task Finance_can_approve_po_but_creator_is_still_blocked()
    {
        var proc = "U-002"; var fin = "U-003";
        var (poId, _) = await SeedSubmittedPo("SOD-1");

        // maker-checker still blocks the creator (checked before the permission gate)
        var res = await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("PO_APPROVAL_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Maker-checker", body.GetProperty("error").GetProperty("message").GetString());

        // finance officer now holds po:approve (finding 7)
        res = await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Approved", (await Json(res)).GetProperty("data").GetProperty("status").GetString());
    }

    private async Task<(string poId, string supplierId)> SeedSubmittedPo(string sku)
    {
        var proc = "U-002";
        var res = await PostAsync("/api/v1/suppliers", new { name = $"Sup {sku}", taxId = $"TRX-{sku}", email = $"{sku.ToLowerInvariant()}@sup.test" }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty = 10m, unitPrice = 5.00m } },
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc)).StatusCode);
        return (poId, supplierId);
    }

    [Fact]
    public async Task Over_receipt_is_rejected_before_any_write()
    {
        var proc = "U-002"; var admin = "U-001";
        var (poId, _) = await SeedApprovedPo("OVR-1", 10m, 2.00m);

        var res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "OVR-1", qty = 6m }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var journalsBefore = (await GetAsync("/api/v1/journals", admin)).GetProperty("data").GetArrayLength();

        // 6 + 6 > 10 → rejected with received/ordered in the message, nothing written
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "OVR-1", qty = 6m }, proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("OVER_RECEIVE", body.GetProperty("error").GetProperty("code").GetString());
        var msg = body.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("6", msg);
        Assert.Contains("10", msg);

        var journalsAfter = (await GetAsync("/api/v1/journals", admin)).GetProperty("data").GetArrayLength();
        Assert.Equal(journalsBefore, journalsAfter);

        // exact fill of the remaining 4 is still allowed
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "OVR-1", qty = 4m }, proc)).StatusCode);
    }

    // T3 finding: ReceivedBySku/ShippedBySku merge db.Receipts.Local on top of a query whose own
    // rows are tracked entities — committed receipts land in .Local via materialization and are
    // counted AGAIN, so a later receipt request can flip the PO to Received before it is actually
    // fully received (which then state-blocks receiving the remainder forever).
    [Fact]
    public async Task Keyed_partial_receipt_keeps_po_Approved_until_fully_received()
    {
        var proc = "U-002"; var admin = "U-001";
        var (poId, _) = await SeedApprovedPo("KPO-1", 10m, 2.00m);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts",
            new { purchaseOrderId = poId, itemSku = "KPO-1", qty = 3m }, proc, $"idem-kpo1a-{Guid.NewGuid():N}")).StatusCode);
        // real total 9 of 10 — but the double-count computes 3 (query) + 3 (local dup) + 6 = 12
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts",
            new { purchaseOrderId = poId, itemSku = "KPO-1", qty = 6m }, proc, $"idem-kpo1b-{Guid.NewGuid():N}")).StatusCode);

        var list = await GetAsync("/api/v1/purchase-orders", admin);
        var po = list.GetProperty("data").EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == poId);
        Assert.Equal("Approved", po.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Keyed_receipts_can_complete_the_order_across_requests()
    {
        var proc = "U-002"; var admin = "U-001";
        var (poId, _) = await SeedApprovedPo("KPO-2", 20m, 2.00m);

        // 10 of 20 — the premature flip (Received) state-blocked the remaining half forever
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts",
            new { purchaseOrderId = poId, itemSku = "KPO-2", qty = 10m }, proc, $"idem-kpo2a-{Guid.NewGuid():N}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts",
            new { purchaseOrderId = poId, itemSku = "KPO-2", qty = 10m }, proc, $"idem-kpo2b-{Guid.NewGuid():N}")).StatusCode);

        var list = await GetAsync("/api/v1/purchase-orders", admin);
        var po = list.GetProperty("data").EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == poId);
        Assert.Equal("Received", po.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Partial_shipments_do_not_mark_so_Shipped_early()
    {
        var proc = "U-002"; var sales = "U-004"; var admin = "U-001";
        var (poId, _) = await SeedApprovedPo("KSO-1", 10m, 2.00m);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts",
            new { purchaseOrderId = poId, itemSku = "KSO-1", qty = 10m }, proc, $"idem-kso1-{Guid.NewGuid():N}")).StatusCode);

        var res = await PostAsync("/api/v1/customers", new { name = "Ship Co", taxId = "TRX-KSO1", email = "ship@cust.test" }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var customerId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = "KSO-1", description = "Item", qty = 10m, unitPrice = 3.00m } },
        }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var soId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, admin)).StatusCode);

        // 8 of 10 shipped — the double-count computes 4 (query) + 4 (local dup) + 4 = 12 >= 10
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = "KSO-1", qty = 4m }, sales)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = "KSO-1", qty = 4m }, sales)).StatusCode);

        var list = await GetAsync("/api/v1/sales-orders", admin);
        var so = list.GetProperty("data").EnumerateArray().First(s => s.GetProperty("id").GetString() == soId);
        Assert.Equal("Confirmed", so.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Idempotency_key_replays_receipt_without_second_write()
    {
        var proc = "U-002"; var admin = "U-001";
        var (poId, _) = await SeedApprovedPo("IDEM-1", 100m, 1.00m);
        var key = $"idem-{poId}";

        var res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "IDEM-1", qty = 30m }, proc, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var first = await Json(res);
        var receiptId = first.GetProperty("data").GetProperty("receipt").GetProperty("id").GetString()!;
        var stockAfterFirst = await StockOf(await GetAsync("/api/v1/stock", admin), "IDEM-1");
        var journalsAfterFirst = (await GetAsync("/api/v1/journals", admin)).GetProperty("data").GetArrayLength();

        // same key → stored response replayed, no second receipt/journal/stock increment
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "IDEM-1", qty = 30m }, proc, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var replay = await Json(res);
        Assert.Equal(receiptId, replay.GetProperty("data").GetProperty("receipt").GetProperty("id").GetString());
        Assert.Equal(first.GetProperty("data").GetProperty("journal").GetProperty("id").GetString(),
            replay.GetProperty("data").GetProperty("journal").GetProperty("id").GetString());

        Assert.Equal(stockAfterFirst, await StockOf(await GetAsync("/api/v1/stock", admin), "IDEM-1"));
        Assert.Equal(journalsAfterFirst, (await GetAsync("/api/v1/journals", admin)).GetProperty("data").GetArrayLength());

        // a different key re-executes normally
        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "IDEM-1", qty = 20m }, proc, $"idem-other-{poId}")).StatusCode);
        Assert.Equal(stockAfterFirst + 20m, await StockOf(await GetAsync("/api/v1/stock", admin), "IDEM-1"));
    }

    [Fact]
    public async Task Journal_reversal_unwinds_stock_and_emits_reversed_event()
    {
        var proc = "U-002"; var admin = "U-001"; var fin = "U-003";
        var (poId, _) = await SeedApprovedPo("REVSTK-1", 8m, 3.00m);
        var res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "REVSTK-1", qty = 8m }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var journalId = (await Json(res)).GetProperty("data").GetProperty("journal").GetProperty("id").GetString()!;
        Assert.Equal(8m, await StockOf(await GetAsync("/api/v1/stock", admin), "REVSTK-1"));

        res = await PostAsync($"/api/v1/journals/{journalId}/reversals", new { }, fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.NotNull((await Json(res)).GetProperty("data").GetProperty("reversalOfId").GetString());

        // finding 4: receipt-derived journal reversal decrements the related stock (floor 0)
        Assert.Equal(0m, await StockOf(await GetAsync("/api/v1/stock", admin), "REVSTK-1"));

        var outbox = await GetAsync("/api/v1/outbox", admin);
        var types = outbox.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("finance.journal.reversed", types);
    }

    [Fact]
    public async Task Permission_gates_block_proc_user()
    {
        var proc = "U-002"; var admin = "U-001";
        Assert.Equal(HttpStatusCode.Forbidden, (await GetRawAsync("/api/v1/journals", proc)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetRawAsync("/api/v1/outbox", proc)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetRawAsync("/api/v1/audit-events", proc)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetRawAsync("/api/v1/journals", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetRawAsync("/api/v1/outbox", admin)).StatusCode);

        // reverse-as-proc denied (finding 8): journals:approve required
        var (poId, _) = await SeedApprovedPo("PROCDENY-1", 4m, 1.00m);
        var res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "PROCDENY-1", qty = 4m }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var journalId = (await Json(res)).GetProperty("data").GetProperty("journal").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/journals/{journalId}/reversals", new { }, proc);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Receipt_error_paths_use_typed_codes()
    {
        var proc = "U-002"; var admin = "U-001";
        var res = await PostAsync("/api/v1/suppliers", new { name = "Sup ERRPATH-1", taxId = "TRX-ERRPATH-1", email = "errpath@sup.test" }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = "ERRPATH-1", description = "Item ERRPATH-1", qty = 5m, unitPrice = 1.00m } },
        }, proc);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // receive on draft → state conflict 409
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "ERRPATH-1", qty = 5m }, proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("RECEIPT_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, admin)).StatusCode);

        // wrong SKU → 404 (missing record per finding 5)
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "NOT-ON-PO", qty = 1m }, proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("ITEM_NOT_FOUND", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // qty validation → 400
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "ERRPATH-1", qty = 0m }, proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // unknown PO → 404
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = "PO-DOES-NOT-EXIST", itemSku = "ERRPATH-1", qty = 1m }, proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("PO_NOT_FOUND", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // submit of a non-draft PO → 409; unknown PO submit → 404
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync("/api/v1/purchase-orders/PO-DOES-NOT-EXIST/submit", new { }, proc)).StatusCode);
    }

    [Fact]
    public async Task Malformed_json_returns_validation_error_envelope()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer U-002");
        var res = await client.PostAsync("/api/v1/suppliers", new StringContent("{ not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json(res);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<HttpResponseMessage> GetRawAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.GetAsync(path);
    }
}
