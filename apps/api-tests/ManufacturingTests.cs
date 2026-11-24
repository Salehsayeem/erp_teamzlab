using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

// MFG deep slice (ERP-MFG-007/008/010/013/014/015/022/041/042/043/067/069, rulings R-MFG1..11):
// BOM revisions, production-order lifecycle, material issues/returns, completions with backflush,
// scrap, close/reopen — every posting a balanced journal + real stock movement in one SaveChanges.
// Each test seeds its own SKUs (the shared fixture database is sequential per class).

namespace Erp.Api.Tests;

public class ManufacturingTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public ManufacturingTests(TestApiFactory factory) => _factory = factory;

    private const string Mfg = "U-008";  // R-MFG manufacturing_officer
    private const string Admin = "U-001"; // R-ADMIN — the checker on every maker-checker gate

    private static readonly DateOnly BomFrom = new(2026, 1, 1);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

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

    private static string Tag(string prefix) => $"{prefix}{Guid.NewGuid():N}".ToUpperInvariant()[..12];

    private async Task<JsonElement> Body(HttpResponseMessage res)
    {
        var json = await Json(res);
        Assert.True(json.GetProperty("success").GetBoolean(), $"expected success envelope: {json.GetRawText()}");
        return json.GetProperty("data");
    }

    private static JsonElement Error(HttpResponseMessage res) => Json(res).GetAwaiter().GetResult().GetProperty("error");

    private static string ErrCode(HttpResponseMessage res) => Error(res).GetProperty("code").GetString()!;

    // ---- shared flow seeds -----------------------------------------------------------

    // supplier + approved PO + receipt → sellable stock for the sku (LOC-A1)
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

    // draft revision + lines, then admin approves (approver ≠ author U-008)
    private async Task<string> SeedApprovedBom(string outputSku, int revision, decimal outputQtyPer, BomLineSeed[] lines,
        DateOnly? from = null, DateOnly? to = null)
    {
        var bomId = await CreateBom(outputSku, revision, outputQtyPer, from, to);
        foreach (var line in lines)
            Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/boms/{bomId}/lines", new
            {
                componentSku = line.ComponentSku,
                qtyPer = line.QtyPer,
                unit = "pcs",
                issuePolicy = line.IssuePolicy,
                unitCost = line.UnitCost,
            }, Mfg)).StatusCode);
        var res = await PostAsync($"/api/v1/boms/{bomId}/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return bomId;
    }

    private async Task<string> CreateBom(string outputSku, int revision, decimal outputQtyPer,
        DateOnly? from = null, DateOnly? to = null)
    {
        var res = await PostAsync("/api/v1/boms", new
        {
            outputSku, revision, outputQtyPer, effectiveFrom = from ?? BomFrom, effectiveTo = to,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Body(res)).GetProperty("id").GetString()!;
    }

    private async Task<string> SeedOrder(string outputSku, decimal demandedQty, decimal overTolerancePct = 0m)
    {
        var res = await PostAsync("/api/v1/production-orders", new
        {
            outputSku, demandedQty, unit = "pcs", overTolerancePct,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Body(res)).GetProperty("id").GetString()!;
    }

    // Draft → Planned (U-008) → Released (admin checker)
    private async Task<string> SeedReleasedOrder(string outputSku, decimal demandedQty, decimal overTolerancePct = 0m)
    {
        var orderId = await SeedOrder(outputSku, demandedQty, overTolerancePct);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg)).StatusCode);
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return orderId;
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

    private async Task<bool> OutboxHas(string type)
    {
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        return outbox.GetProperty("data").EnumerateArray()
            .Any(e => e.GetProperty("type").GetString() == type);
    }

    private async Task<decimal> StockOf(string sku)
    {
        var stock = await GetAsync("/api/v1/stock", Admin);
        return stock.GetProperty("data").EnumerateArray()
            .Where(s => s.GetProperty("itemSku").GetString() == sku)
            .Sum(s => s.GetProperty("qtyOnHand").GetDecimal());
    }

    // ---- 1. 014 AC1: approved revisions are immutable; changes create a successor ----

    [Fact]
    public async Task Approved_Bom_Revision_Is_Immutable_Changes_Create_Successor()
    {
        var out1 = Tag("FG");
        var comp = Tag("RM");
        var bomId = await SeedApprovedBom(out1, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);

        // approved revision never accepts new lines
        var res = await PostAsync($"/api/v1/boms/{bomId}/lines", new
        {
            componentSku = Tag("RM"), qtyPer = 1m, unit = "pcs", issuePolicy = "Manual", unitCost = 1m,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BOM_STATE_ERROR", ErrCode(res));

        // the author cannot approve their own draft (014 separable roles)
        var draft = await CreateBom(Tag("FG"), 1, 1m);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/boms/{draft}/lines", new
        {
            componentSku = comp, qtyPer = 1m, unit = "pcs", issuePolicy = "Manual", unitCost = 2m,
        }, Mfg)).StatusCode);
        res = await PostAsync($"/api/v1/boms/{draft}/approvals", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BOM_APPROVAL_ERROR", ErrCode(res));

        // duplicate component line inside one draft revision → 409
        var dupe = await CreateBom(Tag("FG"), 1, 1m);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/boms/{dupe}/lines", new
        {
            componentSku = comp, qtyPer = 1m, unit = "pcs", issuePolicy = "Manual", unitCost = 2m,
        }, Mfg)).StatusCode);
        res = await PostAsync($"/api/v1/boms/{dupe}/lines", new
        {
            componentSku = comp, qtyPer = 2m, unit = "pcs", issuePolicy = "Manual", unitCost = 3m,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BOM_LINE_DUPLICATE", ErrCode(res));

        // a change is a NEW revision: retire the approved one, then approve its successor;
        // the approved predecessor row itself is never mutated
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{bomId}/retirements", new { reason = "replaced" }, Mfg)).StatusCode);
        var successor = await SeedApprovedBom(out1, 2, 1m, [new BomLineSeed(comp, 2m, 2.50m, "Manual")]);
        Assert.NotEqual(bomId, successor);

        var boms = await GetAsync($"/api/v1/boms?outputSku={out1}", Mfg);
        var rows = boms.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Retired", rows.Single(r => r.GetProperty("id").GetString() == bomId).GetProperty("status").GetString());
        Assert.Equal("Approved", rows.Single(r => r.GetProperty("id").GetString() == successor).GetProperty("status").GetString());
        Assert.Single(rows.Single(r => r.GetProperty("id").GetString() == successor).GetProperty("lines").EnumerateArray());

        // OutputQtyPer <= 0 → 400 (015 AC2 basis)
        res = await PostAsync("/api/v1/boms", new
        {
            outputSku = Tag("FG"), revision = 1, outputQtyPer = 0m, effectiveFrom = BomFrom, effectiveTo = (DateOnly?)null,
        }, Mfg);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrCode(res));

        // boms:write is required — a sales officer gets 403
        res = await PostAsync("/api/v1/boms", new
        {
            outputSku = Tag("FG"), revision = 1, outputQtyPer = 1m, effectiveFrom = BomFrom, effectiveTo = (DateOnly?)null,
        }, "U-004");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", ErrCode(res));
    }

    // ---- 2. 014 AC2 + edge: effective selection returns one revision; overlaps rejected ----

    [Fact]
    public async Task Bom_Effective_Selection_Returns_One_Revision_Overlap_Is_Rejected()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        // revision 1 covers Jan..Jun 2026 only — a FINITE interval leaves room for successors
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")],
            from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 6, 30));

        // an overlapping interval on the same output sku → 409 BOM_EFFECTIVE_OVERLAP
        var res = await PostAsync("/api/v1/boms", new
        {
            outputSku = output, revision = 2, outputQtyPer = 1m,
            effectiveFrom = new DateOnly(2026, 4, 1), effectiveTo = (DateOnly?)null,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BOM_EFFECTIVE_OVERLAP", ErrCode(res));

        // a DISJOINT interval (Jul 2026 onward) coexists — effective selection stays interval-based
        var future = await CreateBom(output, 3, 1m, from: new DateOnly(2026, 7, 1));
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/boms/{future}/lines", new
        {
            componentSku = comp, qtyPer = 1m, unit = "pcs", issuePolicy = "Manual", unitCost = 2m,
        }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{future}/approvals", new { }, Admin)).StatusCode);

        // an order only resolves revisions that are APPROVED — a draft-only sku is not effective
        var draftSku = Tag("FG");
        await CreateBom(draftSku, 1, 1m); // stays Draft
        res = await PostAsync("/api/v1/production-orders", new { outputSku = draftSku, demandedQty = 1m, unit = "pcs", overTolerancePct = 0m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BOM_NOT_EFFECTIVE", ErrCode(res));

        // the approved sku resolves exactly ONE revision for today: rev 1 is out of interval, rev 3 covers now
        var orderId = await SeedOrder(output, 1m);
        var order = await GetOrder(orderId);
        Assert.Equal(future, order.GetProperty("plannedBomRevisionId").GetString());
    }

    // ---- 3. 010 AC1: the release gate lists failed prerequisites machine-readable ----

    [Fact]
    public async Task Order_Release_Gate_Lists_Failed_Prerequisites_Machine_Readable()
    {
        // order created while rev 1 was effective, retired since, still Draft → TWO failures at once
        var output = Tag("FG");
        var comp = Tag("RM");
        var rev1 = await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        var orderId = await SeedOrder(output, 2m); // creation resolves rev1
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{rev1}/retirements", new { reason = "gate probe" }, Mfg)).StatusCode);
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var err = Error(res);
        Assert.Equal("MFG_RELEASE_BLOCKED", err.GetProperty("code").GetString());
        var prereqs = err.GetProperty("prerequisites").EnumerateArray()
            .Select(p => p.GetProperty("code").GetString()).ToList();
        Assert.Contains("MFG_STATE_ERROR", prereqs);
        Assert.Contains("BOM_NOT_EFFECTIVE", prereqs);

        // approved bom WITH lines, order still Draft → only the state failure remains
        var output2 = Tag("FG");
        await SeedApprovedBom(output2, 1, 1m, [new BomLineSeed(Tag("RM"), 1m, 2.00m, "Manual")]);
        var draftOrder = await SeedOrder(output2, 2m);
        res = await PostAsync($"/api/v1/production-orders/{draftOrder}/releases", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        prereqs = Error(res).GetProperty("prerequisites").EnumerateArray()
            .Select(p => p.GetProperty("code").GetString()).ToList();
        Assert.Equal(["MFG_STATE_ERROR"], prereqs);

        // approved bom WITHOUT lines, order Planned → only the lines prerequisite fires
        var emptySku = Tag("FG");
        var emptyBom = await CreateBom(emptySku, 1, 1m);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{emptyBom}/approvals", new { }, Admin)).StatusCode);
        var bareOrder = await SeedOrder(emptySku, 2m);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{bareOrder}/transitions", new { status = "Planned" }, Mfg)).StatusCode);
        res = await PostAsync($"/api/v1/production-orders/{bareOrder}/releases", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        prereqs = Error(res).GetProperty("prerequisites").EnumerateArray()
            .Select(p => p.GetProperty("code").GetString()).ToList();
        Assert.Equal(["BOM_NO_LINES"], prereqs);
        Assert.DoesNotContain("MFG_STATE_ERROR", prereqs);
    }

    // ---- 4. 022 AC2/AC3: release stores the revision and shows the planned→released delta ----

    [Fact]
    public async Task Release_Stores_Released_Revision_And_Shows_Planned_Vs_Released_Delta()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        var rev1 = await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        var orderId = await SeedOrder(output, 4m);
        Assert.Equal(rev1, (await GetOrder(orderId)).GetProperty("plannedBomRevisionId").GetString());

        // the creator cannot release their own order (010 AC3 maker-checker)
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg)).StatusCode);
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("MFG_RELEASE_ERROR", ErrCode(res));

        // a successor revision becomes effective before release → the delta is shown
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{rev1}/retirements", new { reason = "cost change" }, Mfg)).StatusCode);
        var rev2 = await SeedApprovedBom(output, 2, 1m, [new BomLineSeed(comp, 1m, 2.50m, "Manual")]);

        res = await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var release = (await Body(res)).GetProperty("release");
        Assert.Equal(rev1, release.GetProperty("plannedBomRevisionId").GetString());
        Assert.Equal(rev2, release.GetProperty("releasedBomRevisionId").GetString());
        Assert.True(release.GetProperty("revisionChanged").GetBoolean());

        var order = await GetOrder(orderId);
        Assert.Equal(rev2, order.GetProperty("releasedBomRevisionId").GetString());
        Assert.Equal("Released", order.GetProperty("status").GetString());
        Assert.Equal(4m, order.GetProperty("releasedQty").GetDecimal());
        Assert.Equal(4m, order.GetProperty("remainingQty").GetDecimal()); // planned − completed

        // issues cost from the FROZEN released revision (2.50), not the planned one (2.00)
        await SeedStock(comp, 10m, 1m);
        var issue = await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg);
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
        Assert.Equal(2.50m, (await Body(issue)).GetProperty("issue").GetProperty("unitCost").GetDecimal());
    }

    // ---- 5. 007 AC2: guarded transition map ----

    [Fact]
    public async Task Illegal_Order_Transition_Returns_409_MFG_TRANSITION_ERROR()
    {
        var output = Tag("FG");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(Tag("RM"), 1m, 2.00m, "Manual")]);

        var orderId = await SeedOrder(output, 2m);
        // skips are illegal: Draft → Released/InProcess/Completed/Closed (release and close have gates)
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Released" }, Mfg)).StatusCode);
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Released" }, Mfg)));
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)));
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)));
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Closed" }, Mfg)));

        // the one legal Draft move is Planned; repeating it is illegal
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg)).StatusCode);
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg)));
        // Planned → InProcess is a skip (release gate first)
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)));

        // cancellation is legal only pre-execution, needs a reason, and is terminal
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Cancelled" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Cancelled", reason = "demand gone" }, Mfg)).StatusCode);
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg)));

        // no skips after release either: Released → Completed must pass through InProcess
        var released = await SeedReleasedOrder(output, 2m);
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{released}/transitions", new { status = "Completed" }, Mfg)));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{released}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{released}/transitions", new { status = "InProcess" }, Mfg)));
        Assert.Equal("MFG_TRANSITION_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{released}/transitions", new { status = "Cancelled", reason = "late" }, Mfg)));
    }

    // ---- 6. 008 AC2: over-tolerance completion needs mfg:override ----

    [Fact]
    public async Task Completion_Over_Tolerance_Requires_Mfg_Override()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 100m, 1m);
        var orderId = await SeedReleasedOrder(output, 5m, overTolerancePct: 0m);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 5m }, Mfg)).StatusCode);

        // the 6th unit breaches the zero tolerance: the R-MFG officer carries mfg:override (brief
        // §Permissions), so the authorized over-production posts and the ledger shows it
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg)).StatusCode);
        var order = await GetOrder(orderId);
        Assert.Equal(6m, order.GetProperty("completedQty").GetDecimal());
        Assert.Equal(-1m, order.GetProperty("remainingQty").GetDecimal()); // planned − completed, never stored twice

        // a 10% tolerance lets the officer land 11 on a plan of 10 within tolerance
        var tolerant = Tag("FG");
        var tolerantComp = Tag("RM");
        await SeedApprovedBom(tolerant, 1, 1m, [new BomLineSeed(tolerantComp, 1m, 1.00m, "Manual")]);
        await SeedStock(tolerantComp, 100m, 1m);
        var tolerantOrder = await SeedReleasedOrder(tolerant, 10m, overTolerancePct: 10m);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{tolerantOrder}/completions", new { qty = 11m }, Mfg)).StatusCode);

        // quantity ledger exposed (008 AC1)
        var ledger = await GetOrder(tolerantOrder);
        Assert.Equal(10m, ledger.GetProperty("demandedQty").GetDecimal());
        Assert.Equal(10m, ledger.GetProperty("plannedQty").GetDecimal());
        Assert.Equal(10m, ledger.GetProperty("releasedQty").GetDecimal());
        Assert.Equal(11m, ledger.GetProperty("completedQty").GetDecimal());

        // the gate itself (008 AC2): a principal holding mfg:write WITHOUT mfg:override is
        // refused with 409 MFG_OVER_TOLERANCE. No seeded role carries that combination (R-MFG
        // holds every gate, R-MFG9), so — like the double-post race test — the suite constructs
        // the principal at the service boundary against a dedicated DbContext scope.
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        await using var db = new ErpDbContext(options);
        var ec = new EntityContext();
        var service = new ProductionOrderService(new ManufacturingRepository(db, ec), new InventoryRepository(db, ec),
            new JournalRepository(db, ec), new NumberSeries(db), new AuditRepository(db, new NumberSeries(db), ec), ec, db);
        var editor = new Erp.Api.CurrentUser("U-008", "mfg@teamzlab.dev", "Manufacturing Officer", "R-MFG",
            new[] { "boms:read", "boms:write", "mfg:read", "mfg:write" }); // no mfg:override
        var (data, code, message) = service.ReportCompletion(editor, tolerantOrder, new Erp.Api.Dtos.CompletionRequest(1m, null));
        Assert.Equal("MFG_OVER_TOLERANCE", code);
        Assert.Null(data);
        Assert.Contains("mfg:override", message);
        // the refused report left the ledger untouched
        Assert.Equal(11m, (await GetOrder(tolerantOrder)).GetProperty("completedQty").GetDecimal());
    }

    // ---- 7. 042 AC1/AC2 + 069 golden: issue moves stock, posts Dr 1420 / Cr 1400 ----

    [Fact]
    public async Task Material_Issue_Moves_Stock_And_Posts_Balanced_Dr_1420_Cr_1400()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 50m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        var res = await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 10m }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var data = await Body(res);
        Assert.Equal(2.00m, data.GetProperty("issue").GetProperty("unitCost").GetDecimal());
        Assert.Equal(20.00m, data.GetProperty("issue").GetProperty("lineValue").GetDecimal());
        Assert.NotEmpty(data.GetProperty("issue").GetProperty("journalId").GetString()!);

        // stock really moved: 50 − 10
        Assert.Equal(40m, await StockOf(comp));

        // golden journal: Dr 1420 WIP 20.00 / Cr 1400 Inventory 20.00
        var journal = await JournalFor(orderId);
        Assert.Equal("1420", journal.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(20.00m, journal.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal(0m, journal.GetProperty("lines")[0].GetProperty("credit").GetDecimal());
        Assert.Equal("1400", journal.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(0m, journal.GetProperty("lines")[1].GetProperty("debit").GetDecimal());
        Assert.Equal(20.00m, journal.GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        Assert.True(await OutboxHas("mfg.issue.posted"));
    }

    // ---- 8. 042 AC3: a return can never exceed the issue's remaining returnable qty ----

    [Fact]
    public async Task Issue_Return_Cannot_Exceed_Remaining_Returnable_Quantity()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 100m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        var issueId = (await Body(await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 10m }, Mfg)))
            .GetProperty("issue").GetProperty("id").GetString()!;

        // return 4 of the 10 — stock comes back, WIP reverses Dr 1400 / Cr 1420 8.00
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId, qty = 4m }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(94m, await StockOf(comp));
        var journal = await JournalFor($"of issue {issueId}");
        Assert.Equal("1400", journal.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(8.00m, journal.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("1420", journal.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(8.00m, journal.GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        // 7 more would exceed the remaining 6 → 409
        res = await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId, qty = 7m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ISSUE_RETURN_OVER", ErrCode(res));

        // the remaining 6 succeed; then the issue is fully returned and dry
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId, qty = 6m }, Mfg)).StatusCode);
        res = await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId, qty = 1m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ISSUE_RETURN_OVER", ErrCode(res));

        // unknown issue id → 404
        res = await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId = "ISS-9999", qty = 1m }, Mfg);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---- 9. 043 AC1/AC2: backflush computes from the released revision and links the completion ----

    [Fact]
    public async Task Backflush_Completion_Computes_From_Released_Revision_And_Posts_Issues()
    {
        var output = Tag("FG");
        var backflushComp = Tag("RM");
        var manualComp = Tag("PK");
        await SeedApprovedBom(output, 1, 10m,
        [
            new BomLineSeed(backflushComp, 5m, 2.00m, "Backflush"), // 5 per batch of 10 = 0.5 per unit
            new BomLineSeed(manualComp, 1m, 1.00m, "Manual"),       // 1 per batch of 10 = 0.1 per unit
        ]);
        await SeedStock(backflushComp, 100m, 1m);
        await SeedStock(manualComp, 100m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        // the manual line is issued by hand: 1 unit covers the 10-unit batch
        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = manualComp, qty = 1m }, Mfg)).StatusCode);

        var res = await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 10m, note = "batch 1" }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var data = await Body(res);

        // planned unit cost = Σ(QtyPer × UnitCost) ÷ OutputQtyPer = (10 + 1) ÷ 10 = 1.10
        Assert.Equal(1.10m, data.GetProperty("plannedUnitCost").GetDecimal());
        // completion journal Dr 1400 11.00 / Cr 1420 11.00
        Assert.Equal(11.00m, data.GetProperty("journal").GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("1400", data.GetProperty("journal").GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal("1420", data.GetProperty("journal").GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(11.00m, data.GetProperty("journal").GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        // the backflush line auto-issued 10 × 5/10 = 5 units @ 2.00 = 10.00
        var backflush = data.GetProperty("backflush").EnumerateArray().Single();
        Assert.Equal(backflushComp, backflush.GetProperty("componentSku").GetString());
        Assert.Equal(5m, backflush.GetProperty("qty").GetDecimal());
        Assert.Equal(10.00m, backflush.GetProperty("lineValue").GetDecimal());
        Assert.Equal(95m, await StockOf(backflushComp));

        // the auto issue links the completion (043 AC2)
        Assert.NotEmpty(data.GetProperty("completion").GetProperty("id").GetString()!);
        Assert.True(await OutboxHas("mfg.completion.posted"));

        // close-check reconciles exactly: output 10/10, backflush 5, manual 1, wip 11 − 11 = 0
        var check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.Equal(0, check.GetProperty("items").GetArrayLength());
        Assert.Equal(0m, check.GetProperty("wipRemainder").GetDecimal());
    }

    // ---- 10. 067 AC1/AC2: scrap needs a reason; disposal moves stock, return-to-stock does not ----

    [Fact]
    public async Task Scrap_Report_Requires_Reason_Code_And_Moves_Stock_On_Disposal()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 50m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        // 067 AC1: a scrap report without a reason code is a 400
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = comp, qty = 2m, reasonCode = (string?)null, disposition = "Disposal",
        }, Mfg);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrCode(res));

        // disposal: physical stock −2; the scrapped component's cost leaves WIP —
        // journal Dr 5200 4.00 / Cr 1420 4.00 (fix round 1: disposal flows THROUGH WIP)
        res = await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = comp, qty = 2m, reasonCode = "DAMAGED", disposition = "Disposal",
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(48m, await StockOf(comp));
        var scrapId = (await Body(res)).GetProperty("scrap").GetProperty("id").GetString()!;
        var journal = await JournalFor(scrapId);
        Assert.Equal("5200", journal.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(4.00m, journal.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("1420", journal.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(4.00m, journal.GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        // return-to-stock: no movement, no journal
        var journalsBefore = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();
        res = await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = comp, qty = 1m, reasonCode = "WRONG_PICK", disposition = "ReturnToStock",
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(48m, await StockOf(comp));
        Assert.Equal(journalsBefore, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());

        Assert.True(await OutboxHas("mfg.scrap.posted"));
    }

    // ---- 11. 013 AC1: close blocks while quantities are unreconciled and names each one ----

    [Fact]
    public async Task Close_Blocks_While_Unreconciled_Quantities_Remain_And_Names_Them()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 20m, 1m);
        var orderId = await SeedReleasedOrder(output, 5m);

        // the operator issues only 4 of the needed 5, then reports all 5 outputs
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 4m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 5m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);

        // fix round 1 (F2.1): the InProcess → Completed transition is GATED on consumption
        // reconciliation — the report-all-then-stall trap is refused at the gate, naming the mismatch
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var blocked = Error(res);
        Assert.Equal("MFG_CLOSE_BLOCKED", blocked.GetProperty("code").GetString());
        Assert.Contains(blocked.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString()!), c => c == $"consumption:{comp}");

        // and the close itself refuses too — the order is still InProcess, not Completed
        res = await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("MFG_STATE_ERROR", ErrCode(res));

        // repair while InProcess: the last unit is issued, the gate passes
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).StatusCode);

        // Completed orders are not a trap anymore: reporting stays open for reconciliation, so a
        // drift AT Completed (an extra issue here) is repairable at Completed — the close still gates
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)).StatusCode);
        res = await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains(Error(res).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString()!), c => c == $"consumption:{comp}");

        // repair AT Completed: the matching completion reconciles the drifted ledger
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg)).StatusCode);

        // everything reconciles → the balanced ledger closes without a residual (maker-checker: the
        // creator cannot close their own order, so the checker does)
        var checkRes = await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin);
        var check = checkRes.GetProperty("data");
        Assert.False(check.GetProperty("open").GetBoolean());
        Assert.Equal(0m, check.GetProperty("wipRemainder").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)).StatusCode);
        Assert.True(await OutboxHas("mfg.issue.posted"));
    }

    // ---- 11b. 013 AC1 + R-MFG5: a debit WIP remainder closes only as an acknowledged residual ----

    [Fact]
    public async Task Debit_Wip_Remainder_Closes_Via_Acknowledged_Residual_Journal()
    {
        var output = Tag("FG");
        var a = Tag("RM");
        var b = Tag("RM");
        // Σ(QtyPer × UnitCost) = 0.25 but each issue line rounds to 0.13 → 0.01 debit remainder
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(a, 1m, 0.125m, "Manual"), new BomLineSeed(b, 1m, 0.125m, "Manual")]);
        await SeedStock(a, 10m, 1m);
        await SeedStock(b, 10m, 1m);
        var orderId = await SeedReleasedOrder(output, 1m);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = a, qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = b, qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).StatusCode);

        // quantities reconcile but the ledger carries a 0.01 debit remainder
        var check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.DoesNotContain(check.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString()!), c => c != "WIP_REMAINDER");
        Assert.Equal(0.01m, check.GetProperty("wipRemainder").GetDecimal());

        // without the explicit acknowledgment the remainder item blocks the close
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("MFG_CLOSE_BLOCKED", ErrCode(res));

        // with it, the explicit residual journal posts Dr 5200 / Cr 1420 0.01
        res = await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { residualAcknowledgment = "rounding absorbed to scrap" }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var residual = (await Body(res)).GetProperty("residualJournal");
        Assert.Equal("5200", residual.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(0.01m, residual.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("1420", residual.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(0.01m, residual.GetProperty("lines")[1].GetProperty("credit").GetDecimal());
    }

    // ---- 12. 013 AC2: a closed order rejects every further report ----

    [Fact]
    public async Task Closed_Order_Rejects_Further_Reporting()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 50m, 1m);
        var orderId = await SeedReleasedOrder(output, 2m);

        var issueId = (await Body(await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 2m }, Mfg)))
            .GetProperty("issue").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 2m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)).StatusCode);

        Assert.Equal("ORDER_CLOSED", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)));
        Assert.Equal("ORDER_CLOSED", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/issue-returns", new { issueId, qty = 1m }, Mfg)));
        Assert.Equal("ORDER_CLOSED", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg)));
        Assert.Equal("ORDER_CLOSED", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = comp, qty = 1m, reasonCode = "DAMAGED", disposition = "Disposal",
        }, Mfg)));
        // close is idempotent-blocked too
        Assert.Equal("MFG_STATE_ERROR", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)));
    }

    // ---- 13. 013 AC3: reopen preserves the prior close as a linked audit event ----

    [Fact]
    public async Task Reopen_Preserves_The_Prior_Close_As_A_Linked_Audit_Event()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 50m, 1m);
        var orderId = await SeedReleasedOrder(output, 2m);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 2m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 2m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin)).StatusCode);

        // a reopen without a reason never reaches the service
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/reopenings", new { }, Admin);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrCode(res));

        res = await PostAsync($"/api/v1/production-orders/{orderId}/reopenings", new { reason = "missed scrap report" }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var order = (await Body(res)).GetProperty("order");
        Assert.Equal("InProcess", order.GetProperty("status").GetString());
        // the prior close is preserved, never erased
        Assert.Equal(Admin, order.GetProperty("closedBy").GetString());
        Assert.False(order.GetProperty("closedAt").GetString()!.StartsWith("0001", StringComparison.Ordinal));
        Assert.Equal(Admin, order.GetProperty("reopenedBy").GetString());

        // reporting works again on the reopened order
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = comp, qty = 1m, reasonCode = "FOUND_DURING_RECHECK", disposition = "Disposal",
        }, Mfg)).StatusCode);

        // the reopen is a linked audit event referencing the prior close
        var audits = await GetAsync("/api/v1/audit-events", Admin);
        Assert.Contains(audits.GetProperty("data").EnumerateArray(),
            a => a.GetProperty("entityId").GetString() == orderId && a.GetProperty("action").GetString() == "reopen");
    }

    // ---- 14. FinanceGuards on every posting path ----

    [Fact]
    public async Task Posting_Into_Closed_Period_Returns_409_PERIOD_LOCKED()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 50m, 1m);
        var orderId = await SeedReleasedOrder(output, 2m);

        // close today's (open) period for the probe, then restore it — postings carry no client
        // date, so the lock must be exercised on the calendar that covers "now"
        var periods = await GetAsync("/api/v1/fiscal-periods", Admin);
        var today = periods.GetProperty("data").EnumerateArray().Single(p =>
            p.GetProperty("status").GetString() == "Open"
            && DateOnly.Parse(p.GetProperty("startDate").GetString()!) <= Today
            && DateOnly.Parse(p.GetProperty("endDate").GetString()!) >= Today);
        var periodId = today.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/fiscal-periods/{periodId}/close", new { reason = "mfg lock probe" }, Admin)).StatusCode);

        Assert.Equal("PERIOD_LOCKED", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)));
        Assert.Equal("PERIOD_LOCKED", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg)));
        Assert.Equal("PERIOD_LOCKED", ErrCode(await PostAsync($"/api/v1/production-orders/{orderId}/scrap-reports", new
        {
            itemSku = comp, qty = 1m, reasonCode = "DAMAGED", disposition = "Disposal",
        }, Mfg)));

        // nothing moved while locked
        Assert.Equal(50m, await StockOf(comp));

        // reopen restores the calendar and posting flows again
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/fiscal-periods/{periodId}/reopen", new { reason = "probe done" }, Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(49m, await StockOf(comp));
    }

    // ---- 15. negative: an issue beyond on-hand stock is refused with nothing written ----

    [Fact]
    public async Task Issue_With_Insufficient_Stock_Returns_409()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 3m, 1m);
        var orderId = await SeedReleasedOrder(output, 10m);

        var res = await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = comp, qty = 5m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("INSUFFICIENT_STOCK", ErrCode(res));

        Assert.Equal(3m, await StockOf(comp)); // nothing moved
        var check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.Equal(0m, check.GetProperty("wipDebits").GetDecimal()); // no journal written
    }

    // ---- 16. negative (R-MFG6): a CREDIT WIP remainder blocks the Completed transition ----

    [Fact]
    public async Task Credit_WIP_Remainder_Blocks_The_Completed_Transition()
    {
        var output = Tag("FG");
        var a = Tag("RM");
        var b = Tag("RM");
        // Σ(QtyPer × UnitCost) = 0.228 → rounds to 0.23, but each issue line rounds DOWN to 0.11
        // → 0.01 CREDIT remainder (credits exceed debits)
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(a, 1m, 0.114m, "Manual"), new BomLineSeed(b, 1m, 0.114m, "Manual")]);
        await SeedStock(a, 10m, 1m);
        await SeedStock(b, 10m, 1m);
        var orderId = await SeedReleasedOrder(output, 1m);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = a, qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/material-issues", new { componentSku = b, qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 1m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "InProcess" }, Mfg)).StatusCode);

        var check = (await GetAsync($"/api/v1/production-orders/{orderId}/close-check", Admin)).GetProperty("data");
        Assert.Equal(-0.01m, check.GetProperty("wipRemainder").GetDecimal());

        // fix round 1 (F2.1): the credit remainder is caught at the Completed gate — the order can
        // no longer BECOME unclosable; R-MFG6's close-level MFG_CLOSE_WIP_ERROR stays as defense
        // in depth behind the gate
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Completed" }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("MFG_CLOSE_BLOCKED", ErrCode(res));
        Assert.Contains(Error(res).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString()!), c => c == "WIP_REMAINDER");
        Assert.Equal("InProcess", (await GetOrder(orderId)).GetProperty("status").GetString()); // gate held

        // and close refuses outright — the order never reached Completed
        res = await PostAsync($"/api/v1/production-orders/{orderId}/closures", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("MFG_STATE_ERROR", ErrCode(res));
    }

    // ---- 17. negative (015 edge): a retired revision blocks NEW planning, released orders keep theirs ----

    [Fact]
    public async Task Retired_Bom_Blocks_New_Orders()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        var rev1 = await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        await SeedStock(comp, 50m, 1m);

        // one order is released BEFORE retirement — its frozen revision survives
        var releasedOrder = await SeedReleasedOrder(output, 2m);
        Assert.Equal(rev1, (await GetOrder(releasedOrder)).GetProperty("releasedBomRevisionId").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{rev1}/retirements", new { reason = "component discontinued" }, Mfg)).StatusCode);

        // NEW planning on the retired revision is refused
        var res = await PostAsync("/api/v1/production-orders", new { outputSku = output, demandedQty = 1m, unit = "pcs", overTolerancePct = 0m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BOM_NOT_EFFECTIVE", ErrCode(res));

        // the released order keeps working off its frozen revision
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{releasedOrder}/material-issues", new { componentSku = comp, qty = 2m }, Mfg)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/production-orders/{releasedOrder}/completions", new { qty = 2m }, Mfg)).StatusCode);
    }

    // ---- 18. concurrency: a stale double transition updates zero rows and writes no second event ----

    [Fact]
    public async Task Stale_Double_Transition_Is_Rejected_By_The_Status_Guard()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1, 1m, [new BomLineSeed(comp, 1m, 2.00m, "Manual")]);
        var orderId = await SeedOrder(output, 2m);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/transitions", new { status = "Planned" }, Mfg)).StatusCode);

        // scope A loads the order believing it is still Planned
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        await using var dbA = new ErpDbContext(options);
        var ecA = new EntityContext();
        var serviceA = new ProductionOrderService(
            new ManufacturingRepository(dbA, ecA), new InventoryRepository(dbA, ecA),
            new JournalRepository(dbA, ecA), new NumberSeries(dbA),
            new AuditRepository(dbA, new NumberSeries(dbA), ecA), ecA, dbA);
        var stale = new ManufacturingRepository(dbA, ecA).FindOrder(orderId);
        Assert.Equal(ProductionOrderStatus.Planned, stale!.Status);

        // scope B releases via HTTP and commits
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/production-orders/{orderId}/releases", new { }, Admin)).StatusCode);
        var outboxBefore = (await GetAsync("/api/v1/outbox", Admin)).GetProperty("data")
            .EnumerateArray().Count(e => e.GetProperty("type").GetString() == "mfg.order.released");

        // scope A releases from its stale snapshot — the concurrency-token UPDATE matched zero
        // rows, nothing is written, and the caller gets 409
        var releaser = new Erp.Api.CurrentUser("U-001", "admin@teamzlab.dev", "Tenant Admin", "R-ADMIN", new[] { "*" });
        var (data, code, _) = serviceA.Release(releaser, orderId, null);
        Assert.Equal("MFG_CONCURRENCY_ERROR", code);
        Assert.Null(data);
        var outboxAfter = (await GetAsync("/api/v1/outbox", Admin)).GetProperty("data")
            .EnumerateArray().Count(e => e.GetProperty("type").GetString() == "mfg.order.released");
        Assert.Equal(outboxBefore, outboxAfter); // no second release event
        Assert.Equal("Released", (await GetOrder(orderId)).GetProperty("status").GetString());
    }
}
