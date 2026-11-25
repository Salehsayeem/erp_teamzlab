using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

// Journal-adjacent race hardening (survey: .superpowers/sdd/06_Plan/reports/journal-race-survey.md).
// Four uncovered check-then-act windows on money paths, each proved by the house race forms:
// stale-snapshot (deterministic: scope A holds a context, scope B commits via HTTP, scope A
// commits its stale view — MoneyHardeningTests convention) for single-row status flips, and
// two concurrent HTTP callers (ProcurementDeepTests FIX-4 convention, "exactly one wins") for
// aggregate guards that have no row to hold.
//   1. count close double-post  → counts.Status token (409 COUNT_ALREADY_CLOSED)
//   2. asset event double-approve → asset_events.JournalId token (409 ASSET_EVENT_ALREADY_APPROVED)
//   3. over-receipt race → PO-row FOR UPDATE + under-lock re-check (409 OVER_RECEIVE)
//   4. same-period depreciation double-run → unique index (409 DEPRECIATION_PERIOD_ALREADY_POSTED)

namespace Erp.Api.Tests;

public class JournalRaceHardeningTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public JournalRaceHardeningTests(TestApiFactory factory) => _factory = factory;

    private const string Admin = "U-001";
    private const string Proc = "U-002";
    private const string Writer = "U-005"; // assets_officer — assets:write + assets:approve

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.PostAsJsonAsync(path, body);
    }

    private async Task<HttpResponseMessage> GetRawAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.GetAsync(path);
    }

    private async Task<JsonElement> GetAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await Json(await client.GetAsync(path));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static string Tag(string prefix) => $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant()[..^8];

    // racers answer 2xx on success with no error envelope — null means "this one won"
    private static string? TryCode(HttpResponseMessage res)
    {
        var root = JsonDocument.Parse(res.Content.ReadAsStream()).RootElement;
        return root.TryGetProperty("error", out var e) ? e.GetProperty("code").GetString() : null;
    }

    // Deterministic race barrier: hold, in a separate transaction, the row the racing writers
    // UPDATE at SaveChanges — both callers run their (unserialised) guards, pass, and park
    // inside SaveChanges on the barrier lock; releasing the barrier lets BOTH commits through,
    // so the double-write is observed every run, not whenever the clock happens to overlap.
    private (ErpDbContext Db, IDbContextTransaction Tx) OpenBarrier()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        var db = new ErpDbContext(options);
        var tx = db.Database.BeginTransaction();
        return (db, tx);
    }

    private static decimal StockOf(JsonElement stock, string sku) =>
        stock.GetProperty("data").EnumerateArray()
            .First(s => s.GetProperty("itemSku").GetString() == sku)
            .GetProperty("qtyOnHand").GetDecimal();

    private static int JournalsMentioning(JsonElement journals, string marker) =>
        journals.GetProperty("data").EnumerateArray()
            .Count(j => j.GetProperty("memo").GetString()!.Contains(marker, StringComparison.Ordinal));

    // (ErpDbContext, EntityContext) constructor for the stale-snapshot scopes — mirrors
    // MoneyHardeningTests' scope-A service wiring: every repository rides the SAME dbA so the
    // stale snapshot is the tracked identity-map view.
    private (ErpDbContext Db, EntityContext Ec, InventoryService Inventory, AssetService Assets) ScopeA()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        var dbA = new ErpDbContext(options);
        var ecA = new EntityContext();
        var numbers = new NumberSeries(dbA);
        var audit = new AuditRepository(dbA, new NumberSeries(dbA), ecA);
        return (dbA, ecA,
            new InventoryService(new InventoryRepository(dbA, ecA), new PurchaseOrderRepository(dbA, ecA),
                new JournalRepository(dbA, ecA), numbers, audit, ecA, dbA),
            new AssetService(new AssetsRepository(dbA, ecA), new JournalRepository(dbA, ecA), numbers, audit, ecA, dbA));
    }

    private static Erp.Api.CurrentUser AdminUser =>
        new("U-001", "admin@teamzlab.dev", "Tenant Admin", "R-ADMIN", new[] { "*" });

    // supplier + approved PO, nothing received yet — the receive race seeds from this
    private async Task<string> SeedApprovedPo(string sku, decimal qty, decimal unitPrice)
    {
        var res = await PostAsync("/api/v1/suppliers", new { name = $"Sup {sku}", taxId = $"TRX-{sku}", email = $"{sku.ToLowerInvariant()}@sup.test" }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice } },
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, Proc)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        return poId;
    }

    private async Task<string> SeedStock(string sku, decimal qty, decimal unitPrice)
    {
        var poId = await SeedApprovedPo(sku, qty, unitPrice);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, Proc)).StatusCode);
        return poId;
    }

    private async Task<string> SeedActiveAsset(string tag, decimal cost)
    {
        var res = await PostAsync("/api/v1/assets", new
        {
            tag, serial = $"SN-{tag}", name = $"Asset {tag}", category = "Equipment", cost, locationId = "LOC-A1",
        }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var assetId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations", new { sourceProjectId = (string?)null, amount = cost }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var capEventId = (await Json(res)).GetProperty("data").GetProperty("event").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/assets/{assetId}/capitalizations/{capEventId}/approvals", new { }, Admin)).StatusCode);
        return assetId;
    }

    // ---- RACE 1: count close — the Open→Closed flip must post the variance journal once ----

    [Fact]
    public async Task Count_Close_From_A_Stale_Snapshot_Is_Refused_And_Posts_Nothing()
    {
        var sku = Tag("JRH-CNT");
        await SeedStock(sku, 10m, 2.00m);
        var res = await PostAsync("/api/v1/stock/counts", new
        {
            locationId = "LOC-A1",
            items = new[] { new { itemSku = sku, countedQty = 8m } }, // -2 variance vs the frozen system 10
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var countId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // scope A: the second officer's context holds the count while it is still Open
        var (dbA, ecA, inventoryA, _) = ScopeA();
        var stale = new InventoryRepository(dbA, ecA).FindCount(countId);
        Assert.Equal(Erp.Api.CountStatus.Open, stale!.Status);

        // scope B: the first officer closes via HTTP — one adjustment journal, stock 10 → 8
        res = await PostAsync($"/api/v1/stock/counts/{countId}/close", new { }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var stockAfterFirst = StockOf(await GetAsync("/api/v1/stock", Admin), sku);
        Assert.Equal(8m, stockAfterFirst);
        var journalsAfterFirst = JournalsMentioning(await GetAsync("/api/v1/journals", Admin), countId);
        Assert.Equal(1, journalsAfterFirst);

        // scope A: closes the SAME count from its stale Open view — the token-guarded flip must
        // match zero rows, refuse with 409, and commit NOTHING (no second journal, no re-draw)
        var (count, adjustments, code, message) = inventoryA.Close(AdminUser, countId);
        Assert.Null(count);
        Assert.Null(adjustments);
        Assert.Equal("COUNT_ALREADY_CLOSED", code);
        Assert.Contains(countId, message);
        Assert.Equal(journalsAfterFirst, JournalsMentioning(await GetAsync("/api/v1/journals", Admin), countId));
        Assert.Equal(stockAfterFirst, StockOf(await GetAsync("/api/v1/stock", Admin), sku));
    }

    // ---- RACE 2: asset event approval — the pending event must post its journal once ------

    [Fact]
    public async Task Impairment_Approval_From_A_Stale_Snapshot_Is_Refused_And_Posts_Nothing()
    {
        var tag = Tag("JRH-IMP");
        var assetId = await SeedActiveAsset(tag, 1200m);
        var res = await PostAsync($"/api/v1/assets/{assetId}/impairments", new { amount = 300m, reason = "storm damage" }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var eventId = (await Json(res)).GetProperty("data").GetProperty("event").GetProperty("id").GetString()!;

        // scope A: the second officer's context holds the event while it is still pending
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        await using var dbA = new ErpDbContext(options);
        var ecA = new EntityContext();
        var numbers = new NumberSeries(dbA);
        var assetsA = new AssetService(new AssetsRepository(dbA, ecA), new JournalRepository(dbA, ecA),
            numbers, new AuditRepository(dbA, new NumberSeries(dbA), ecA), ecA, dbA);
        var stale = new AssetsRepository(dbA, ecA).FindEvent(eventId);
        Assert.Equal("", stale!.JournalId); // scope A believes the event is still pending

        // scope B: the first officer approves via HTTP — one impairment journal, accumulated +300
        res = await PostAsync($"/api/v1/assets/{assetId}/impairments/{eventId}/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var journalsAfterFirst = JournalsMentioning(await GetAsync("/api/v1/journals", Admin), eventId);
        Assert.Equal(1, journalsAfterFirst);
        var accumulatedAfterFirst = AccumulatedOf(assetId);
        Assert.Equal(300m, accumulatedAfterFirst);

        // scope A: approves the SAME event from its stale pending view — the token-guarded
        // JournalId flip must match zero rows, refuse with 409, and commit NOTHING
        var (data, code, message) = assetsA.ApproveImpairment(AdminUser, assetId, eventId);
        Assert.Null(data);
        Assert.Equal("ASSET_EVENT_ALREADY_APPROVED", code);
        Assert.Contains(eventId, message);
        Assert.Equal(journalsAfterFirst, JournalsMentioning(await GetAsync("/api/v1/journals", Admin), eventId));
        Assert.Equal(accumulatedAfterFirst, AccumulatedOf(assetId));
    }

    private decimal AccumulatedOf(string assetId) =>
        GetAsync("/api/v1/assets", Admin).GetAwaiter().GetResult()
            .GetProperty("data").EnumerateArray()
            .First(a => a.GetProperty("id").GetString() == assetId)
            .GetProperty("accumulatedDepreciation").GetDecimal();

    // ---- RACE 3: over-receipt — concurrent receives must not both pass the guard ----------

    // The over-receipt guard is an aggregate check (ReceivedQty vs line.Qty) with no row to
    // hold — the T6 FIX-4 convention applies: two concurrent callers, exactly one wins, the
    // loser 409s OVER_RECEIVE from the under-lock re-check, and the AP journal + stock land once.
    [Fact]
    public async Task Concurrent_Receives_Cannot_Over_Receive_Or_Double_Post_The_Ap_Journal()
    {
        var sku = Tag("JRH-RCV");
        var poId = await SeedApprovedPo(sku, 10m, 3.00m);

        // barrier on the PO row: both racers run their guard (0 received) and park inside
        // SaveChanges on the MarkReceived UPDATE; the release must not let both through
        var (dbB, txB) = OpenBarrier();
        dbB.PurchaseOrders.FromSql($"SELECT * FROM purchase_orders WHERE id = {poId} FOR UPDATE").ToList();

        var r1 = PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty = 10m }, Proc);
        var r2 = PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty = 10m }, Proc);
        await ParkRacersBarrier(txB);
        await Task.WhenAll(r1, r2);

        var statuses = new[] { (await r1).StatusCode, (await r2).StatusCode };
        Assert.Single(statuses, s => s == HttpStatusCode.Created);
        Assert.Single(statuses, s => s == HttpStatusCode.Conflict);
        var codes = new[] { TryCode(await r1), TryCode(await r2) };
        Assert.Single(codes, c => c is null);
        Assert.Single(codes, c => c == "OVER_RECEIVE");

        // one receipt's worth of stock and exactly one AP-accrual journal — not two
        Assert.Equal(10m, StockOf(await GetAsync("/api/v1/stock", Admin), sku));
        Assert.Equal(1, JournalsMentioning(await GetAsync("/api/v1/journals", Admin), poId));
    }

    private async Task ParkRacersBarrier(IDbContextTransaction txB)
    {
        await Task.Delay(2000); // both racers are parked by construction — the barrier blocks SaveChanges
        txB.Commit();
    }

    // ---- RACE 4: same-period depreciation — one period, one run, one journal --------------

    // The per-period guard is an app-level existence check (T4 fix round 1) with no row to
    // hold and no index behind it — two concurrent runs for one period both pass it. The
    // unique index makes one-run-per-period a DB invariant; the violation maps to 409.
    [Fact]
    public async Task Concurrent_Depreciation_Runs_For_One_Period_Post_The_Journal_Once()
    {
        var assetId = await SeedActiveAsset(Tag("JRH-DEP"), 1200m);
        var periodId = $"JRH-{Today:yyyy-MM}"; // one run per period — the invariant under test

        // barrier on the asset row: both runs pass the per-period guard and park inside
        // SaveChanges on the accumulated-depreciation UPDATE; the release lets BOTH through
        var (dbB, txB) = OpenBarrier();
        dbB.Assets.FromSql($"SELECT * FROM fixed_assets WHERE id = {assetId} FOR UPDATE").ToList();

        var r1 = PostAsync("/api/v1/assets/depreciation-runs", new { periodId }, Admin);
        var r2 = PostAsync("/api/v1/assets/depreciation-runs", new { periodId }, Admin);
        await ParkRacersBarrier(txB);
        await Task.WhenAll(r1, r2);

        var statuses = new[] { (await r1).StatusCode, (await r2).StatusCode };
        Assert.Single(statuses, s => s == HttpStatusCode.OK);
        Assert.Single(statuses, s => s == HttpStatusCode.Conflict);
        var codes = new[] { TryCode(await r1), TryCode(await r2) };
        Assert.Single(codes, c => c is null);
        Assert.Single(codes, c => c == "DEPRECIATION_PERIOD_ALREADY_POSTED");

        // one run's worth of expense (1200 / 60 months = 20.00) and exactly one journal
        Assert.Equal(20m, AccumulatedOf(assetId));
        Assert.Equal(1, JournalsMentioning(await GetAsync("/api/v1/journals", Admin), periodId));
    }
}
