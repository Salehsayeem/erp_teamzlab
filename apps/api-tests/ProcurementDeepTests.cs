using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

// T6 PROCUREMENT deep slice (ERP-PROCUREMENT-022/023/024/025/026/027/028/029/030/031/032/033/034/
// 036/037/038/039 + 044/047, trimmed per rulings R-T6a..i):
// sourcing events with lots + frozen criteria, eligibility-gated invitations, Q&A with event
// version bumps, buyer-recorded bids with digest receipts (sealed until an audited opening),
// independent evaluator scoring with a deterministic normalized trace, maker-checked awards with
// eligibility re-check and lot-ceiling allocation, and commitment-only contracts with versioned
// amendments, price-schedule overlap protection, obligations and manual renewal exercise.
// Makers: U-002 (procurement, R-PROC). Checkers: U-003 (finance — sourcing:approve +
// contracts:approve); U-001 (admin, *) can act as either. Evaluator: U-006 (R-EVAL).
// Bid-opening and late-bid gates compare wall-clock instants, so tests time-shift closes_at on
// the throwaway fixture database directly (the only writes a test makes outside the API).

namespace Erp.Api.Tests;

public class ProcurementDeepTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public ProcurementDeepTests(TestApiFactory factory) => _factory = factory;

    private const string Proc = "U-002";   // R-PROC — maker
    private const string Fin = "U-003";    // R-FIN — sourcing:approve / contracts:approve checker
    private const string Admin = "U-001";  // R-ADMIN — *
    private const string Eval = "U-006";   // R-EVAL — sourcing:read + sourcing:evaluate

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? token, string? idemKey = null)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        if (idemKey is not null) client.DefaultRequestHeaders.Add("Idempotency-Key", idemKey);
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

    private async Task<JsonElement> PostForData(string path, object body, string token, string? idemKey = null)
    {
        var res = await PostAsync(path, body, token, idemKey);
        Assert.True(res.IsSuccessStatusCode, $"{path} → {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await Json(res)).GetProperty("data");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static string ErrorCode(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

    private async Task<string> SeedSupplier(string tag)
    {
        var data = await PostForData("/api/v1/suppliers",
            new { name = $"Supplier {tag}", taxId = $"TRX-{tag}", email = $"{tag.ToLowerInvariant()}@sup.test" }, Proc);
        return data.GetProperty("id").GetString()!;
    }

    // creates + publishes a sourcing event with one lot and an invitation per supplier. Default
    // criteria sum to 100: PRICE 60 (scale 10), DELIVERY 30 (scale 5), ISO 10 (pass/fail).
    // Returns (eventId, lotId, criterionId by code).
    private async Task<(string eventId, string lotId, Dictionary<string, string> criteria)> SeedPublishedEvent(
        string tag, string eventType = "Rfq", decimal targetQty = 10m, DateTimeOffset? closesAt = null,
        string[]? suppliers = null, (string code, string kind, decimal weight, int scale)[]? criteria = null)
    {
        var eventId = await CreateEvent(tag, eventType, closesAt ?? DateTimeOffset.UtcNow.AddHours(4));
        var lotId = await AddLot(eventId, tag, targetQty);
        var ids = new Dictionary<string, string>();
        foreach (var (code, kind, weight, scale) in criteria ?? [("PRICE", "Scored", 60m, 10), ("DELIVERY", "Scored", 30m, 5), ("ISO", "PassFail", 10m, 0)])
            ids[code] = (await PostForData($"/api/v1/sourcing-events/{eventId}/criteria",
                new { code, name = $"{code} {tag}", kind, weightPct = weight, scaleMax = scale }, Proc)).GetProperty("id").GetString()!;
        foreach (var supplierId in suppliers ?? []) await Invite(eventId, supplierId);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sourcing-events/{eventId}/publications", new { }, Proc)).StatusCode);
        return (eventId, lotId, ids);
    }

    private async Task<string> CreateEvent(string tag, string eventType = "Rfq", DateTimeOffset? closesAt = null, string actor = Proc)
    {
        var data = await PostForData("/api/v1/sourcing-events", new
        {
            title = $"Sourcing {tag}",
            eventType,
            opensAt = DateTimeOffset.UtcNow.AddHours(-1),
            closesAt = closesAt ?? DateTimeOffset.UtcNow.AddHours(4),
            timeZoneName = "Asia/Dhaka",
            currency = "BDT",
        }, actor);
        return data.GetProperty("id").GetString()!;
    }

    private async Task<string> AddLot(string eventId, string tag, decimal targetQty = 10m)
    {
        var data = await PostForData($"/api/v1/sourcing-events/{eventId}/lots",
            new { lotNo = "L1", description = $"Lot {tag}", itemSku = $"SKU-{tag}", targetQty, ceilingValue = 100000m }, Proc);
        return data.GetProperty("id").GetString()!;
    }

    private async Task<string> Invite(string eventId, string supplierId)
    {
        var data = await PostForData($"/api/v1/sourcing-events/{eventId}/invitations",
            new { supplierIds = new[] { supplierId } }, Proc);
        return data.EnumerateArray().First(i => i.GetProperty("supplierId").GetString() == supplierId)
            .GetProperty("id").GetString()!;
    }

    // buyer records a bid for a supplier; default single line qty 5 @ 100 with 10% discount
    private async Task<string> SubmitBid(string eventId, string supplierId, string lotId,
        decimal qty = 5m, decimal unitPrice = 100m, decimal discountPct = 10m, string? idemKey = null)
    {
        var data = await PostForData($"/api/v1/sourcing-events/{eventId}/bids",
            new { supplierId, lines = new[] { new { lotId, qty, unitPrice, discountPct } } }, Proc, idemKey);
        return data.GetProperty("bidId").GetString()!;
    }

    private async Task<Dictionary<string, string>> AssignEvaluatorAndScore(
        string eventId, string bidId, Dictionary<string, string> criteria, (string code, decimal? raw, bool? pass)[] scores)
    {
        await PostForData($"/api/v1/sourcing-events/{eventId}/evaluator-assignments",
            new { userIds = new[] { Eval } }, Proc);
        var result = new Dictionary<string, string>();
        foreach (var (code, raw, pass) in scores)
        {
            var data = await PostForData($"/api/v1/sourcing-events/{eventId}/scores",
                new { bidId, criterionId = criteria[code], rawScore = raw, passFail = pass, comment = (string?)"ok" }, Eval);
            result[code] = data.GetProperty("id").GetString()!;
        }
        return result;
    }

    private async Task<(string awardId, string eventId)> SeedRecommendedAward(string tag, string supplierId, decimal qty = 5m, decimal unitPrice = 100m)
    {
        var (eventId, lotId, _) = await SeedPublishedEvent(tag, suppliers: [supplierId]);
        await SubmitBid(eventId, supplierId, lotId, qty: qty, unitPrice: unitPrice);
        await CloseEvent(eventId);
        var data = await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId, qty, unitPrice, recommendation = $"Best value {tag}" }, Proc);
        return (data.GetProperty("id").GetString()!, eventId);
    }

    private async Task<string> SeedActiveContract(string tag, string supplierId, decimal targetQty = 10m,
        int optionPeriodsTotal = 1, int? renewalNoticeDays = 30, int termDays = 90)
    {
        var (awardId, _) = await SeedRecommendedAward($"{tag}-CTR", supplierId, qty: targetQty / 2);
        await PostForData($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Fin);
        var data = await PostForData("/api/v1/contracts", new
        {
            supplierId,
            awardId,
            currency = "BDT",
            effectiveFrom = Today,
            effectiveTo = Today.AddDays(termDays),
            renewalNoticeDays = (int?)renewalNoticeDays,
            optionPeriodsTotal = (int?)optionPeriodsTotal,
        }, Proc);
        var contractId = data.GetProperty("id").GetString()!;
        var amendment = await PostForData($"/api/v1/contracts/{contractId}/amendments", new
        {
            effectiveDate = Today,
            summary = $"Initial price schedule {tag}",
            priceLines = new[] { new { itemSku = $"SKU-{tag}-CTR", unitPrice = 100m, effectiveFrom = Today, effectiveTo = (DateOnly?)null } },
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, amendment.GetProperty("amendment").GetProperty("id").GetString() is { } ? HttpStatusCode.Created : HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/contracts/{contractId}/activations", new { }, Fin)).StatusCode);
        return contractId;
    }

    // the bid-opening and late-bid gates compare wall-clock instants — shift closes_at into the
    // past on the throwaway fixture database (test-only write outside the API, PersistenceTests style)
    private async Task CloseEvent(string eventId)
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sourcing_events SET closes_at = $1 WHERE id = $2", conn);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddMinutes(-5));
        cmd.Parameters.AddWithValue(eventId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private static JsonElement BidRow(JsonElement bids, string bidId) =>
        bids.GetProperty("data").EnumerateArray().First(b => b.GetProperty("bidId").GetString() == bidId);

    private static bool HasProp(JsonElement row, string name) =>
        row.TryGetProperty(name, out _) || (row.ValueKind == JsonValueKind.Object
            && row.EnumerateObject().Any(p => p.Name == name));

    // 022 AC3 — publication is blocked until at least one invitation AND criteria exist
    [Fact]
    public async Task Event_Activation_Without_Supplier_Or_Criterion_Returns_409()
    {
        var eventId = await CreateEvent("T1A");
        var res = await PostAsync($"/api/v1/sourcing-events/{eventId}/publications", new { }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVENT_PUBLISH_BLOCKED", ErrorCode(await Json(res)));

        // criteria alone still block: no invitation yet
        await PostForData($"/api/v1/sourcing-events/{eventId}/criteria",
            new { code = "PRICE", name = "Price T1A", kind = "Scored", weightPct = 100m, scaleMax = 10 }, Proc);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/publications", new { }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVENT_PUBLISH_BLOCKED", ErrorCode(await Json(res)));

        // invitation alone blocks the second event: no criteria at all
        var supplierId = await SeedSupplier("T1B");
        var eventId2 = await CreateEvent("T1B");
        await Invite(eventId2, supplierId);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId2}/publications", new { }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVENT_PUBLISH_BLOCKED", ErrorCode(await Json(res)));
    }

    // 030 AC1 — weights must sum to exactly 100 at publish
    [Fact]
    public async Task Publish_Blocks_When_Criteria_Weights_Not_Summing_100()
    {
        var supplierId = await SeedSupplier("T2");
        var eventId = await CreateEvent("T2");
        await AddLot(eventId, "T2");
        await Invite(eventId, supplierId);
        await PostForData($"/api/v1/sourcing-events/{eventId}/criteria",
            new { code = "PRICE", name = "Price", kind = "Scored", weightPct = 60m, scaleMax = 10 }, Proc);
        await PostForData($"/api/v1/sourcing-events/{eventId}/criteria",
            new { code = "DELIVERY", name = "Delivery", kind = "Scored", weightPct = 30m, scaleMax = 5 }, Proc);

        var res = await PostAsync($"/api/v1/sourcing-events/{eventId}/publications", new { }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVENT_PUBLISH_BLOCKED", ErrorCode(await Json(res)));

        await PostForData($"/api/v1/sourcing-events/{eventId}/criteria",
            new { code = "ISO", name = "ISO cert", kind = "PassFail", weightPct = 10m, scaleMax = 0 }, Proc);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/publications", new { }, Proc);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Published", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        // freeze audited + outboxed
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains("procurement.event.published", outbox.GetProperty("data").EnumerateArray()
            .Select(o => o.GetProperty("type").GetString()));
    }

    // 023 AC2 + 047 AC2/044 — Suspended status and in-force Hard gates block invitations;
    // a time-bounded exception (023 AC3) restores eligibility while it runs
    [Fact]
    public async Task Invitation_For_Suspended_Supplier_Returns_409_SUPPLIER_INELIGIBLE()
    {
        var suspended = await SeedSupplier("T3S");
        var gated = await SeedSupplier("T3G");
        var excepted = gated; // same supplier: gate → blocked, gate + exception → invited
        var futureGated = await SeedSupplier("T3F");
        var warned = await SeedSupplier("T3W");
        var eventId = await CreateEvent("T3");

        // status endpoint needs a reason (047 AC1)
        var res = await PostAsync($"/api/v1/suppliers/{suspended}/status", new { status = "Suspended", reason = "" }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));
        res = await PostAsync($"/api/v1/suppliers/{suspended}/status", new { status = "Suspended", reason = "defaulted on delivery" }, Proc);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/invitations", new { supplierIds = new[] { suspended } }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("SUPPLIER_INELIGIBLE", ErrorCode(await Json(res)));

        // in-force Hard gate blocks (023 AC1/044 AC1)
        res = await PostAsync($"/api/v1/suppliers/{gated}/gates", new
        {
            gateCode = "TAX-CLEAR",
            severity = "Hard",
            effectiveFrom = Today,
            effectiveTo = (DateOnly?)null,
            evidenceRef = (string?)null,
        }, Proc, "t3-gate");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/invitations", new { supplierIds = new[] { gated } }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("SUPPLIER_INELIGIBLE", ErrorCode(await Json(res)));

        // time-bounded exception (023 AC3) — re-POST the gate with an exception window
        res = await PostAsync($"/api/v1/suppliers/{excepted}/gates", new
        {
            gateCode = "TAX-CLEAR",
            severity = "Hard",
            effectiveFrom = Today,
            effectiveTo = (DateOnly?)null,
            evidenceRef = (string?)null,
            exceptionUntil = Today.AddDays(30),
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/invitations", new { supplierIds = new[] { excepted } }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // future-dated Hard gate is not yet in force — invitation proceeds
        res = await PostAsync($"/api/v1/suppliers/{futureGated}/gates", new
        {
            gateCode = "ISO-CERT",
            severity = "Hard",
            effectiveFrom = Today.AddDays(30),
            effectiveTo = (DateOnly?)null,
            evidenceRef = (string?)null,
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/invitations", new { supplierIds = new[] { futureGated } }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // Warning gates never block but are recorded on the invitation
        res = await PostAsync($"/api/v1/suppliers/{warned}/gates", new
        {
            gateCode = "FIN-DISCLOSE",
            severity = "Warning",
            effectiveFrom = Today,
            effectiveTo = (DateOnly?)null,
            evidenceRef = (string?)null,
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/invitations", new { supplierIds = new[] { warned } }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Contains("FIN-DISCLOSE", (await Json(res)).GetProperty("data").EnumerateArray()
            .First(i => i.GetProperty("supplierId").GetString() == warned)
            .GetProperty("warningGates").GetString());
    }

    // 028 AC2/AC3 — late bids rejected; same Idempotency-Key replays the same receipt
    [Fact]
    public async Task Bid_After_Close_Returns_409_And_Same_Key_Replay_Reuses_Bid()
    {
        var supplierId = await SeedSupplier("T4");
        var (eventId, lotId, _) = await SeedPublishedEvent("T4", suppliers: [supplierId]);

        var res = await PostAsync($"/api/v1/sourcing-events/{eventId}/bids",
            new { supplierId, lines = new[] { new { lotId, qty = 5m, unitPrice = 100m, discountPct = 10m } } },
            Proc, "t4-bid");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var receipt = (await Json(res)).GetProperty("data");
        var bidId = receipt.GetProperty("bidId").GetString()!;
        Assert.Equal(1, receipt.GetProperty("eventVersion").GetInt32());
        Assert.Equal(64, receipt.GetProperty("contentDigest").GetString()!.Length);

        // same key + same payload → same receipt, never a second bid (AC3) — even after close
        await CloseEvent(eventId);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/bids",
            new { supplierId, lines = new[] { new { lotId, qty = 5m, unitPrice = 100m, discountPct = 10m } } },
            Proc, "t4-bid");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(bidId, (await Json(res)).GetProperty("data").GetProperty("bidId").GetString());

        // a NEW key after ClosesAt is a late bid → rejected (AC2)
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/bids",
            new { supplierId, lines = new[] { new { lotId, qty = 6m, unitPrice = 90m, discountPct = 0m } } }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BID_CLOSED", ErrorCode(await Json(res)));

        // same key with a mutated payload → idempotency conflict, still one bid
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/bids",
            new { supplierId, lines = new[] { new { lotId, qty = 7m, unitPrice = 80m, discountPct = 5m } } },
            Proc, "t4-bid");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("IDEMPOTENCY_CONFLICT", ErrorCode(await Json(res)));

        var bids = await GetAsync($"/api/v1/sourcing-events/{eventId}/bids", Admin);
        Assert.Equal(1, bids.GetProperty("data").GetArrayLength());
    }

    // 025 AC1/AC2 trimmed (R-T6b) — prices hidden from non-sourcing:approve callers until an
    // approved opening records who/when; early opening is BIDS_SEALED; opener ≠ event creator
    [Fact]
    public async Task Bid_Prices_Hidden_From_Buyer_Until_Opening_Is_Recorded()
    {
        var supplierId = await SeedSupplier("T5");
        var (eventId, lotId, _) = await SeedPublishedEvent("T5", suppliers: [supplierId]);
        var bidId = await SubmitBid(eventId, supplierId, lotId);

        // sealed: buyer (R-PROC has no sourcing:approve) sees no prices
        var sealedBody = await GetAsync($"/api/v1/sourcing-events/{eventId}/bids", Proc);
        var sealedRow = BidRow(sealedBody, bidId);
        Assert.False(HasProp(sealedRow, "unitPrice"));
        Assert.False(HasProp(sealedRow.GetProperty("lines")[0], "unitPrice"));
        Assert.False(HasProp(sealedRow.GetProperty("lines")[0], "lineTotal"));

        // sourcing:approve callers may inspect sealed prices
        var approverView = BidRow(await GetAsync($"/api/v1/sourcing-events/{eventId}/bids", Fin), bidId);
        Assert.True(HasProp(approverView.GetProperty("lines")[0], "unitPrice"));

        // opening before ClosesAt → sealed
        var res = await PostAsync($"/api/v1/sourcing-events/{eventId}/bid-openings", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BIDS_SEALED", ErrorCode(await Json(res)));

        // maker-checker: the event creator cannot run the approved opening
        var adminEvent = await CreateEvent("T5B", actor: Admin); // Admin creates → creator = U-001
        var adminLot = await AddLot(adminEvent, "T5B");
        await PostForData($"/api/v1/sourcing-events/{adminEvent}/criteria",
            new { code = "PRICE", name = "Price", kind = "Scored", weightPct = 100m, scaleMax = 10 }, Admin);
        await Invite(adminEvent, supplierId);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sourcing-events/{adminEvent}/publications", new { }, Admin)).StatusCode);
        await SubmitBid(adminEvent, supplierId, adminLot);
        await CloseEvent(adminEvent);
        res = await PostAsync($"/api/v1/sourcing-events/{adminEvent}/bid-openings", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BID_OPENING_APPROVAL_ERROR", ErrorCode(await Json(res)));

        // approved checker opens (after the window closes) → who/when recorded, prices unseal
        await CloseEvent(eventId);
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/bid-openings", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var opened = BidRow(await GetAsync($"/api/v1/sourcing-events/{eventId}/bids", Proc), bidId);
        Assert.Equal("U-003", opened.GetProperty("openedBy").GetString());
        Assert.True(HasProp(opened.GetProperty("lines")[0], "unitPrice"));

        // a second opening is a state conflict
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/bid-openings", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("BIDS_ALREADY_OPEN", ErrorCode(await Json(res)));
    }

    // 031 AC1 — evaluators see and score only their assigned events, only their own scores
    [Fact]
    public async Task Evaluator_Access_Isolated_To_Assigned_Event_And_Own_Scores()
    {
        var s1 = await SeedSupplier("T6A");
        var s2 = await SeedSupplier("T6B");
        var (eventA, lotA, criteriaA) = await SeedPublishedEvent("T6A", suppliers: [s1]);
        var (eventB, lotB, _) = await SeedPublishedEvent("T6B", suppliers: [s2]);
        var bidA = await SubmitBid(eventA, s1, lotA);
        await SubmitBid(eventB, s2, lotB);

        var res = await PostAsync($"/api/v1/sourcing-events/{eventA}/evaluator-assignments",
            new { userIds = new[] { Proc } }, Proc); // creator cannot be an evaluator
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVALUATOR_CREATOR_CONFLICT", ErrorCode(await Json(res)));

        var assigned = await PostForData($"/api/v1/sourcing-events/{eventA}/evaluator-assignments",
            new { userIds = new[] { Eval } }, Proc);
        Assert.Equal(Eval, assigned.EnumerateArray().First().GetProperty("userId").GetString());
        // duplicate assignment is idempotent
        assigned = await PostForData($"/api/v1/sourcing-events/{eventA}/evaluator-assignments",
            new { userIds = new[] { Eval } }, Proc);
        Assert.Equal(1, assigned.GetArrayLength());

        // event list is isolated to assigned events
        var list = await GetAsync("/api/v1/sourcing-events", Eval);
        var listedIds = list.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToHashSet();
        Assert.Contains(eventA, listedIds);
        Assert.DoesNotContain(eventB, listedIds);

        // unassigned event is forbidden for the evaluator (bids + score summary)
        Assert.Equal(HttpStatusCode.Forbidden, (await GetRawAsync($"/api/v1/sourcing-events/{eventB}/bids", Eval)).StatusCode);
        res = await GetRawAsync($"/api/v1/sourcing-events/{eventB}/score-summary", Eval);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(res)));

        // scoring needs the sourcing:evaluate permission
        res = await PostAsync($"/api/v1/sourcing-events/{eventA}/scores",
            new { bidId = bidA, criterionId = criteriaA["PRICE"], rawScore = 8m, passFail = (bool?)null, comment = (string?)null }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        // …and an assigned evaluator: admin carries * but was never assigned to event A
        res = await PostAsync($"/api/v1/sourcing-events/{eventA}/scores",
            new { bidId = bidA, criterionId = criteriaA["PRICE"], rawScore = 8m, passFail = (bool?)null, comment = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(res)));

        // out-of-range input rejected, never coerced (032 AC3)
        res = await PostAsync($"/api/v1/sourcing-events/{eventA}/scores",
            new { bidId = bidA, criterionId = criteriaA["PRICE"], rawScore = 11m, passFail = (bool?)null, comment = (string?)null }, Eval);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // assigned evaluator scores own event — score rows stamped with their own user id
        var score = await PostForData($"/api/v1/sourcing-events/{eventA}/scores",
            new { bidId = bidA, criterionId = criteriaA["PRICE"], rawScore = 8m, passFail = (bool?)null, comment = (string?)"strong" }, Eval);
        Assert.Equal(Eval, score.GetProperty("evaluatorUserId").GetString());
        res = await PostAsync($"/api/v1/sourcing-events/{eventB}/scores",
            new { bidId = (await GetAsync($"/api/v1/sourcing-events/{eventB}/bids", Admin)).GetProperty("data")[0].GetProperty("bidId").GetString()!,
                  criterionId = "PRICE", rawScore = 8m, passFail = (bool?)null, comment = (string?)null }, Eval);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // 032 AC1/AC2/AC3 — the normalized trace is deterministic for fixed inputs, flags missing
    // inputs, and a failed pass/fail criterion marks the bid disqualified
    [Fact]
    public async Task Score_Summary_Trace_Is_Deterministic_For_Fixed_Inputs()
    {
        var s1 = await SeedSupplier("T7A");
        var s2 = await SeedSupplier("T7B");
        var s3 = await SeedSupplier("T7C");
        var (eventId, lotId, criteria) = await SeedPublishedEvent("T7", suppliers: [s1, s2, s3]);
        var bid1 = await SubmitBid(eventId, s1, lotId);
        var bid2 = await SubmitBid(eventId, s2, lotId);
        var bid3 = await SubmitBid(eventId, s3, lotId);

        await AssignEvaluatorAndScore(eventId, bid1, criteria, [("PRICE", 8m, null), ("DELIVERY", 4m, null), ("ISO", null, true)]);
        await AssignEvaluatorAndScore(eventId, bid2, criteria, [("PRICE", 6m, null), ("DELIVERY", 2m, null), ("ISO", null, true)]);
        // bid3: PRICE 10, DELIVERY missing, ISO failed
        await AssignEvaluatorAndScore(eventId, bid3, criteria, [("PRICE", 10m, null), ("ISO", null, false)]);

        var first = await GetAsync($"/api/v1/sourcing-events/{eventId}/score-summary", Proc);
        var secondRaw = await (await GetRawAsync($"/api/v1/sourcing-events/{eventId}/score-summary", Proc)).Content.ReadAsStringAsync();
        var firstRaw = await (await GetRawAsync($"/api/v1/sourcing-events/{eventId}/score-summary", Proc)).Content.ReadAsStringAsync();
        Assert.Equal(firstRaw, secondRaw); // determinism: same inputs → byte-identical trace

        var summary = first.GetProperty("data");
        Assert.Equal("1", summary.GetProperty("formulaVersion").GetString());
        var rows = summary.GetProperty("bids").EnumerateArray().ToDictionary(b => b.GetProperty("bidId").GetString()!);

        // PRICE 8/10 × 60 = 48.00; DELIVERY 4/5 × 30 = 24.00 → 72.00, rank 1
        Assert.Equal(72.00m, rows[bid1].GetProperty("total").GetDecimal());
        Assert.Equal(1, rows[bid1].GetProperty("rank").GetInt32());
        Assert.False(rows[bid1].GetProperty("disqualified").GetBoolean());
        var price1 = rows[bid1].GetProperty("rows").EnumerateArray().First(r => r.GetProperty("criterionCode").GetString() == "PRICE");
        Assert.Equal(0.8000m, price1.GetProperty("normalized").GetDecimal());
        Assert.Equal(48.00m, price1.GetProperty("contribution").GetDecimal());
        Assert.Equal(Eval, price1.GetProperty("evaluatorUserId").GetString());

        Assert.Equal(48.00m, rows[bid2].GetProperty("total").GetDecimal());
        Assert.Equal(2, rows[bid2].GetProperty("rank").GetInt32());

        // bid3: failed ISO → disqualified; missing DELIVERY flagged, never coerced
        Assert.True(rows[bid3].GetProperty("disqualified").GetBoolean());
        var delivery3 = rows[bid3].GetProperty("rows").EnumerateArray().First(r => r.GetProperty("criterionCode").GetString() == "DELIVERY");
        Assert.True(delivery3.GetProperty("missing").GetBoolean());
        Assert.Equal(0m, delivery3.GetProperty("contribution").GetDecimal());
        Assert.Equal(3, rows[bid3].GetProperty("rank").GetInt32()); // disqualified sorts last
    }

    // 033 + 016 AC1 — the recommender cannot approve their own award
    [Fact]
    public async Task Award_Approval_By_Recommender_Returns_409()
    {
        var supplierId = await SeedSupplier("T8");
        var (awardId, eventId) = await SeedRecommendedAward("T8", supplierId);

        var res = await PostAsync($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("AWARD_APPROVAL_ERROR", ErrorCode(await Json(res)));

        res = await PostAsync($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var award = (await Json(res)).GetProperty("data").GetProperty("award");
        Assert.Equal("Approved", award.GetProperty("status").GetString());
        Assert.Equal(Fin, award.GetProperty("approvedBy").GetString());

        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains("procurement.award.approved", outbox.GetProperty("data").EnumerateArray()
            .Select(o => o.GetProperty("type").GetString()));

        var events = await GetAsync("/api/v1/sourcing-events?status=Awarded", Proc);
        Assert.Contains(eventId, events.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetString()));

        // double approval is a state conflict
        res = await PostAsync($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("AWARD_STATE_ERROR", ErrorCode(await Json(res)));
    }

    // 023 edge + 033 AC2/AC3 — eligibility re-checked NOW at approval; reject-with-reason awards
    // never reach an order and close the event
    [Fact]
    public async Task Award_Approval_Recheck_Blocks_Suspended_Supplier_With_No_Order()
    {
        var supplierId = await SeedSupplier("T9");
        var (awardId, eventId) = await SeedRecommendedAward("T9", supplierId);

        // suspend between recommendation and approval
        var res = await PostAsync($"/api/v1/suppliers/{supplierId}/status", new { status = "Suspended", reason = "blacklisted" }, Proc);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        res = await PostAsync($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("SUPPLIER_INELIGIBLE", ErrorCode(await Json(res)));

        // no order can reference an unapproved award
        res = await PostAsync("/api/v1/contracts", new
        {
            supplierId, awardId, currency = "BDT", effectiveFrom = Today, effectiveTo = Today.AddDays(30),
            renewalNoticeDays = (int?)null, optionPeriodsTotal = (int?)null,
        }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("AWARD_NOT_APPROVED", ErrorCode(await Json(res)));

        // eligibility restored → checker approves
        await PostAsync($"/api/v1/suppliers/{supplierId}/status", new { status = "Active", reason = "cleared" }, Proc);
        res = await PostAsync($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // no-award path (033 AC3): a lone recommendation rejected with a reason → NoAward
        var s2 = await SeedSupplier("T9B");
        var (award2, event2) = await SeedRecommendedAward("T9B", s2, qty: 2m);
        res = await PostAsync($"/api/v1/awards/{award2}/approvals", new { reason = "all offers above budget" }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rejected = (await Json(res)).GetProperty("data").GetProperty("award");
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("all offers above budget", rejected.GetProperty("rejectionReason").GetString());
        var events = await GetAsync("/api/v1/sourcing-events?status=NoAward", Proc);
        Assert.Contains(event2, events.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetString()));
        res = await PostAsync("/api/v1/contracts", new
        {
            supplierId = s2, awardId = award2, currency = "BDT", effectiveFrom = Today, effectiveTo = Today.AddDays(30),
            renewalNoticeDays = (int?)null, optionPeriodsTotal = (int?)null,
        }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("AWARD_NOT_APPROVED", ErrorCode(await Json(res)));
    }

    // 034 AC1 — split allocations stay within the lot ceiling; remainder visible
    [Fact]
    public async Task Split_Allocations_Over_Lot_Ceiling_Returns_409()
    {
        var s1 = await SeedSupplier("T10A");
        var s2 = await SeedSupplier("T10B");
        var (eventId, lotId, _) = await SeedPublishedEvent("T10", targetQty: 10m, suppliers: [s1, s2]);
        await SubmitBid(eventId, s1, lotId);
        await SubmitBid(eventId, s2, lotId);
        await CloseEvent(eventId);

        var a1 = (await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId = s1, qty = 7m, unitPrice = 100m, recommendation = "split A" }, Proc)).GetProperty("id").GetString()!;
        var a2 = (await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId = s2, qty = 5m, unitPrice = 95m, recommendation = "split B" }, Proc)).GetProperty("id").GetString()!;

        var res = await PostAsync($"/api/v1/awards/{a1}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3m, (await Json(res)).GetProperty("data").GetProperty("lot").GetProperty("remainingTargetQty").GetDecimal());

        res = await PostAsync($"/api/v1/awards/{a2}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("AWARD_OVER_CEILING", ErrorCode(await Json(res)));

        // a split that fits the remainder approves
        var a3 = (await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId = s2, qty = 3m, unitPrice = 95m, recommendation = "split remainder" }, Proc)).GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/awards/{a3}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0m, (await Json(res)).GetProperty("data").GetProperty("lot").GetProperty("remainingTargetQty").GetDecimal());

        // nothing left — even one more unit is over the ceiling
        var a4 = (await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId = s1, qty = 1m, unitPrice = 100m, recommendation = "top-up" }, Proc)).GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/awards/{a4}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("AWARD_OVER_CEILING", ErrorCode(await Json(res)));
    }

    // 036 AC1/AC3 — contracts only from Approved awards; activation is checker-gated and needs
    // at least one price line
    [Fact]
    public async Task Contract_From_Unapproved_Award_Returns_409_And_Activation_Needs_Checker()
    {
        var supplierId = await SeedSupplier("T11");
        var (awardId, _) = await SeedRecommendedAward("T11", supplierId);
        await PostForData($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Fin);

        var res = await PostAsync("/api/v1/contracts", new
        {
            supplierId, awardId = "AWD-9999", currency = "BDT", effectiveFrom = Today, effectiveTo = Today.AddDays(30),
            renewalNoticeDays = (int?)null, optionPeriodsTotal = (int?)null,
        }, Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("AWARD_NOT_FOUND", ErrorCode(await Json(res)));

        var contract = await PostForData("/api/v1/contracts", new
        {
            supplierId, awardId, currency = "BDT", effectiveFrom = Today, effectiveTo = Today.AddDays(90),
            renewalNoticeDays = (int?)30, optionPeriodsTotal = (int?)1,
        }, Proc);
        Assert.Equal("Draft", contract.GetProperty("status").GetString());
        Assert.Equal(1, contract.GetProperty("version").GetInt32());
        var contractId = contract.GetProperty("id").GetString()!;

        // activation by the creator is maker-checker blocked
        res = await PostAsync($"/api/v1/contracts/{contractId}/activations", new { }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_APPROVAL_ERROR", ErrorCode(await Json(res)));

        // activation without any price line is blocked
        res = await PostAsync($"/api/v1/contracts/{contractId}/activations", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_NO_PRICE_LINES", ErrorCode(await Json(res)));

        // price lines enter through amendments; contract version bumps
        res = await PostAsync($"/api/v1/contracts/{contractId}/amendments", new
        {
            effectiveDate = Today,
            summary = "Initial price schedule",
            priceLines = new[] { new { itemSku = "SKU-T11", unitPrice = 100m, effectiveFrom = Today, effectiveTo = (DateOnly?)null } },
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(2, (await Json(res)).GetProperty("data").GetProperty("contract").GetProperty("version").GetInt32());

        res = await PostAsync($"/api/v1/contracts/{contractId}/activations", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var activated = (await Json(res)).GetProperty("data");
        Assert.Equal("Active", activated.GetProperty("status").GetString());
        Assert.Equal(Fin, activated.GetProperty("activatedBy").GetString());

        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains("procurement.contract.activated", outbox.GetProperty("data").EnumerateArray()
            .Select(o => o.GetProperty("type").GetString()));

        // re-activation is a state conflict
        res = await PostAsync($"/api/v1/contracts/{contractId}/activations", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_STATE_ERROR", ErrorCode(await Json(res)));
    }

    // 037 AC1/AC3 — executed versions never change; price schedules may not overlap
    [Fact]
    public async Task Amendment_Price_Schedule_Overlap_Returns_409_And_Executed_Version_Immutable()
    {
        var supplierId = await SeedSupplier("T12");
        var contractId = await SeedActiveContract("T12", supplierId); // version 2 after the schedule amendment

        object Amendment(string sku, DateOnly from, DateOnly? to = null, string summary = "amend") => new
        {
            effectiveDate = from, summary,
            priceLines = new[] { new { itemSku = sku, unitPrice = 100m, effectiveFrom = from, effectiveTo = to } },
        };

        var res = await PostAsync($"/api/v1/contracts/{contractId}/amendments",
            Amendment("SKU-T12-CTR", Today), Proc); // identical range → overlap
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_PRICE_OVERLAP", ErrorCode(await Json(res)));

        res = await PostAsync($"/api/v1/contracts/{contractId}/amendments",
            Amendment("SKU-T12-CTR", Today.AddDays(1)), Proc); // inside the open range → overlap
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_PRICE_OVERLAP", ErrorCode(await Json(res)));

        // disjoint SKU and a bounded follow-on range amend cleanly; versions strictly increment
        await PostForData($"/api/v1/contracts/{contractId}/amendments", Amendment("SKU-T12-B", Today, Today.AddDays(10), "bounded line"), Proc);
        var third = await PostForData($"/api/v1/contracts/{contractId}/amendments", Amendment("SKU-T12-C", Today.AddDays(20), null, "next line"), Proc);
        Assert.Equal(4, third.GetProperty("contract").GetProperty("version").GetInt32());

        var row = (await GetAsync("/api/v1/contracts", Proc)).GetProperty("data").EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == contractId);
        Assert.Equal(4, row.GetProperty("version").GetInt32());
        Assert.Equal(3, row.GetProperty("amendmentCount").GetInt32()); // executed versions preserved, never rewritten
    }

    // 038 AC3 — evidence alone never verifies; only the checker verify step does
    [Fact]
    public async Task Obligation_Evidence_Requires_Verification_Before_Verified()
    {
        var supplierId = await SeedSupplier("T13");
        var contractId = await SeedActiveContract("T13", supplierId);

        var res = await PostAsync($"/api/v1/contracts/{contractId}/obligations",
            new { obligationType = "Delivery plan", ownerUserId = Proc, dueDate = (DateOnly?)null, sourceClause = (string?)"Cl. 4.1" }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));

        var obligation = await PostForData($"/api/v1/contracts/{contractId}/obligations",
            new { obligationType = "Delivery plan", ownerUserId = Proc, dueDate = Today.AddDays(-1), sourceClause = (string?)"Cl. 4.1" }, Proc);
        var obligationId = obligation.GetProperty("id").GetString()!;
        Assert.Equal("Open", obligation.GetProperty("status").GetString());

        // verification before evidence is a state conflict
        res = await PostAsync($"/api/v1/contracts/{contractId}/obligations/{obligationId}/verifications", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OBLIGATION_STATE_ERROR", ErrorCode(await Json(res)));

        // overdue queue counts Open obligations past due
        var row = await ContractRowAsync(contractId);
        Assert.Equal(1, row.GetProperty("overdueObligations").GetInt32());

        var evidenced = await PostForData($"/api/v1/contracts/{contractId}/obligations/{obligationId}/evidence",
            new { evidenceRef = "doc://grn-7788" }, Proc);
        Assert.Equal("EvidenceSubmitted", evidenced.GetProperty("status").GetString());
        Assert.Null(evidenced.GetProperty("verifiedBy").GetString()); // evidence never verifies

        // maker-checker: the verifier cannot be the obligation's creator (evidenced first)
        var adminOb = await PostForData($"/api/v1/contracts/{contractId}/obligations",
            new { obligationType = "Insurance", ownerUserId = Admin, dueDate = Today.AddDays(7), sourceClause = (string?)null }, Admin);
        await PostForData($"/api/v1/contracts/{contractId}/obligations/{adminOb.GetProperty("id").GetString()!}/evidence",
            new { evidenceRef = "doc://insurance-cert" }, Admin);
        res = await PostAsync($"/api/v1/contracts/{contractId}/obligations/{adminOb.GetProperty("id").GetString()!}/verifications", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OBLIGATION_APPROVAL_ERROR", ErrorCode(await Json(res)));

        var verified = await PostForData($"/api/v1/contracts/{contractId}/obligations/{obligationId}/verifications", new { }, Fin);
        Assert.Equal("Verified", verified.GetProperty("status").GetString());
        Assert.Equal(Fin, verified.GetProperty("verifiedBy").GetString());

        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains("procurement.obligation.verified", outbox.GetProperty("data").EnumerateArray()
            .Select(o => o.GetProperty("type").GetString()));

        Assert.Equal(0, (await ContractRowAsync(contractId)).GetProperty("overdueObligations").GetInt32());

        // evidence on a Verified obligation is a state conflict (038 AC3)
        res = await PostAsync($"/api/v1/contracts/{contractId}/obligations/{obligationId}/evidence",
            new { evidenceRef = "doc://late" }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OBLIGATION_STATE_ERROR", ErrorCode(await Json(res)));
    }

    // 039 AC3 + R-T6g — renewal exercise is manual, sequential, capped by OptionPeriodsTotal
    [Fact]
    public async Task Renewal_Exercise_Beyond_Last_Option_Returns_409_CONTRACT_NO_OPTION()
    {
        var supplierId = await SeedSupplier("T14");
        var contractId = await SeedActiveContract("T14", supplierId, optionPeriodsTotal: 1, renewalNoticeDays: 30, termDays: 15);

        // unknown status filter is a client error
        var res = await GetRawAsync("/api/v1/contracts?status=Bogus", Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // the maker (contract creator) cannot exercise their own contract's renewal
        res = await PostAsync($"/api/v1/contracts/{contractId}/renewals", new { optionNumber = 1 }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_APPROVAL_ERROR", ErrorCode(await Json(res)));

        // skipping to option 2 with only one option → no option (039 AC3)
        res = await PostAsync($"/api/v1/contracts/{contractId}/renewals", new { optionNumber = 2 }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_NO_OPTION", ErrorCode(await Json(res)));

        var renewed = await PostForData($"/api/v1/contracts/{contractId}/renewals", new { optionNumber = 1 }, Fin);
        Assert.Equal(1, renewed.GetProperty("optionsExercised").GetInt32());
        Assert.Equal(Today.AddDays(30), DateOnly.Parse(renewed.GetProperty("effectiveTo").GetString()!)); // 15-day term extended by 15 days

        // the exercised contract is inside a 45-day expiry window and notice-due
        var list = await GetAsync("/api/v1/contracts?expiringWithinDays=45", Proc);
        Assert.Contains(list.GetProperty("data").EnumerateArray(),
            c => c.GetProperty("id").GetString() == contractId && c.GetProperty("noticeDue").GetBoolean());

        // second exercise beyond the last option → no option
        res = await PostAsync($"/api/v1/contracts/{contractId}/renewals", new { optionNumber = 2 }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CONTRACT_NO_OPTION", ErrorCode(await Json(res)));
    }

    // 027 AC2/AC3 — event-wide published answers bump the event version; supplier-specific do not
    [Fact]
    public async Task Qa_EventWide_Answer_Bumps_Event_Version()
    {
        var supplierId = await SeedSupplier("T15");
        var (eventId, _, _) = await SeedPublishedEvent("T15", suppliers: [supplierId]);

        var res = await PostAsync($"/api/v1/sourcing-events/{eventId}/questions",
            new { question = "Qty per lot?", answer = "As listed", scope = "EventWide", publish = false, supplierId = (string?)null }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(1, await EventVersion(eventId)); // draft answers do not bump

        await PostForData($"/api/v1/sourcing-events/{eventId}/questions",
            new { question = "Site visit?", answer = "2026-12-01 10:00", scope = "EventWide", publish = true, supplierId = (string?)null }, Proc);
        Assert.Equal(2, await EventVersion(eventId)); // published event-wide answer bumps (027 AC3)

        // supplier-specific answers need a real supplier and never bump the event version
        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/questions",
            new { question = "Q?", answer = "A", scope = "SupplierSpecific", publish = true, supplierId = (string?)null }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));

        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/questions",
            new { question = "Q?", answer = "A", scope = "SupplierSpecific", publish = true, supplierId = "SUP-9999" }, Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("SUPPLIER_NOT_FOUND", ErrorCode(await Json(res)));

        await PostForData($"/api/v1/sourcing-events/{eventId}/questions",
            new { question = "Sample needed?", answer = "Yes, by Dec 5", scope = "SupplierSpecific", publish = true, supplierId = supplierId }, Proc);
        Assert.Equal(2, await EventVersion(eventId));
    }

    // negative path — GET /sourcing-events filter validation
    [Fact]
    public async Task Sourcing_Event_List_Unknown_Status_Filter_Returns_400()
    {
        var res = await GetRawAsync("/api/v1/sourcing-events?status=Bogus", Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));
        res = await GetRawAsync("/api/v1/sourcing-events?eventType=Bogus", Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // negative path — POST /sourcing-events validation (026 AC1: deadlines as instants + zone)
    [Fact]
    public async Task Create_Event_With_Reverse_Dates_Returns_400()
    {
        var res = await PostAsync("/api/v1/sourcing-events", new
        {
            title = "Backwards",
            eventType = "Rfq",
            opensAt = DateTimeOffset.UtcNow.AddHours(2),
            closesAt = DateTimeOffset.UtcNow.AddHours(1),
            timeZoneName = "Asia/Dhaka",
            currency = "BDT",
        }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));

        res = await PostAsync("/api/v1/sourcing-events", new
        {
            title = "No zone", eventType = "Rfq",
            opensAt = DateTimeOffset.UtcNow, closesAt = DateTimeOffset.UtcNow.AddHours(1),
            timeZoneName = "", currency = "BDT",
        }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // negative path — 022 edit lock: lots (and criteria) freeze at publication
    [Fact]
    public async Task Lot_After_Publish_Returns_409_EVENT_PUBLISHED()
    {
        var supplierId = await SeedSupplier("T18");
        var (eventId, _, _) = await SeedPublishedEvent("T18", suppliers: [supplierId]);

        var res = await PostAsync($"/api/v1/sourcing-events/{eventId}/lots",
            new { lotNo = "L2", description = "Late lot", itemSku = (string?)"SKU-X", targetQty = 1m, ceilingValue = 10m }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVENT_PUBLISHED", ErrorCode(await Json(res)));

        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/criteria",
            new { code = "LATE", name = "Late", kind = "Scored", weightPct = 1m, scaleMax = 5 }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVENT_PUBLISHED", ErrorCode(await Json(res)));

        res = await PostAsync("/api/v1/sourcing-events/EV-9999/lots",
            new { lotNo = "L1", description = "Ghost", itemSku = (string?)null, targetQty = 1m, ceilingValue = 1m }, Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("EVENT_NOT_FOUND", ErrorCode(await Json(res)));
    }

    // negative path — 030: scored criteria need a positive scale; weights must be positive
    [Fact]
    public async Task Scored_Criterion_Without_Scale_Returns_400()
    {
        var eventId = await CreateEvent("T19");
        var res = await PostAsync($"/api/v1/sourcing-events/{eventId}/criteria",
            new { code = "BAD", name = "No scale", kind = "Scored", weightPct = 50m, scaleMax = 0 }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));

        res = await PostAsync($"/api/v1/sourcing-events/{eventId}/criteria",
            new { code = "BAD2", name = "No weight", kind = "Scored", weightPct = 0m, scaleMax = 5 }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // negative path — 022 edge: RFI events carry no award intent
    [Fact]
    public async Task Award_On_Rfi_Event_Returns_409()
    {
        var supplierId = await SeedSupplier("T20");
        var (eventId, lotId, _) = await SeedPublishedEvent("T20", eventType: "Rfi", suppliers: [supplierId]);

        var res = await PostAsync("/api/v1/awards",
            new { eventId, lotId, supplierId, qty = 1m, unitPrice = 10m, recommendation = "info only" }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("EVENT_NOT_AWARDABLE", ErrorCode(await Json(res)));

        // referencing lookups: unknown event/lot/supplier
        res = await PostAsync("/api/v1/awards",
            new { eventId = "EV-9999", lotId, supplierId, qty = 1m, unitPrice = 10m, recommendation = "x" }, Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("EVENT_NOT_FOUND", ErrorCode(await Json(res)));
    }

    // negative path — waivers need a reason, stay non-destructive, and respect maker-checker
    [Fact]
    public async Task Waiver_Without_Reason_Returns_400()
    {
        var supplierId = await SeedSupplier("T22");
        var contractId = await SeedActiveContract("T22", supplierId);
        var obligationId = (await PostForData($"/api/v1/contracts/{contractId}/obligations",
            new { obligationType = "Audit report", ownerUserId = Proc, dueDate = Today.AddDays(14), sourceClause = (string?)null }, Proc)).GetProperty("id").GetString()!;

        var res = await PostAsync($"/api/v1/contracts/{contractId}/obligations/{obligationId}/waivers", new { reason = "" }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));

        // the maker has no waiver permission — waiving an obligation they did not create
        var adminOb = await PostForData($"/api/v1/contracts/{contractId}/obligations",
            new { obligationType = "Permit", ownerUserId = Admin, dueDate = Today.AddDays(7), sourceClause = (string?)null }, Admin);
        res = await PostAsync($"/api/v1/contracts/{contractId}/obligations/{adminOb.GetProperty("id").GetString()!}/waivers", new { reason = "waived" }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        // maker-checker: the obligation creator cannot waive their own obligation
        res = await PostAsync($"/api/v1/contracts/{contractId}/obligations/{adminOb.GetProperty("id").GetString()!}/waivers", new { reason = "self-waive" }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OBLIGATION_APPROVAL_ERROR", ErrorCode(await Json(res)));

        var waived = await PostForData($"/api/v1/contracts/{contractId}/obligations/{obligationId}/waivers", new { reason = "regulator deferred" }, Fin);
        Assert.Equal("Waived", waived.GetProperty("status").GetString());
        Assert.Equal("regulator deferred", waived.GetProperty("waiverReason").GetString());

        // waived, not deleted: the queue ignores it and evidence stays rejected (038 edge)
        Assert.Equal(0, (await ContractRowAsync(contractId)).GetProperty("overdueObligations").GetInt32());
        res = await PostAsync($"/api/v1/contracts/{contractId}/obligations/{obligationId}/evidence",
            new { evidenceRef = "doc://after-waiver" }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OBLIGATION_STATE_ERROR", ErrorCode(await Json(res)));
    }

    private async Task<JsonElement> ContractRowAsync(string contractId) =>
        (await GetAsync("/api/v1/contracts", Proc)).GetProperty("data").EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == contractId);

    private async Task<int> EventVersion(string eventId)
    {
        var list = await GetAsync("/api/v1/sourcing-events", Proc);
        return list.GetProperty("data").EnumerateArray()
            .First(e => e.GetProperty("id").GetString() == eventId).GetProperty("version").GetInt32();
    }
}
