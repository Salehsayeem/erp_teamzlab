using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

// Fix round 1 for the MFG deep slice (review findings F1.1, F4.1, F1.2, F1.3/F2.2, F2.1 + minors):
// 1. completions receipt the output sku into stock (F1.1) and output-sku scrap succeeds;
// 2. component scrap disposal flows THROUGH WIP — Dr 5200 / Cr 1420 — and the order closes (F4.1);
// 3. backflush issue values are allocated off the completion credit basis so pure backflush
//    cannot strand a cents-level credit remainder (F1.2);
// 4. economic re-checks (ceiling, CompletedQty accumulation, availability, returnable bound)
//    run INSIDE the order-row lock against freshly-read state (F1.3/F2.2);
// 5. the InProcess→Completed transition gates on consumption reconciliation and Completed
//    orders accept reconciliation reporting (F2.1 — no more Completed trap).

namespace Erp.Api.Tests;

public class MfgFixRound1Tests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public MfgFixRound1Tests(TestApiFactory factory) => _factory = factory;

    private const string Mfg = "U-008";  // R-MFG manufacturing_officer
    private const string Admin = "U-001"; // the checker on every maker-checker gate

    private static readonly DateOnly BomFrom = new(2026, 1, 1);

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

    // an error body may only be read ONCE (the content stream is consumed) — grab the whole
    // error object when both the code and a nested list are asserted
    private static async Task<JsonElement> ErrObj(HttpResponseMessage res)
    {
        var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone().GetProperty("error");
    }

    private async Task SeedStock(string sku, decimal qty, decimal price)
    {
        var proc = "U-002";
        var res = await PostAsync("/api/v1/suppliers", new { name = $"Sup {sku}", taxId = $"TRX-{sku}", email = $"{sku.ToLowerInvariant()}@sup.test" }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Body(res)).GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = price } },
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Body(res)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { }, Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, proc)).StatusCode);
    }

    private record BomLineSeed(string ComponentSku, decimal QtyPer, decimal UnitCost, string IssuePolicy);

    private async Task<string> SeedApprovedBom(string outputSku, decimal outputQtyPer, BomLineSeed[] lines)
    {
        var res = await PostAsync("/api/v1/boms", new
        {
            outputSku, revision = 1, outputQtyPer, effectiveFrom = BomFrom, effectiveTo = (DateOnly?)null,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var bomId = (await Body(res)).GetProperty("id").GetString()!;
        foreach (var line in lines)
            Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/boms/{bomId}/lines", new
            {
                componentSku = line.ComponentSku, qtyPer = line.QtyPer, unit = "pcs",
                issuePolicy = line.IssuePolicy, unitCost = line.UnitCost,
            }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{bomId}/approvals", new { }, Admin)).StatusCode);
        return bomId;
    }

    private async Task<string> SeedReleasedOrder(string outputSku, decimal demandedQty)
    {
        var res = await PostAsync("/api/v1/production-orders", new
        {
            outputSku, demandedQty, unit = "pcs", overTolerancePct = 0m,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var orderId = (await Body(res)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Admin)).StatusCode);
        return orderId;
    }

    private async Task<decimal> StockOf(string sku)
    {
        var stock = await GetAsync("/api/v1/stock", Admin);
        return stock.GetProperty("data").EnumerateArray()
            .Where(s => s.GetProperty("itemSku").GetString() == sku)
            .Sum(s => s.GetProperty("qtyOnHand").GetDecimal());
    }

    private async Task<JsonElement> GetOrder(string orderId)
    {
        var all = await GetAsync("/api/v1/production-orders", Mfg);
        return all.GetProperty("data").EnumerateArray()
            .First(o => o.GetProperty("id").GetString() == orderId);
    }

    private async Task<JsonElement> JournalFor(string needle)
    {
        var journals = await GetAsync("/api/v1/journals", Admin);
        return journals.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("memo").GetString()!.Contains(needle, StringComparison.Ordinal));
    }

    private ProductionOrderService Service(ErpDbContext db)
    {
        var ec = new EntityContext();
        return new ProductionOrderService(new ManufacturingRepository(db, ec), new InventoryRepository(db, ec),
            new JournalRepository(db, ec), new NumberSeries(db), new AuditRepository(db, new NumberSeries(db), ec), ec, db);
    }

    private ErpDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options);

    // ---- FIX 1 (F1.1): completion receipts the output sku; output-sku scrap succeeds ----

    [Fact]
    public async Task Completion_Receipts_Output_Sku_And_Output_Scrap_Succeeds()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 10m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 10m }, Mfg)).StatusCode);

        // the GL said inventory grew — now /stock agrees: the 10 completed units are receivable
        Assert.Equal(10m, await StockOf(output));

        // scrap-reporting the output sku no longer 409s INSUFFICIENT_STOCK (fix-1 companion)
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = output, qty = 2m, reasonCode = "DAMAGED", disposition = "Disposal",
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(8m, await StockOf(output)); // disposal drew the finished goods down
    }

    // ---- FIX 2 (F4.1): component scrap disposal flows through WIP and the order closes ----

    [Fact]
    public async Task Component_Scrap_Disposal_Flows_Through_Wip_And_The_Order_Closes()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        // 1.2 units of material per output: 10 issued + 2 disposed = the 12-unit BOM expectation
        await SeedApprovedBom(output, 1m, [new BomLineSeed(comp, 1.2m, 1.00m, "Manual")]);
        await SeedStock(comp, 12m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 10m }, Mfg)).StatusCode);
        Assert.Equal(2m, await StockOf(comp));

        // disposal: Dr 5200 / Cr **1420 WIP** at the component's issue cost basis (was Cr 1400)
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = comp, qty = 2m, reasonCode = "DAMAGED", disposition = "Disposal",
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(0m, await StockOf(comp)); // stock math consistent: 12 − 10 − 2
        var scrapId = (await Body(res)).GetProperty("scrap").GetProperty("id").GetString()!;
        var journal = await JournalFor(scrapId);
        Assert.Equal("5200", journal.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(2.00m, journal.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("1420", journal.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(2.00m, journal.GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        // completion credits WIP at the FULL BOM cost (10 × 1.2 = 12.00) — disposal consumed the rest
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 10m }, Mfg)).StatusCode);
        Assert.Equal(10m, await StockOf(output));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).StatusCode);

        // WIP nets zero: debits 10 (issues) + 2 (disposal through WIP) = credits 12 (full BOM)
        var check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.Equal(0, check.GetProperty("items").GetArrayLength());
        Assert.Equal(12m, check.GetProperty("wipDebits").GetDecimal());
        Assert.Equal(12m, check.GetProperty("wipCredits").GetDecimal());
        Assert.Equal(0m, check.GetProperty("wipRemainder").GetDecimal());

        // the scrap story no longer ends in an unclosable order
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)).StatusCode);
    }

    // ---- FIX 3 (F1.2): pure-backflush rounding reconciles to the completion credit ----

    [Fact]
    public async Task Pure_Backflush_Rounding_Sums_To_The_Completion_Credit_Basis()
    {
        var output = Tag("FG");
        var a = Tag("RM");
        var b = Tag("RM");
        // credit = round(0.20/3, 2) = 0.07 but per-line raw values round to 0.03+0.03 = 0.06 —
        // the last line plugs so Σ debit equals the credit basis
        await SeedApprovedBom(output, 3m, [new BomLineSeed(a, 1m, 0.10m, "Backflush"), new BomLineSeed(b, 1m, 0.10m, "Backflush")]);
        await SeedStock(a, 10m, 1m);
        await SeedStock(b, 10m, 1m);
        var orderId = await SeedReleasedOrder(output, 1m);

        var res = await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var data = await Body(res);
        Assert.Equal(0.07m, data.GetProperty("journal").GetProperty("lines")[1].GetProperty("credit").GetDecimal());
        var bf = data.GetProperty("backflush").EnumerateArray().Select(x => x.GetProperty("lineValue").GetDecimal()).ToList();
        Assert.Equal(2, bf.Count);
        Assert.Equal(0.07m, bf[0] + bf[1]); // Σ debit == completion credit — no stranded cent

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).StatusCode);

        var check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.Equal(0, check.GetProperty("items").GetArrayLength());
        Assert.Equal(0m, check.GetProperty("wipRemainder").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)).StatusCode);
    }

    // ---- FIX 4 (F1.3/F2.2): economic re-checks re-read state INSIDE the lock ----

    [Fact]
    public async Task Completion_After_External_Completion_ReReads_CompletedQty_Under_Lock()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [new BomLineSeed(comp, 1m, 1.00m, "Backflush")]);
        await SeedStock(comp, 50m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        // scope A loads the order believing CompletedQty is 0
        await using var dbA = NewDb();
        var serviceA = Service(dbA);
        serviceA.List(null, output);

        // an external completion commits 3 while A is in flight
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 3m }, Mfg)).StatusCode);

        // A reports 3 from its stale snapshot: the locked re-read must account for the 3 —
        // the ledger accumulates to 6, it must not lose the external units
        var (data, code, _) = serviceA.ReportCompletion(
            new Erp.Api.CurrentUser("U-008", "mfg@teamzlab.dev", "Manufacturing Officer", "R-MFG", new[] { "mfg:write", "mfg:override" }),
            orderId, new Erp.Api.Dtos.CompletionRequest(3m, null));
        Assert.Null(code);
        Assert.NotNull(data);

        var order = await GetOrder(orderId);
        Assert.Equal(6m, order.GetProperty("completedQty").GetDecimal());
        Assert.Equal(44m, await StockOf(comp)); // both reports backflushed 3 each: 50 − 3 − 3 — nothing lost
    }

    [Fact]
    public async Task Return_After_External_Return_ReReads_The_Returnable_Bound_Under_Lock()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 100m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        var issueId = (await Body(await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 10m }, Mfg)))
            .GetProperty("issue").GetProperty("id").GetString()!;

        // scope A caches the issue from before the external return
        await using var dbA = NewDb();
        var serviceA = Service(dbA);
        var cached = new ManufacturingRepository(dbA, new EntityContext()).FindIssue(orderId, issueId);
        Assert.NotNull(cached);

        // an external return of 4 commits first — only 6 remain returnable
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId, qty = 4m }, Mfg)).StatusCode);

        // the in-flight return of 7 must re-read the bound under the lock and refuse
        var (data, code, message) = serviceA.ReturnIssue(
            new Erp.Api.CurrentUser("U-008", "mfg@teamzlab.dev", "Manufacturing Officer", "R-MFG", new[] { "mfg:write" }),
            orderId, new Erp.Api.Dtos.IssueReturnRequest(issueId, 7m));
        Assert.Equal("ISSUE_RETURN_OVER", code);
        Assert.Null(data);
        Assert.Contains("6", message);

        // and the bound holds from the API surface too
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId, qty = 7m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ISSUE_RETURN_OVER", ErrCode(res));
        Assert.Equal(94m, await StockOf(comp)); // 100 − 10 + 4: the refused return moved nothing
    }

    // ---- FIX 5 (F2.1): Completed is gated and repairable ----

    [Fact]
    public async Task Completed_Transition_Gates_On_Consumption_And_Completed_Is_Repairable()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 20m, 1m);
        var orderId = await SeedReleasedOrder(output, 5m);

        // report-all with one unit short: exactly the trap story
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 4m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 5m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);

        // InProcess → Completed refuses while consumption is unreconciled, naming the mismatch
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var err = await ErrObj(res);
        Assert.Equal("MFG_CLOSE_BLOCKED", err.GetProperty("code").GetString());
        var items = err.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString()!).ToList();
        Assert.Contains($"consumption:{comp}", items);

        // repair: the last unit is issued — at InProcess the gate passes...
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).StatusCode);

        // ...and Completed is not a trap: reporting stays open there, a drift is repairable AT
        // Completed, and the reconciled order closes without a residual
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)).StatusCode);
        var check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.True(check.GetProperty("open").GetBoolean());

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg)).StatusCode); // mfg:override over plan
        check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.Equal(0, check.GetProperty("items").GetArrayLength());
        Assert.Equal(0m, check.GetProperty("wipRemainder").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)).StatusCode);
    }

    // ---- MINOR (b): post-release amendment natural-key race maps to 409 AMENDMENT_CONFLICT ----

    [Fact]
    public async Task Amendment_Natural_Key_Race_Maps_To_409_Amendment_Conflict()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        var orderId = await SeedReleasedOrder(output, 4m);

        // the race simulator: a row that the scoped version-count cannot see but whose PK the
        // next amendment insert collides with (concurrent amendments both compute {orderId}:v1)
        await using var db = NewDb();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO order_amendments (id, tenant_id, legal_entity_id, order_id, field, old_value, new_value, reason, changed_by, changed_at) " +
            "VALUES ({0} || ':v1', 'other-tenant', 'other-entity', {0}, 'PlannedQty', '4', '5', 'lost the race', 'U-000', now())", orderId);

        var service = Service(db);
        var (data, code, message) = service.Transition(
            new Erp.Api.CurrentUser("U-008", "mfg@teamzlab.dev", "Manufacturing Officer", "R-MFG", new[] { "mfg:write" }),
            orderId, new Erp.Api.Dtos.TransitionOrderRequest(ProductionOrderStatus.Released, 5m, "legitimate amendment"));
        Assert.Equal("AMENDMENT_CONFLICT", code);
        Assert.Null(data);
        Assert.Contains("collided", message);
        Assert.Equal("Released", (await GetOrder(orderId)).GetProperty("status").GetString());
    }
}
