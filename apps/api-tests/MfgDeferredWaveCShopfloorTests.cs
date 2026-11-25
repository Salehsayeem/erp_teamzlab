using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

// MFG deferred screens — Wave C (MF5 shop-floor extensions 044..049, MF6 subcontract
// 050..052, MF7 maintenance interface 053/054). TDD sketch items 39-44 + the labor
// maker-checker/overlap money-adjacent paths.
namespace Erp.Api.Tests;

public class MfgDeferredWaveCShopfloorTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public MfgDeferredWaveCShopfloorTests(TestApiFactory factory) => _factory = factory;

    private const string Mfg = "U-008";
    private const string Planner = "U-011";
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
        var receipt = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, proc);
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        // the receipt create returns { receipt, journal } — the receipt id is the movement reference
        return (await Body(receipt)).GetProperty("receipt").GetProperty("id").GetString()!;
    }

    private async Task SeedApprovedBom(string outputSku, decimal outputQtyPer, (string sku, decimal qtyPer, decimal cost)[] lines)
    {
        var res = await PostAsync("/api/v1/boms", new { outputSku, revision = 1, outputQtyPer, effectiveFrom = new DateOnly(2026, 1, 1) }, Mfg);
        var bomId = (await Body(res)).GetProperty("id").GetString()!;
        foreach (var l in lines)
            await PostAsync($"/api/v1/boms/{bomId}/lines", new { componentSku = l.sku, qtyPer = l.qtyPer, unit = "pcs", issuePolicy = "Manual", unitCost = l.cost }, Mfg);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/boms/{bomId}/approvals", new { }, Admin)).StatusCode);
    }

    private async Task<string> SeedApprovedRouting(string outputSku, string? supplierId = null, string? serviceDescription = null)
    {
        var wcRes = await PostAsync("/api/v1/work-centers", new { code = Tag("CWC"), name = "wc", capabilities = "mold" }, Mfg);
        var wcId = (await Body(wcRes)).GetProperty("id").GetString()!;
        var res = await PostAsync("/api/v1/routings", new { outputSku, revision = 1, effectiveFrom = new DateOnly(2026, 1, 1) }, Mfg);
        var routingId = (await Body(res)).GetProperty("id").GetString()!;
        if (supplierId is null)
        {
            res = await PostAsync($"/api/v1/routings/{routingId}/operations", new
            {
                operationCode = "OP10", workCenterId = wcId, requiredCapabilities = "mold",
                timeUnit = "min", setupMin = 30m, perUnitMin = 10m, isZeroDuration = false, seq = 1,
            }, Mfg);
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        }
        else
        {
            res = await PostAsync($"/api/v1/routings/{routingId}/subcontract-operations", new
            {
                operationCode = "SB10", workCenterId = wcId, supplierId, serviceDescription, setupMin = 0m, perUnitMin = 0m, seq = 1,
            }, Mfg);
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/routings/{routingId}/approvals", new { }, Planner)).StatusCode);
        return routingId;
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

    private async Task<string> SeedTwoOpRouting(string outputSku)
    {
        var wcRes = await PostAsync("/api/v1/work-centers", new { code = Tag("TWC"), name = "wc", capabilities = "mold" }, Mfg);
        var wcId = (await Body(wcRes)).GetProperty("id").GetString()!;
        var res = await PostAsync("/api/v1/routings", new { outputSku, revision = 1, effectiveFrom = new DateOnly(2026, 1, 1) }, Mfg);
        var routingId = (await Body(res)).GetProperty("id").GetString()!;
        foreach (var seq in new[] { 1, 2 })
        {
            var op = await PostAsync($"/api/v1/routings/{routingId}/operations", new
            {
                operationCode = $"OP{seq:00}", workCenterId = wcId, requiredCapabilities = "mold",
                timeUnit = "min", setupMin = 30m, perUnitMin = 10m, isZeroDuration = false, seq,
            }, Mfg);
            Assert.True(op.IsSuccessStatusCode, await op.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/routings/{routingId}/approvals", new { }, Planner)).StatusCode);
        return routingId;
    }

    private async Task<(string orderId, string workerId)> SeedOrderWithWorker()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        await SeedStock(comp, 50m, 1m);
        await SeedApprovedRouting(output);
        var orderId = await SeedReleasedOrder(output, 10m);
        // a worker for time reporting — the PEOPLE register seeds employment rows; use a
        // direct insert so this slice stays independent of the PEOPLE partition
        var workerId = "WKR-" + Guid.NewGuid().ToString("N")[..8];
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO employments (id, tenant_id, legal_entity_id, worker_no, full_name, preferred_name, national_id, email, phone, category_code, manager_user_id, location_id, weekly_planned_hours, hire_date, termination_date, termination_reason, status, created_by, created_at) " +
            "VALUES ({0}, 'T-001', 'LE-001', {0}, 'Shop Worker', NULL, NULL, NULL, NULL, 'OPS', NULL, NULL, 40, '2026-01-01', NULL, NULL, 0, 'U-008', now())", workerId);
        return (orderId, workerId);
    }

    // ---- 39: duplicate counter source ids ignored idempotently; rollover never negative ----

    [Fact]
    public async Task Counter_Duplicates_Are_Idempotent_And_Rollover_Never_Goes_Negative()
    {
        var (orderId, _) = await SeedOrderWithWorker();
        var sourceId = $"cnt-{Guid.NewGuid():N}";

        var first = await PostAsync($"/api/v1/production-orders/{orderId}/counter-events", new { sourceEventId = sourceId, counterValue = 100m }, Mfg);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var data = await Body(first);
        Assert.Equal(100m, data.GetProperty("deltaQty").GetDecimal());
        Assert.False(data.GetProperty("rollover").GetBoolean());

        // duplicate source id → same row, no second event
        var dup = await PostAsync($"/api/v1/production-orders/{orderId}/counter-events", new { sourceEventId = sourceId, counterValue = 100m }, Mfg);
        Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
        Assert.Equal(data.GetProperty("id").GetString(), (await Body(dup)).GetProperty("id").GetString());

        // rollover: the counter wraps to a lower reading → delta = the new reading, flagged
        var rollover = await PostAsync($"/api/v1/production-orders/{orderId}/counter-events", new { sourceEventId = $"cnt-{Guid.NewGuid():N}", counterValue = 7m }, Mfg);
        Assert.Equal(HttpStatusCode.Created, rollover.StatusCode);
        var roll = await Body(rollover);
        Assert.True(roll.GetProperty("rollover").GetBoolean());
        Assert.Equal(7m, roll.GetProperty("deltaQty").GetDecimal()); // never negative
    }

    // ---- 44 (labor): supervisor approval ≠ reporter; overlapping intervals rejected; ----
    // ---- approved entries immutable except via linked correction --------------------------------

    [Fact]
    public async Task Labor_Time_Overlap_Rejected_Approval_Maker_Checker_Approved_Immutable()
    {
        var (orderId, workerId) = await SeedOrderWithWorker();
        var start = new DateTimeOffset(2026, 5, 4, 8, 0, 0, TimeSpan.Zero);

        var res = await PostAsync($"/api/v1/production-orders/{orderId}/labor-time", new
        {
            workerEmploymentId = workerId, startAt = start, endAt = start.AddMinutes(60),
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var entry = await Body(res);

        // overlapping interval for the same worker → rejected (044 AC3)
        var overlap = await PostAsync($"/api/v1/production-orders/{orderId}/labor-time", new
        {
            workerEmploymentId = workerId, startAt = start.AddMinutes(30), endAt = start.AddMinutes(90),
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal("LABOR_TIME_OVERLAP", ErrCode(overlap));

        // maker-checker: the reporter cannot approve their own entry
        var own = await PostAsync($"/api/v1/production-orders/{orderId}/labor-time/{entry.GetProperty("id").GetString()}/approvals", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("LABOR_APPROVAL_ERROR", ErrCode(own));

        // a different officer approves; the flip is final — a second approval writes nothing
        var approval = await PostAsync($"/api/v1/production-orders/{orderId}/labor-time/{entry.GetProperty("id").GetString()}/approvals", new { }, Planner);
        Assert.True(approval.IsSuccessStatusCode, await approval.Content.ReadAsStringAsync());
        var again = await PostAsync($"/api/v1/production-orders/{orderId}/labor-time/{entry.GetProperty("id").GetString()}/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("LABOR_ALREADY_DECIDED", ErrCode(again));

        // approved entries are immutable: a second approval attempt cannot re-open, and the
        // list shows Approved with the approver recorded
        var entries = (await GetAsync($"/api/v1/production-orders/{orderId}/labor-time", Mfg)).GetProperty("data").EnumerateArray().ToList();
        var row = entries.First(e => e.GetProperty("id").GetString() == entry.GetProperty("id").GetString());
        Assert.Equal("Approved", row.GetProperty("status").GetString());
        Assert.Equal(Planner, row.GetProperty("approvedBy").GetString());
    }

    // ---- 40: WIP transfers bounded by open qty and routing precedence ----

    [Fact]
    public async Task Wip_Transfers_Respect_Open_Qty_And_Routing_Precedence()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        await SeedStock(comp, 50m, 1m);
        await SeedTwoOpRouting(output); // seq 1 → seq 2 — a real precedence chain
        var orderId = await SeedReleasedOrder(output, 10m);

        // over-transfer beyond the open qty (planned 10) → rejected (048 AC1)
        var over = await PostAsync($"/api/v1/production-orders/{orderId}/wip-transfers", new
        {
            fromSeq = 1, toSeq = 2, qty = 11m,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("WIP_TRANSFER_OVER", ErrCode(over));

        // skipping ahead (1 → 2 is legal; 2 → 2 is not a step; 1 → 99 unknown) — precedence holds
        var invalid = await PostAsync($"/api/v1/production-orders/{orderId}/wip-transfers", new
        {
            fromSeq = 2, toSeq = 2, qty = 5m,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Equal("WIP_PRECEDENCE_ERROR", ErrCode(invalid));

        // a legitimate transfer within bounds succeeds and consumes the open qty
        var ok = await PostAsync($"/api/v1/production-orders/{orderId}/wip-transfers", new
        {
            fromSeq = 1, toSeq = 2, qty = 4m,
        }, Mfg);
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());

        var remaining = await PostAsync($"/api/v1/production-orders/{orderId}/wip-transfers", new
        {
            fromSeq = 1, toSeq = 2, qty = 7m,
        }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, remaining.StatusCode); // 10 − 4 = 6 open
        Assert.Equal("WIP_TRANSFER_OVER", ErrCode(remaining));
    }

    // ---- 41/42: linked corrections preserve originals; post-close defers to period policy ----

    [Fact]
    public async Task Corrections_Link_Compensating_Events_And_Defer_To_Period_Policy()
    {
        var (orderId, workerId) = await SeedOrderWithWorker();
        var start = new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero);
        var res = await PostAsync($"/api/v1/production-orders/{orderId}/labor-time", new
        {
            workerEmploymentId = workerId, startAt = start, endAt = start.AddMinutes(60),
        }, Planner); // reported by the planner so Mfg can approve — then Mfg proposes, Planner checks
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var entry = await Body(res);
        var entryId = entry.GetProperty("id").GetString()!;

        // the correction proposes a reversing entry — proposer ≠ approver
        var corr = await PostAsync("/api/v1/production-corrections", new
        {
            targetType = "labor_time", targetId = entryId, reason = "double-report",
            compensatingJson = "{\"minutesDelta\": -60}",
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, corr.StatusCode);
        var correction = await Body(corr);

        var own = await PostAsync($"/api/v1/production-corrections/{correction.GetProperty("id").GetString()}/approvals", new { }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("CORRECTION_APPROVAL_ERROR", ErrCode(own));

        // approval applies the compensating event; the ORIGINAL entry stays intact
        var approval = await PostAsync($"/api/v1/production-corrections/{correction.GetProperty("id").GetString()}/approvals", new { }, Planner);
        Assert.True(approval.IsSuccessStatusCode, await approval.Content.ReadAsStringAsync());
        var entries = (await GetAsync($"/api/v1/production-orders/{orderId}/labor-time", Mfg)).GetProperty("data").EnumerateArray().ToList();
        var original = entries.First(e => e.GetProperty("id").GetString() == entryId);
        Assert.Equal(60m, original.GetProperty("minutes").GetDecimal()); // preserved, never overwritten
        var compensating = entries.Where(e => e.GetProperty("correctionOfId").GetString() == entryId).ToList();
        Assert.Single(compensating);
        Assert.Equal(-60m, compensating[0].GetProperty("minutes").GetDecimal());

        // post-close correction defers to finance period policy: a NEW correction against an
        // entry whose work date sits in a CLOSED period is refused (049 edge)
        // a single-day CLOSED period uniquely covering the entry's work date — the seeded rolling
        // months are Open, and EnsurePeriodOpen picks the LATEST-starting covering period
        var closedPeriodId = "FP-closed-" + Guid.NewGuid().ToString("N")[..6];
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO fiscal_periods (id, tenant_id, legal_entity_id, name, start_date, end_date, status) " +
            "VALUES ({0}, 'T-001', 'LE-001', {0}, '2026-05-06', '2026-05-06', 2)", closedPeriodId); // 2 = Closed
        var later = await PostAsync($"/api/v1/production-orders/{orderId}/labor-time", new
        {
            workerEmploymentId = workerId, startAt = start.AddDays(1), endAt = start.AddDays(1).AddMinutes(30),
        }, Planner);
        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
        var entry2 = await Body(later);
        var corr2 = await PostAsync("/api/v1/production-corrections", new
        {
            targetType = "labor_time", targetId = entry2.GetProperty("id").GetString()!, reason = "typo",
            compensatingJson = "{\"minutesDelta\": -30}",
        }, Mfg);
        var corr2Id = (await Body(corr2)).GetProperty("id").GetString()!;
        var approval2 = await PostAsync($"/api/v1/production-corrections/{corr2Id}/approvals", new { }, Planner);
        Assert.Equal(HttpStatusCode.Conflict, approval2.StatusCode);
        Assert.Equal("PERIOD_LOCKED", ErrCode(approval2));
    }

    // ---- 43: subcontract completion cannot exceed the accepted returned quantity ----

    [Fact]
    public async Task Subcontract_Completion_Is_Gated_On_Accepted_Returned_Quantity()
    {
        var output = Tag("FG");
        var comp = Tag("RM");
        await SeedApprovedBom(output, 1m, [(comp, 1m, 1.00m)]);
        var proc = "U-002";
        var supRes = await PostAsync("/api/v1/suppliers", new { name = "SubContractor", taxId = "TRX-SUB", email = "sub@x.y" }, proc);
        var supplierId = (await Body(supRes)).GetProperty("id").GetString()!;
        await SeedApprovedRouting(output, supplierId, "anodizing service");
        var orderId = await SeedReleasedOrder(output, 10m);

        // send 10 out (custody must reference an accepted inventory movement — the PO receipt)
        var movementId = await SeedStock(output, 20m, 1m);
        var send = await PostAsync("/api/v1/subcontract-custodies", new
        {
            orderId, routingOperationSeq = 1, kind = "Send", qty = 10m, inventoryMovementId = movementId,
        }, Mfg);
        Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());

        // unknown movement id rejected (051 AC1)
        var badCustody = await PostAsync("/api/v1/subcontract-custodies", new
        {
            orderId, routingOperationSeq = 1, kind = "Return", qty = 5m, inventoryMovementId = "NOPE-1",
        }, Mfg);
        Assert.Equal(HttpStatusCode.NotFound, badCustody.StatusCode);
        Assert.Equal("MOVEMENT_NOT_FOUND", ErrCode(badCustody));

        // completing 5 units with NOTHING accepted back → rejected (052 AC3)
        var early = await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 5m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("SUBCONTRACT_ACCEPTANCE_SHORT", ErrCode(early));

        // the return receipt (no quality plan on the output → accepted directly)
        var receipt = await PostAsync($"/api/v1/subcontract-orders/{orderId}/receipts", new { routingOperationSeq = 1, qty = 6m }, Mfg);
        Assert.True(receipt.IsSuccessStatusCode, await receipt.Content.ReadAsStringAsync());

        // completion up to the accepted quantity now passes; beyond it fails again
        var ok = await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 6m }, Mfg);
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        var over = await PostAsync($"/api/v1/production-orders/{orderId}/completions", new { qty = 2m }, Mfg);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("SUBCONTRACT_ACCEPTANCE_SHORT", ErrCode(over));
    }

    // ---- 44: maintenance ingestion idempotent; out-of-order never regresses availability ----

    [Fact]
    public async Task Maintenance_Ingestion_Is_Idempotent_And_Out_Of_Order_Never_Regresses()
    {
        var wcRes = await PostAsync("/api/v1/work-centers", new { code = Tag("MW"), name = "w", capabilities = "mold" }, Mfg);
        var wcId = (await Body(wcRes)).GetProperty("id").GetString()!;
        // resolve the work center code from the list (the ingestion contract keys on ResourceCode)
        var list = (await GetAsync("/api/v1/work-centers", Mfg)).GetProperty("data").EnumerateArray()
            .First(w => w.GetProperty("id").GetString() == wcId);
        var resourceCode = list.GetProperty("code").GetString()!;

        var start = new DateTimeOffset(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);
        var evt = new { sourceEventId = $"maint-{Guid.NewGuid():N}", revision = 1, resourceCode, assetId = (string?)null, windowStart = start, windowEnd = start.AddHours(4), reason = "preventive" };

        var first = await PostAsync("/api/v1/maintenance-availability-events", evt, Mfg);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var accepted = await Body(first);
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());

        // duplicate source event id → the stored window returns (053 AC1)
        var dup = await PostAsync("/api/v1/maintenance-availability-events", evt, Mfg);
        Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
        Assert.Equal(accepted.GetProperty("id").GetString(), (await Body(dup)).GetProperty("id").GetString());

        // out-of-order revision (older) → ignored, availability never regresses
        var stale = await PostAsync("/api/v1/maintenance-availability-events", evt with { revision = 0, windowStart = start.AddHours(-24) }, Mfg);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        var staleData = await Body(stale);
        Assert.Equal(accepted.GetProperty("id").GetString(), staleData.GetProperty("id").GetString());
        Assert.Equal(1, staleData.GetProperty("revision").GetInt32());

        // higher revision → window REPLACED (a planned longer outage)
        var longer = await PostAsync("/api/v1/maintenance-availability-events", evt with { revision = 2, windowEnd = start.AddHours(8) }, Mfg);
        Assert.Equal(HttpStatusCode.OK, longer.StatusCode);
        Assert.Equal(accepted.GetProperty("id").GetString(), (await Body(longer)).GetProperty("id").GetString());

        // the outage materialized as an unavailable calendar window on the work center
        var calendar = (await GetAsync($"/api/v1/work-centers/{wcId}/calendar", Mfg)).GetProperty("data").EnumerateArray()
            .Where(c => !c.GetProperty("isAvailable").GetBoolean()).ToList();
        Assert.Single(calendar);
        Assert.Equal("maintenance", calendar[0].GetProperty("source").GetString());

        // unmapped resource code → integration exception, never a crash (053 AC1)
        var unmapped = await PostAsync("/api/v1/maintenance-availability-events", evt with
        {
            sourceEventId = $"maint-{Guid.NewGuid():N}", resourceCode = "NO-SUCH-WC",
        }, Mfg);
        Assert.Equal(HttpStatusCode.Created, unmapped.StatusCode);
        var exception = await Body(unmapped);
        Assert.Equal("Exception", exception.GetProperty("status").GetString());
        Assert.Equal("UNMAPPED_RESOURCE", exception.GetProperty("exceptionReason").GetString());
    }
}
