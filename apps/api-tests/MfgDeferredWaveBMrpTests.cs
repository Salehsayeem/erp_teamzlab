using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

// MFG deferred screens — Wave B (MF4 MRP: netting, planned orders, conversion, publication;
// ERP-MFG-033..040). TDD sketch items 33-39 (039 AC1 = modifiers ride the output).
namespace Erp.Api.Tests;

public class MfgDeferredWaveBMrpTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public MfgDeferredWaveBMrpTests(TestApiFactory factory) => _factory = factory;

    private const string Mfg = "U-008";     // run maker
    private const string Planner = "U-011"; // converter/publisher (≠ maker — 037/040)
    private const string Admin = "U-001";

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
        var stream = await (await client.GetAsync(path)).Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> Body(HttpResponseMessage res)
    {
        var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var json = doc.RootElement.Clone();
        Assert.True(json.GetProperty("success").GetBoolean(), $"expected success envelope: {json.GetRawText()}");
        return json.GetProperty("data");
    }

    private static async Task<JsonElement> RawJson(HttpResponseMessage res)
    {
        var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static string Tag(string prefix) => $"{prefix}{Guid.NewGuid():N}".ToUpperInvariant()[..12];

    private static string ErrCode(HttpResponseMessage res) =>
        RawJson(res).GetAwaiter().GetResult().GetProperty("error").GetProperty("code").GetString()!;

    private async Task SeedStock(string sku, decimal qty, decimal price)
    {
        var proc = "U-002";
        var res = await PostAsync("/api/v1/suppliers", new { name = $"Sup {sku}", taxId = $"TRX-{sku}", email = $"{sku.ToLowerInvariant()}@sup.test" }, proc);
        var supplierId = (await Body(res)).GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = price } },
        }, proc);
        var poId = (await Body(res)).GetProperty("id").GetString()!;
        await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc);
        await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, proc)).StatusCode);
    }

    private async Task SeedApprovedBom(string outputSku, decimal outputQtyPer, (string sku, decimal qtyPer, decimal cost)[] lines)
    {
        var res = await PostAsync("/api/v1/boms", new { outputSku, revision = 1, outputQtyPer, effectiveFrom = new DateOnly(2026, 1, 1) }, Mfg);
        var bomId = (await Body(res)).GetProperty("id").GetString()!;
        foreach (var l in lines)
            await PostAsync($"/api/v1/boms/{bomId}/lines", new { componentSku = l.sku, qtyPer = l.qtyPer, unit = "pcs", issuePolicy = "Manual", unitCost = l.cost }, Mfg);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{bomId}/approvals", new { }, Admin)).StatusCode);
    }

    private async Task SeedApprovedRouting(string outputSku, decimal setupMin, decimal perUnitMin)
    {
        var wcRes = await PostAsync("/api/v1/work-centers", new { code = Tag("MWC"), name = "wc", capabilities = "mold" }, Mfg);
        var wcId = (await Body(wcRes)).GetProperty("id").GetString()!;
        var res = await PostAsync("/api/v1/routings", new { outputSku, revision = 1, effectiveFrom = new DateOnly(2026, 1, 1) }, Mfg);
        var routingId = (await Body(res)).GetProperty("id").GetString()!;
        await PostAsync($"/api/v1/routings/{routingId}/operations", new
        {
            operationCode = "OP10", workCenterId = wcId, requiredCapabilities = "mold",
            timeUnit = "min", setupMin, perUnitMin, isZeroDuration = false, seq = 1,
        }, Mfg);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/routings/{routingId}/approvals", new { }, Planner)).StatusCode);
    }

    private async Task SeedReleasedOrder(string outputSku, decimal demandedQty)
    {
        var res = await PostAsync("/api/v1/production-orders", new { outputSku, demandedQty, unit = "pcs", overTolerancePct = 0m }, Mfg);
        var orderId = (await Body(res)).GetProperty("id").GetString()!;
        await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Admin)).StatusCode);
        return;
    }

    private async Task<JsonElement> RunMrp(string maker, (string sku, decimal qty)[] demands, decimal? minLot = null, decimal? multiple = null, decimal? safetyPct = null)
    {
        var res = await PostAsync("/api/v1/mrp-runs", new
        {
            demands = demands.Select(d => new { itemSku = d.sku, qty = d.qty, unit = "pcs", by = (DateOnly?)new DateOnly(2026, 4, 15) }).ToArray(),
            horizonFrom = new DateOnly(2026, 4, 1), horizonTo = new DateOnly(2026, 4, 30),
            safetyStockPct = safetyPct, minLotQty = minLot, multipleQty = multiple,
        }, maker);
        if (res.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
            Assert.Fail("mrp-run failed: " + (await RawJson(res)).GetRawText()); // failure path only
        return await Body(res);
    }

    private async Task<List<JsonElement>> Recommendations(JsonElement run)
    {
        var all = await GetAsync($"/api/v1/mrp-runs/{run.GetProperty("id").GetString()}/recommendations", Mfg);
        return all.GetProperty("data").EnumerateArray().ToList();
    }

    // ---- 33: same snapshots + params → identical recommendations (deterministic replay) ----

    [Fact]
    public async Task Mrp_Replay_With_Same_Inputs_Produces_Identical_Recommendations()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 2m, 1.00m)]);
        await SeedApprovedRouting(output, 120m, 10m);
        await SeedStock(output, 4m, 2m);
        await SeedStock(comp, 10m, 1m);

        var run1 = await RunMrp(Mfg, [(output, 10m)], minLot: 6m);
        var run2 = await RunMrp(Mfg, [(output, 10m)], minLot: 6m);
        Assert.Equal("Completed", run1.GetProperty("status").GetString());
        Assert.Equal("Completed", run2.GetProperty("status").GetString());

        var rec1 = await Recommendations(run1);
        var rec2 = await Recommendations(run2);
        Assert.Equal(rec1.Count, rec2.Count);
        foreach (var (a, b) in rec1.Zip(rec2))
        {
            Assert.Equal(a.GetProperty("itemSku").GetString(), b.GetProperty("itemSku").GetString());
            Assert.Equal(a.GetProperty("netQty").GetDecimal(), b.GetProperty("netQty").GetDecimal());
            Assert.Equal(a.GetProperty("requiredBy").GetString(), b.GetProperty("requiredBy").GetString());
            Assert.Equal(a.GetProperty("modifiersJson").GetString(), b.GetProperty("modifiersJson").GetString());
        }
    }

    // ---- 34: quality-held / wrong-owner stock excluded from netting; no double counting ----

    [Fact]
    public async Task Netting_Excludes_Held_And_Foreign_Stock_Without_Double_Counting_Supply()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        await SeedStock(output, 6m, 2m); // on-hand 6
        await SeedStock(comp, 10m, 1m);

        // an open quality hold removes 4 of the 6 from availability (034 AC2)
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO quality_holds (id, tenant_id, legal_entity_id, item_sku, lot_serial, qty, order_id, status, held_by, held_at) " +
            "VALUES ({0}, 'T-001', 'LE-001', {1}, NULL, 4, NULL, 0, 'U-001', now())",
            "QH-" + Guid.NewGuid().ToString("N")[..8], output);
        // wrong-owner stock: a row owned by another tenant must never net (034 AC3)
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO stock_levels (tenant_id, legal_entity_id, warehouse_id, location_id, item_sku, qty_on_hand) " +
            "VALUES ('T-OTHER', 'LE-OTHER', 'WH-001', 'LOC-A1', {0}, 999)", output);

        // scheduled supply: an open released order for 5 covers part of the demand — counted ONCE
        var run = await RunMrp(Mfg, [(output, 10m)]);
        Assert.Equal("Completed", run.GetProperty("status").GetString());
        var lines = await Recommendations(run);
        var fg = lines.First(l => l.GetProperty("itemSku").GetString() == output);
        // availability = 6 − 4 held = 2; no open orders for this fresh sku → net = 10 − 2 = 8
        Assert.Equal(8m, fg.GetProperty("netQty").GetDecimal());
        Assert.Equal(2m, fg.GetProperty("supplyQty").GetDecimal());

        // replay after an open released order exists → scheduled supply 5 counted once (10 − 2 − 5 = 3)
        await SeedReleasedOrder(output, 5m);
        var run2 = await RunMrp(Mfg, [(output, 10m)]);
        var lines2 = await Recommendations(run2);
        var fg2 = lines2.First(l => l.GetProperty("itemSku").GetString() == output);
        Assert.Equal(3m, fg2.GetProperty("netQty").GetDecimal());
    }

    // ---- 35: circular BOM fails the run with a path diagnostic ----

    [Fact]
    public async Task Circular_Bom_Fails_The_Run_With_A_Path_Diagnostic()
    {
        var a = Tag("FG");
        var b = Tag("FG");
        await SeedApprovedBom(a, 1m, [(b, 1m, 1.00m)]);
        await SeedApprovedBom(b, 1m, [(a, 1m, 1.00m)]);

        var run = await RunMrp(Mfg, [(a, 5m)]);
        Assert.Equal("Failed", run.GetProperty("status").GetString());
        var detail = run.GetProperty("failureDetail").GetString() ?? "";
        Assert.Contains(a, detail);
        Assert.Contains(b, detail);
    }

    // ---- 36: missing lead time = exception, never a zero-day date ----

    [Fact]
    public async Task Missing_Lead_Time_Is_An_Exception_And_Never_Zero_Day()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        // the manufactured item has a BOM but NO routing → no lead-time data anywhere
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);

        var run = await RunMrp(Mfg, [(output, 5m)]);
        Assert.Equal("Completed", run.GetProperty("status").GetString());
        var lines = await Recommendations(run);
        var fg = lines.First(l => l.GetProperty("itemSku").GetString() == output);
        Assert.Equal("MISSING_LEAD_TIME", fg.GetProperty("exceptionCode").GetString());
        Assert.Equal(JsonValueKind.Null, fg.GetProperty("requiredBy").ValueKind);
        var messages = await GetAsync($"/api/v1/mrp-runs/{run.GetProperty("id").GetString()}/action-messages", Mfg);
        var msgs = messages.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(msgs, m => m.GetProperty("code").GetString() == "MISSING_LEAD_TIME");
    }

    // ---- 37: conversion idempotent on retry; exactly one linked order ----

    [Fact]
    public async Task Conversion_Is_Idempotent_And_Creates_Exactly_One_Linked_Order()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        await SeedApprovedRouting(output, 60m, 5m);
        await SeedStock(output, 1m, 2m);

        var run = await RunMrp(Mfg, [(output, 10m)]); // net = 10 − 1 = 9
        var lines = await Recommendations(run);
        var fg = lines.First(l => l.GetProperty("itemSku").GetString() == output);
        Assert.Equal(9m, fg.GetProperty("netQty").GetDecimal());
        var lineId = fg.GetProperty("id").GetString()!;

        // conversion by a DIFFERENT officer than the run maker (037 commitment boundary)
        var conv = await PostAsync($"/api/v1/mrp-lines/{lineId}/conversions", new { }, Planner);
        if (!conv.IsSuccessStatusCode)
            Assert.Fail("conversion: " + (await RawJson(conv)).GetRawText());
        Assert.Equal(HttpStatusCode.Created, conv.StatusCode);
        var data = await Body(conv);
        var orderId = data.GetProperty("convertedOrderId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(orderId));

        // retry without a key → same order, no second order; and with a key → byte-identical replay
        var conv2 = await PostAsync($"/api/v1/mrp-lines/{lineId}/conversions", new { }, Planner);
        Assert.Equal(HttpStatusCode.OK, conv2.StatusCode);
        Assert.Equal(orderId, (await Body(conv2)).GetProperty("convertedOrderId").GetString());

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Planner}");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"conv-{Guid.NewGuid():N}");
        var conv3 = client.PostAsJsonAsync($"/api/v1/mrp-lines/{lineId}/conversions", new { }).GetAwaiter().GetResult();
        Assert.True(conv3.IsSuccessStatusCode);
        Assert.Equal(orderId, (await Body(conv3)).GetProperty("convertedOrderId").GetString());

        // maker-checker: the run maker cannot convert their own recommendation
        var run2 = await RunMrp(Mfg, [(output, 3m)]);
        var line2 = (await Recommendations(run2)).First(l => l.GetProperty("itemSku").GetString() == output);
        var own = await PostAsync($"/api/v1/mrp-lines/{line2.GetProperty("id").GetString()}/conversions", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("MRP_CONVERSION_ERROR", ErrCode(own));
    }

    // ---- 38 + 039: stale-source publication gate; modifiers identified in the output ----

    [Fact]
    public async Task Publish_Gates_On_Stale_Sources_And_Idempotent_With_Modifiers_Visible()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        await SeedApprovedRouting(output, 60m, 5m);
        await SeedStock(output, 1m, 2m);

        var run = await RunMrp(Mfg, [(output, 10m)], minLot: 5m, multiple: 4m);
        var lines = await Recommendations(run);
        var fg = lines.First(l => l.GetProperty("itemSku").GetString() == output);
        // net 9 → MIN_LOT no-op (9 ≥ 5); MULTIPLE rounds 9 → 12; the applied modifier + result are identified (039 AC1)
        Assert.Equal(12m, fg.GetProperty("netQty").GetDecimal());
        var modifiers = fg.GetProperty("modifiersJson").GetString() ?? "";
        Assert.Contains("MULTIPLE", modifiers);

        var runId = run.GetProperty("id").GetString()!;
        var lineId = fg.GetProperty("id").GetString()!;

        // the run maker cannot publish their own run (040 maker-checker)
        var own = await PostAsync($"/api/v1/mrp-runs/{runId}/publications", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("MRP_PUBLISH_ERROR", ErrCode(own));

        // a new quality hold drains availability after the snapshot → stale critical source
        await using (var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options))
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO quality_holds (id, tenant_id, legal_entity_id, item_sku, lot_serial, qty, order_id, status, held_by, held_at) " +
                "VALUES ({0}, 'T-001', 'LE-001', {1}, NULL, 1, NULL, 0, 'U-001', now())",
                "QH-" + Guid.NewGuid().ToString("N")[..8], output);

        var stalePub = await PostAsync($"/api/v1/mrp-runs/{runId}/publications", new { }, Planner);
        Assert.Equal(HttpStatusCode.Conflict, stalePub.StatusCode);
        Assert.Equal("MRP_SOURCES_STALE", ErrCode(stalePub));

        // with an override reason the publication lands; second call is idempotent
        var pub = await PostAsync($"/api/v1/mrp-runs/{runId}/publications", new { overrideReason = "drift acknowledged" }, Planner);
        Assert.Equal(HttpStatusCode.OK, pub.StatusCode);
        var pubBody = await Body(pub);
        Assert.True(pubBody.GetProperty("overrodeStaleSources").GetBoolean());
        var pub2 = await PostAsync($"/api/v1/mrp-runs/{runId}/publications", new { }, Admin);
        Assert.Equal(HttpStatusCode.OK, pub2.StatusCode);
        Assert.Equal(pubBody.GetProperty("id").GetString(), (await Body(pub2)).GetProperty("id").GetString()); // idempotent per run

        // the converted line still exists after publication (recommendations survive publish)
        var conv = await PostAsync($"/api/v1/mrp-lines/{lineId}/conversions", new { }, Planner);
        Assert.True(conv.IsSuccessStatusCode || ErrCode(conv) == "MRP_SOURCES_STALE");
    }
}
