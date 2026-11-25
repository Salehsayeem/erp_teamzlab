using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

// MFG deferred screens — Wave D (MF8 quality plans/inspections 055..058, MF9 holds/NCR/CAPA
// 059..063). TDD sketch items 45-49. R-QC is CONSUMED here, never seeded by a migration — the
// fixture bootstraps the role + user into its own throwaway DB (idempotent for the post-PROC
// world where the PROC migration seeds it).
namespace Erp.Api.Tests;

public class MfgDeferredWaveDQualityTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    private const string Mfg = "U-008";   // quality:plan (author)
    private const string Admin = "U-001"; // plan approver (checker)
    private const string Qc = "U-012";    // R-QC — inspect + dispose (fixture-bootstrapped)
    private const string Qc2 = "U-013";   // second R-QC user — disposition checker

    public MfgDeferredWaveDQualityTests(TestApiFactory factory)
    {
        _factory = factory;
        // fixture-only R-QC bootstrap (brief §5: MFG consumes R-QC, PROCUREMENT seeds it; until
        // that slice lands, tests bootstrap the role here — ON CONFLICT keeps it idempotent when
        // the PROC migration eventually seeds the same rows)
        using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(factory.ConnectionString).Options);
        db.Database.ExecuteSqlRaw(
            "INSERT INTO roles (id, name, permissions, tenant_id, legal_entity_id) " +
            "VALUES ('R-QC', 'quality_officer', ARRAY['quality:inspect', 'quality:dispose', 'mfg:read'], 'T-001', 'LE-001') " +
            "ON CONFLICT (id) DO NOTHING");
        db.Database.ExecuteSqlRaw(
            "INSERT INTO users (id, email, display_name, active, tenant_id, legal_entity_id) " +
            "VALUES ('U-012', 'quality@teamzlab.dev', 'Quality Officer', true, 'T-001', 'LE-001') " +
            "ON CONFLICT (id) DO NOTHING");
        db.Database.ExecuteSqlRaw(
            "INSERT INTO users (id, email, display_name, active, tenant_id, legal_entity_id) " +
            "VALUES ('U-013', 'quality2@teamzlab.dev', 'Quality Checker', true, 'T-001', 'LE-001') " +
            "ON CONFLICT (id) DO NOTHING");
        db.Database.ExecuteSqlRaw(
            "INSERT INTO memberships (user_id, tenant_id, legal_entity_id, role_id) " +
            "VALUES ('U-012', 'T-001', 'LE-001', 'R-QC') ON CONFLICT DO NOTHING");
        db.Database.ExecuteSqlRaw(
            "INSERT INTO memberships (user_id, tenant_id, legal_entity_id, role_id) " +
            "VALUES ('U-013', 'T-001', 'LE-001', 'R-QC') ON CONFLICT DO NOTHING");
    }

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

    private async Task<string> SeedApprovedPlan(string itemSku, (string code, bool mandatory, decimal? min, decimal? max)[] characteristics)
    {
        var res = await PostAsync("/api/v1/quality-plans", new
        {
            itemSku, revision = 1, effectiveFrom = new DateOnly(2026, 1, 1),
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var planId = (await Body(res)).GetProperty("id").GetString()!;
        foreach (var c in characteristics)
        {
            var added = await PostAsync($"/api/v1/quality-plans/{planId}/characteristics", new
            {
                code = c.code, name = $"Char {c.code}", dataType = "Numeric",
                minValue = c.min, maxValue = c.max, unit = "mm", mandatory = c.mandatory,
            }, Mfg);
            Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/quality-plans/{planId}/approvals", new { }, Admin)).StatusCode);
        return planId;
    }

    private async Task<string> SeedStock(string sku, decimal qty, decimal price)
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
        return poId;
    }

    private decimal AvailableOf(JsonElement stock, string sku) =>
        stock.GetProperty("data").EnumerateArray().Where(s => s.GetProperty("itemSku").GetString() == sku)
            .Sum(s => s.TryGetProperty("availableQty", out var a) ? a.GetDecimal() : s.GetProperty("qtyOnHand").GetDecimal());

    // ---- 45: an inspection missing mandatory characteristics cannot finalize ----

    [Fact]
    public async Task Inspection_Without_Mandatory_Characteristics_Cannot_Finalize()
    {
        var item = Tag("FG");
        var planId = await SeedApprovedPlan(item, [("DIM", true, 9.9m, 10.1m), ("COLOR", false, null, null)]);

        var res = await PostAsync("/api/v1/inspections", new
        {
            qualityPlanId = planId, orderId = "MF8-INS", itemSku = item, lotSerial = (string?)null, populationQty = 10m,
        }, Qc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var inspection = await Body(res);
        Assert.Equal(1, inspection.GetProperty("planRevision").GetInt32()); // the EXACT plan revision stored (055)
        var inspectionId = inspection.GetProperty("id").GetString()!;

        // finalizing without the mandatory DIM result → blocked (058 AC1)
        var early = await PostAsync($"/api/v1/inspections/{inspectionId}/finalizations", new { }, Qc);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("QUALITY_INSPECTION_INCOMPLETE", ErrCode(early));

        // entering the mandatory result unlocks finalization
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/inspections/{inspectionId}/results", new
        {
            characteristicCode = "DIM", rawValue = "10.05",
        }, Qc)).StatusCode);

        var finalize = await PostAsync($"/api/v1/inspections/{inspectionId}/finalizations", new { }, Qc);
        Assert.True(finalize.IsSuccessStatusCode, await finalize.Content.ReadAsStringAsync());

        // double finalize → state conflict
        var again = await PostAsync($"/api/v1/inspections/{inspectionId}/finalizations", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("QUALITY_INSPECTION_STATE_ERROR", ErrCode(again));
    }

    // ---- 46: finalized results immutable; corrections are linked amended results ----

    [Fact]
    public async Task Finalized_Results_Are_Immutable_And_Corrections_Link_Both()
    {
        var item = Tag("FG");
        var planId = await SeedApprovedPlan(item, [("DIM", true, 9.9m, 10.1m)]);
        var res = await PostAsync("/api/v1/inspections", new
        {
            qualityPlanId = planId, orderId = "MF8-INS2", itemSku = item, lotSerial = (string?)null, populationQty = 5m,
        }, Qc);
        var inspectionId = (await Body(res)).GetProperty("id").GetString()!;
        var resultRes = await PostAsync($"/api/v1/inspections/{inspectionId}/results", new { characteristicCode = "DIM", rawValue = "10.05" }, Qc);
        var resultId = (await Body(resultRes)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/inspections/{inspectionId}/finalizations", new { }, Qc)).StatusCode);

        // the finalized inspection refuses new results (058 AC2 — immutable)
        var extra = await PostAsync($"/api/v1/inspections/{inspectionId}/results", new { characteristicCode = "DIM", rawValue = "10.0" }, Qc);
        Assert.Equal(HttpStatusCode.Conflict, extra.StatusCode);
        Assert.Equal("QUALITY_INSPECTION_STATE_ERROR", ErrCode(extra));

        // the correction path: a NEW linked amended result, both preserved
        var amend = await PostAsync($"/api/v1/inspections/{inspectionId}/results/{resultId}/amendments", new
        {
            rawValue = "10.02", reason = "instrument recalibrated",
        }, Qc);
        Assert.True(amend.IsSuccessStatusCode, await amend.Content.ReadAsStringAsync());
        var results = (await GetAsync($"/api/v1/inspections/{inspectionId}/results", Qc)).GetProperty("data").EnumerateArray().ToList();
        var original = results.First(r => r.GetProperty("id").GetString() == resultId);
        Assert.Equal("10.05", original.GetProperty("rawValue").GetString()); // preserved
        var amended = results.Where(r => r.GetProperty("amendsId").GetString() == resultId).ToList();
        Assert.Single(amended);
        Assert.Equal("10.02", amended[0].GetProperty("rawValue").GetString());
        Assert.Equal(2, amended[0].GetProperty("amendmentNo").GetInt32());
    }

    // ---- 47: holds remove quantity from availability; dispositions restore per decision ----

    [Fact]
    public async Task Quality_Holds_Remove_Availability_And_Dispositions_Restore_Per_Decision()
    {
        var item = Tag("RM");
        await SeedStock(item, 10m, 2m);

        var before = await GetAsync("/api/v1/stock", Admin);
        Assert.Equal(10m, AvailableOf(before, item));

        // the holder is Qc; the disposer must be a DIFFERENT quality officer (059 maker-checker)
        var hold = await PostAsync("/api/v1/quality-holds", new
        {
            itemSku = item, lotSerial = (string?)null, qty = 4m, orderId = (string?)null,
            inspectionId = (string?)null, nonconformanceId = (string?)null,
        }, Qc);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var holdId = (await Body(hold)).GetProperty("id").GetString()!;

        // availability honors the open hold (059 AC2) — the flagged InventoryService change
        var held = await GetAsync("/api/v1/stock", Admin);
        Assert.Equal(6m, AvailableOf(held, item)); // 10 − 4 held

        // maker-checker: the holder cannot dispose their own hold
        var own = await PostAsync($"/api/v1/quality-holds/{holdId}/dispositions", new
        {
            disposition = "ReleaseToStock", qty = 4m, reason = "self-release",
        }, Qc);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("QUALITY_DISPOSITION_ERROR", ErrCode(own));

        // a different officer releases to stock → availability restored
        var release = await PostAsync($"/api/v1/quality-holds/{holdId}/dispositions", new
        {
            disposition = "ReleaseToStock", qty = 4m, reason = "verified good",
        }, Qc2);
        Assert.True(release.IsSuccessStatusCode, await release.Content.ReadAsStringAsync());
        var after = await GetAsync("/api/v1/stock", Admin);
        Assert.Equal(10m, AvailableOf(after, item));

        // scrap disposition draws stock through WIP-like accounting with the period guard
        var hold2 = (await Body(await PostAsync("/api/v1/quality-holds", new
        {
            itemSku = item, lotSerial = (string?)null, qty = 2m, orderId = (string?)null,
            inspectionId = (string?)null, nonconformanceId = (string?)null,
        }, Qc))).GetProperty("id").GetString()!;
        var scrap = await PostAsync($"/api/v1/quality-holds/{hold2}/dispositions", new
        {
            disposition = "Scrap", qty = 2m, reason = "corroded",
        }, Qc2);
        Assert.True(scrap.IsSuccessStatusCode, await scrap.Content.ReadAsStringAsync());
        var scrapData = await Body(scrap);
        Assert.False(string.IsNullOrWhiteSpace(scrapData.GetProperty("journalId").GetString()));
        var final = await GetAsync("/api/v1/stock", Admin);
        Assert.Equal(8m, AvailableOf(final, item)); // 10 − 2 scrapped
    }

    // ---- 48: NC closure blocked while mandatory containment is open ----

    [Fact]
    public async Task Nc_Closure_Blocked_While_Containment_Open_And_Reviewed_On_Release()
    {
        var nc = await PostAsync("/api/v1/nonconformances", new
        {
            sourceEventId = $"nc-{Guid.NewGuid():N}", title = "Dimension drift", itemSku = Tag("FG"),
            orderId = (string?)null, affectedQty = 8m,
            objects = new[] { new { objectType = "lot", objectRef = "LOT-1", qty = 8m } },
        }, Qc);
        Assert.Equal(HttpStatusCode.Created, nc.StatusCode);
        var ncData = await Body(nc);
        var ncId = ncData.GetProperty("id").GetString()!;
        Assert.Single(await Task.FromResult(ncData.GetProperty("objects").EnumerateArray().ToList())); // ≥1 affected object (060)

        // containment opened by Qc; only an AUTHORIZED reviewer (≠ opener) releases (061 AC2)
        var containment = (await Body(await PostAsync($"/api/v1/nonconformances/{ncId}/containments", new { kind = "Quarantine" }, Qc)))
            .GetProperty("id").GetString()!;
        var selfRelease = await PostAsync($"/api/v1/containments/{containment}/releases", new { reason = "self" }, Qc);
        Assert.Equal(HttpStatusCode.Conflict, selfRelease.StatusCode);
        Assert.Equal("CONTAINMENT_RELEASE_ERROR", ErrCode(selfRelease));

        var blocked = await PostAsync($"/api/v1/nonconformances/{ncId}/closures", new { }, Qc2);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("NC_CONTAINMENT_OPEN", ErrCode(blocked));

        var release = await PostAsync($"/api/v1/containments/{containment}/releases", new { reason = "containment verified" }, Qc2);
        Assert.True(release.IsSuccessStatusCode, await release.Content.ReadAsStringAsync());

        Assert.True((await PostAsync($"/api/v1/nonconformances/{ncId}/closures", new { }, Qc2)).IsSuccessStatusCode);
    }

    // ---- 49: CAPA closure blocked until actions + effectiveness criteria are satisfied ----

    [Fact]
    public async Task Capa_Closure_Blocked_Until_Actions_And_Effectiveness_Satisfied()
    {
        var capa = (await Body(await PostAsync("/api/v1/capa-cases", new
        {
            title = "Recurring defect", nonconformanceId = (string?)null,
            effectivenessCriteria = "zero recurrences over 30 days",
        }, Qc))).GetProperty("id").GetString()!;
        var action = (await Body(await PostAsync($"/api/v1/capa-cases/{capa}/actions", new
        {
            description = "recalibrate station", dueBy = (DateTimeOffset?)new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        }, Qc))).GetProperty("id").GetString()!;

        var early = await PostAsync($"/api/v1/capa-cases/{capa}/closures", new { }, Qc2);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("CAPA_CLOSE_BLOCKED", ErrCode(early));

        Assert.True((await PostAsync($"/api/v1/capa-actions/{action}/completions", new { }, Qc2)).IsSuccessStatusCode);

        // actions done but effectiveness not yet verified → still blocked (062 AC1)
        var mid = await PostAsync($"/api/v1/capa-cases/{capa}/closures", new { }, Qc2);
        Assert.Equal(HttpStatusCode.Conflict, mid.StatusCode);
        Assert.Equal("CAPA_CLOSE_BLOCKED", ErrCode(mid));

        // an UNSATISFIED verification also blocks; maker-checker on verification ≠ closer
        await PostAsync($"/api/v1/capa-cases/{capa}/verifications", new { criteriaResult = "one recurrence", satisfied = false }, Qc);
        var unsat = await PostAsync($"/api/v1/capa-cases/{capa}/closures", new { }, Qc2);
        Assert.Equal(HttpStatusCode.Conflict, unsat.StatusCode);
        Assert.Equal("CAPA_CLOSE_BLOCKED", ErrCode(unsat));

        await PostAsync($"/api/v1/capa-cases/{capa}/verifications", new { criteriaResult = "clean 30 days", satisfied = true }, Qc);
        var close = await PostAsync($"/api/v1/capa-cases/{capa}/closures", new { }, Qc2);
        Assert.True(close.IsSuccessStatusCode, await close.Content.ReadAsStringAsync());
    }
}
