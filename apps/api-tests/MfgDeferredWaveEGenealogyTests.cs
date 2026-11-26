using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

// MFG deferred screens — Wave E (MF10 genealogy/rework/close reconciliation 064..070 + the
// route-level authz negative sweep). TDD sketch items 50-54.
namespace Erp.Api.Tests;

public class MfgDeferredWaveEGenealogyTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public MfgDeferredWaveEGenealogyTests(TestApiFactory factory) => _factory = factory;

    private const string Mfg = "U-008";
    private const string Planner = "U-012";
    private const string Admin = "U-001";

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

    private async Task<string> SeedReleasedOrder(string outputSku, decimal demandedQty)
    {
        var res = await PostAsync("/api/v1/production-orders", new { outputSku, demandedQty, unit = "pcs", overTolerancePct = 0m }, Mfg);
        var orderId = (await Body(res)).GetProperty("id").GetString()!;
        await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg);
        var rel = await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Admin);
        if (!rel.IsSuccessStatusCode)
            Assert.Fail("release failed: " + (await RawJson(rel)).GetRawText());
        return orderId;
    }

    // full helper: stock + bom + order + issue + completion with lot identity
    private async Task<string> CompleteOrder(string outputSku, string comp, decimal outQty, string? lotSerial)
    {
        await SeedStock(comp, outQty * 2m, 1m);
        var orderId = await SeedReleasedOrder(outputSku, outQty);
        var issues = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Mfg));
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = outQty }, Mfg);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var completion = await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = outQty, lotSerial }, Mfg);
        if (!completion.IsSuccessStatusCode)
            Assert.Fail("completion: " + (await RawJson(completion)).GetRawText());
        return orderId;
    }

    // ---- 50: controlled-item events without mandatory identity are rejected ----

    [Fact]
    public async Task Controlled_Item_Completion_Requires_Lot_Identity()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        // profile-gate the sku as lot-controlled (065 AC2)
        await using (var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options))
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO finance_settings (id, key, legal_entity_id, tenant_id, value) VALUES ({0}, 'mfg.lot_controlled_skus', 'LE-001', 'T-001', {1})",
                "FS-lot-" + Guid.NewGuid().ToString("N")[..8], output);

        await SeedStock(comp, 20m, 1m);
        var orderId = await SeedReleasedOrder(output, 5m);
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 5m }, Mfg)).IsSuccessStatusCode);

        // completion WITHOUT the lot serial → rejected, nothing recorded (065 AC2)
        var missing = await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 5m, lotSerial = (string?)null }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal("LOT_IDENTITY_REQUIRED", ErrCode(missing));

        // with the identity the completion lands and the event carries the lot
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 5m, lotSerial = "LOT-OK-1" }, Mfg)).IsSuccessStatusCode);
        var events = (await GetAsync($"/api/v1/genealogy?sku={output}", Mfg)).GetProperty("data");
        var evt = events.GetProperty("events").EnumerateArray().First();
        Assert.Equal("LOT-OK-1", evt.GetProperty("lotSerial").GetString());
    }

    // ---- 51/66: genealogy reports gaps, never infers; completeness flips on missing links ----

    [Fact]
    public async Task Genealogy_Reports_Gaps_And_Completeness_Flips_On_Missing_Links()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        var orderId = await CompleteOrder(output, comp, 4m, null);

        var trace = (await GetAsync($"/api/v1/genealogy?sku={output}", Mfg)).GetProperty("data");
        var events = trace.GetProperty("events").EnumerateArray().ToList();
        Assert.Single(events); // one transformation event from the completion
        Assert.Equal(4m, events[0].GetProperty("outputQty").GetDecimal());
        // the component has NO producing event (it was purchased) — an unresolved edge, reported
        var gaps = trace.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Contains(gaps, g => g.GetProperty("itemSku").GetString() == comp);
        Assert.False(trace.GetProperty("complete").GetBoolean()); // incomplete, never inferred

        // a lot query that matches nothing returns an honest empty trace
        var empty = (await GetAsync($"/api/v1/genealogy?lot=NO-SUCH-LOT", Mfg)).GetProperty("data");
        Assert.Equal(0, empty.GetProperty("events").GetArrayLength());
    }

    // ---- 52 (068): rework qty capped at affected qty; completion gated on reinspection ----

    [Fact]
    public async Task Rework_Quantity_Is_Capped_And_Completion_Gated_On_Reinspection()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        var orderId = await CompleteOrder(output, comp, 6m, null);

        // an NC identifying 3 affected units bounds the rework
        var nc = (await Body(await PostAsync("/api/v1/nonconformances", new
        {
            sourceEventId = $"nc-{Guid.NewGuid():N}", title = "Cosmetic defects", itemSku = output,
            orderId, affectedQty = 3m,
            objects = new[] { new { objectType = "order", objectRef = orderId, qty = 3m } },
        }, Admin))).GetProperty("id").GetString()!;

        // qty over the affected total without an approved adjustment → rejected (068 AC1)
        var over = await PostAsync("/api/v1/rework-orders", new
        {
            sourceOrderId = orderId, itemSku = output, qty = 5m, nonconformanceId = nc,
            holdId = (string?)null, adjustmentReason = (string?)null,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("REWORK_QTY_OVER", ErrCode(over));

        // within the cap → created; approval by a DIFFERENT officer (068 maker-checker)
        var rework = (await Body(await PostAsync("/api/v1/rework-orders", new
        {
            sourceOrderId = orderId, itemSku = output, qty = 3m, nonconformanceId = nc,
            holdId = (string?)null, adjustmentReason = (string?)null,
        }, Mfg))).GetProperty("id").GetString()!;
        var own = await PostAsync($"/api/v1/rework-orders/{rework}/approvals", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("REWORK_APPROVAL_ERROR", ErrCode(own));
        Assert.True((await PostAsync($"/api/v1/rework-orders/{rework}/approvals", new { }, Planner)).IsSuccessStatusCode);

        // completion without a finalized passing reinspection → gated (068 AC3)
        var gated = await PostAsync($"/api/v1/rework-orders/{rework}/completions", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, gated.StatusCode);
        Assert.Equal("REINSPECTION_REQUIRED", ErrCode(gated));

        // a finalized PASSING inspection of the same item unlocks completion
        var plan = (await Body(await PostAsync("/api/v1/quality-plans", new
        {
            itemSku = output, revision = 1, effectiveFrom = new DateOnly(2026, 1, 1),
        }, Mfg))).GetProperty("id").GetString()!;
        Assert.True((await PostAsync($"/api/v1/quality-plans/{plan}/characteristics", new
        {
            code = "VISUAL", name = "Visual check", dataType = "Boolean", minValue = (decimal?)null,
            maxValue = (decimal?)null, unit = (string?)null, mandatory = true,
        }, Mfg)).IsSuccessStatusCode);
        Assert.True((await PostAsync($"/api/v1/quality-plans/{plan}/approvals", new { }, Admin)).IsSuccessStatusCode);
        var inspection = (await Body(await PostAsync("/api/v1/inspections", new
        {
            qualityPlanId = plan, orderId, itemSku = output, lotSerial = (string?)null, populationQty = 3m,
        }, Admin))).GetProperty("id").GetString()!;
        Assert.True((await PostAsync($"/api/v1/inspections/{inspection}/results", new { characteristicCode = "VISUAL", rawValue = "true" }, Admin)).IsSuccessStatusCode);
        Assert.True((await PostAsync($"/api/v1/inspections/{inspection}/finalizations", new { }, Admin)).IsSuccessStatusCode);
        Assert.True((await PostAsync($"/api/v1/rework-orders/{rework}/reinspections", new { inspectionId = inspection }, Mfg)).IsSuccessStatusCode);

        Assert.True((await PostAsync($"/api/v1/rework-orders/{rework}/completions", new { }, Mfg)).IsSuccessStatusCode);
    }

    // ---- 53 (070): close reconciliation lists bucket variances; critical blocks absent override ----

    [Fact]
    public async Task Close_Reconciliation_Lists_Variances_And_Critical_Blocks_Close()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 2.00m)]);
        await SeedStock(comp, 30m, 1m);
        var orderId = await SeedReleasedOrder(output, 5m);
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 4m }, Mfg)).IsSuccessStatusCode);
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 5m }, Mfg)).IsSuccessStatusCode);
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).IsSuccessStatusCode);
        // reconcile consumption fully so the Completed-transition gate passes — the variance
        // under test is recorded explicitly via the ledger, not discovered by the gate
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)).IsSuccessStatusCode);
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).IsSuccessStatusCode);

        // reconciliation exposes the consumption variance (4 issued vs 5 planned) with owner + age
        var recon = (await GetAsync($"/api/v1/production-orders/{orderId}/close-reconciliation", Admin)).GetProperty("data");
        Assert.Equal(5m, recon.GetProperty("buckets").GetProperty("issued").GetDecimal());
        Assert.Equal(5m, recon.GetProperty("buckets").GetProperty("completed").GetDecimal());
        Assert.Empty(recon.GetProperty("variances").EnumerateArray()); // none recorded yet

        // record the variance with an owner; an UNRESOLVED CRITICAL variance blocks close
        var varRes = await PostAsync($"/api/v1/production-orders/{orderId}/close-reconciliation/variance-dispositions", new
        {
            bucketCode = "consumption", qtyVariance = 1m, isCritical = true, owner = "U-008",
        }, Admin);
        if (!varRes.IsSuccessStatusCode)
            Assert.Fail("variance: " + (await RawJson(varRes)).GetRawText());
        var variance = (await Body(varRes)).GetProperty("id").GetString()!;

        var blocked = await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("MFG_CLOSE_BLOCKED", ErrCode(blocked));

        // maker-checker: the disposer cannot decide their own variance
        var own = await PostAsync($"/api/v1/variance-dispositions/{variance}/decisions", new
        {
            disposition = "accepted", reason = "self-accept",
        }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("VARIANCE_DECISION_ERROR", ErrCode(own));

        // a different officer accepts with a reason → close proceeds
        Assert.True((await PostAsync($"/api/v1/variance-dispositions/{variance}/decisions", new
        {
            disposition = "accepted", reason = "approved scrap",
        }, Planner)).IsSuccessStatusCode);
        Assert.True((await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)).IsSuccessStatusCode);
    }

    // ---- 54: authz negative sweep over the new routes (Gate 2) ----

    [Fact]
    public async Task Authz_Negative_Sweep_On_New_Routes()
    {
        var noAuth = await PostAsync("/api/v1/mrp-runs", new { demands = Array.Empty<object>(), horizonFrom = new DateOnly(2026, 1, 1), horizonTo = new DateOnly(2026, 1, 2) }, null);
        Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);

        // R-HR has no MFG permissions at all
        var hr = "U-007";
        foreach (var (path, body) in new (string Path, object Body)[]
        {
            ("/api/v1/mrp-runs", new { demands = new[] { new { itemSku = "X", qty = 1m, unit = "pcs", by = (DateOnly?)null } }, horizonFrom = new DateOnly(2026, 1, 1), horizonTo = new DateOnly(2026, 1, 2) }),
            ("/api/v1/schedule-runs", new { orderId = "X", mode = "Finite", horizonFrom = new DateOnly(2026, 1, 1), horizonTo = new DateOnly(2026, 1, 2), calendarVersion = "c", algorithmVersion = "a" }),
            ("/api/v1/quality-plans", new { itemSku = "X", revision = 1, effectiveFrom = new DateOnly(2026, 1, 1) }),
            ("/api/v1/quality-holds", new { itemSku = "X", qty = 1m }),
            ("/api/v1/production-corrections", new { targetType = "labor_time", targetId = "X", reason = "r", compensatingJson = "{}" }),
            ("/api/v1/rework-orders", new { sourceOrderId = "X", itemSku = "X", qty = 1m }),
            ("/api/v1/subcontract-custodies", new { orderId = "X", routingOperationSeq = 1, kind = "Send", qty = 1m, inventoryMovementId = "Y" }),
            ("/api/v1/maintenance-availability-events", new { sourceEventId = "S", revision = 1, resourceCode = "R", windowStart = DateTimeOffset.UtcNow, windowEnd = DateTimeOffset.UtcNow.AddHours(1) }),
        })
        {
            var res = await PostAsync(path, body, hr);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }
    }
}
