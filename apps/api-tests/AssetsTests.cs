using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// ASSETS slice (ERP-ASSETS-001/002/003/019/023/024/026/037/041/043/044/046/047/048/051/053/055/056):
// fixed-asset register with capitalization + maker-checker, monthly depreciation runs with
// idempotent run ids, transfer history, impairment, disposal with GL handoff; projects with gated
// closure; service cases with SLA due + channel idempotency.
// Writer/checker: U-005 is the seeded assets_officer (R-ASSETS), U-001 is admin (*) — the checker.

namespace Erp.Api.Tests;

public class AssetsTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public AssetsTests(TestApiFactory factory) => _factory = factory;

    private const string Writer = "U-005"; // assets_officer — assets:write + assets:approve
    private const string Checker = "U-001"; // tenant admin — *

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

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    // creates a draft asset as the assets officer (LOC-A1 is the seeded stock location)
    private async Task<string> SeedAsset(string tag, decimal cost)
    {
        var res = await PostAsync("/api/v1/assets", new
        {
            tag,
            serial = $"SN-{tag}",
            name = $"Asset {tag}",
            category = "Equipment",
            cost,
            locationId = "LOC-A1",
        }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    // proposes + checker-approves capitalization so the asset lands Active (in service)
    private async Task<string> SeedActiveAsset(string tag, decimal cost)
    {
        var assetId = await SeedAsset(tag, cost);
        var res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations", new { sourceProjectId = (string?)null, amount = cost }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var eventId = (await Json(res)).GetProperty("data").GetProperty("event").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations/{eventId}/approvals", new { }, Checker);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return assetId;
    }

    private JsonElement AssetRow(JsonElement list, string assetId) =>
        list.GetProperty("data").EnumerateArray().First(a => a.GetProperty("id").GetString() == assetId);

    private static decimal SumLines(JsonElement journal, string side, string? accountCode = null) =>
        journal.GetProperty("lines").EnumerateArray()
            .Where(l => accountCode is null || l.GetProperty("accountCode").GetString() == accountCode)
            .Sum(l => l.GetProperty(side).GetDecimal());

    private static bool HasLine(JsonElement journal, string accountCode, decimal amount) =>
        journal.GetProperty("lines").EnumerateArray().Any(l =>
            l.GetProperty("accountCode").GetString() == accountCode
            && (l.GetProperty("debit").GetDecimal() == amount || l.GetProperty("credit").GetDecimal() == amount));

    [Fact]
    public async Task Create_Asset_Duplicate_Tag_Returns_409_ASSET_DUPLICATE_TAG()
    {
        var assetId = await SeedAsset("TAG-DUP-1", 1200m);
        var list = await GetAsync("/api/v1/assets", Checker);
        Assert.Equal("Draft", AssetRow(list, assetId).GetProperty("status").GetString());

        // same tag, different serial → rejected while the asset is active (041 AC1)
        var res = await PostAsync("/api/v1/assets", new
        {
            tag = "TAG-DUP-1", serial = "SN-OTHER", name = "Asset dup tag", category = "Equipment",
            cost = 500m, locationId = "LOC-A1",
        }, Writer);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ASSET_DUPLICATE_TAG", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // same serial, different tag → same uniqueness rule
        res = await PostAsync("/api/v1/assets", new
        {
            tag = "TAG-OTHER-1", serial = "SN-TAG-DUP-1", name = "Asset dup serial", category = "Equipment",
            cost = 500m, locationId = "LOC-A1",
        }, Writer);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ASSET_DUPLICATE_TAG", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // assets:write required
        var forbidden = await PostAsync("/api/v1/assets", new
        {
            tag = "TAG-DENY-1", serial = "SN-TAG-DENY-1", name = "No perm", category = "Equipment",
            cost = 100m, locationId = "LOC-A1",
        }, "U-004"); // sales officer — no assets permissions
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Capitalize_Approval_By_Creator_Returns_409_And_Ok_When_Checker()
    {
        var assetId = await SeedAsset("CAP-SOD-1", 4800m);
        var res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations", new { sourceProjectId = (string?)null, amount = 4800m }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var eventId = (await Json(res)).GetProperty("data").GetProperty("event").GetProperty("id").GetString()!;

        // maker-checker: the proposer cannot approve their own capitalization
        res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations/{eventId}/approvals", new { }, Writer);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("APPROVAL_MAKER_CHECKER", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // checker approves → asset lands Active with an in-service date (043/044)
        res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations/{eventId}/approvals", new { }, Checker);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await Json(res)).GetProperty("data");
        Assert.Equal("Active", data.GetProperty("asset").GetProperty("status").GetString());
        Assert.NotNull(data.GetProperty("asset").GetProperty("inServiceDate").GetString());
        Assert.Equal("Posted", data.GetProperty("event").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Capitalize_Posts_Balanced_Journal_Dr_1600_Cr_1610()
    {
        var assetId = await SeedAsset("CAP-GL-1", 4800m);
        var res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations", new { sourceProjectId = (string?)null, amount = 4800m }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var eventId = (await Json(res)).GetProperty("data").GetProperty("event").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/assets/{assetId}/capitalizations/{eventId}/approvals", new { }, Checker);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var journals = await GetAsync("/api/v1/journals", Checker);
        var jrn = journals.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("memo").GetString()!.Contains(eventId, StringComparison.Ordinal));
        Assert.Equal("1600", jrn.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(4800m, jrn.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("1610", jrn.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(4800m, jrn.GetProperty("lines")[1].GetProperty("credit").GetDecimal());
        Assert.Equal(SumLines(jrn, "debit"), SumLines(jrn, "credit"));

        var outbox = await GetAsync("/api/v1/outbox", Checker);
        Assert.Contains("assets.asset.capitalized", outbox.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList());
    }

    [Fact]
    public async Task Depreciation_Run_Replay_Same_Key_Posts_One_Journal()
    {
        var assetId = await SeedActiveAsset("DEP-T4", 600m); // monthly = round(600/60, 2) = 10.00
        var key = "DEP-RUN-T4";

        var journalsBefore = (await GetAsync("/api/v1/journals", Checker)).GetProperty("data").GetArrayLength();
        var res = await PostAsync("/api/v1/assets/depreciation-runs", new { periodId = "2026-11" }, Checker, key);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var first = (await Json(res)).GetProperty("data");
        Assert.Equal(key, first.GetProperty("id").GetString()); // Idempotency-Key IS the run id (048 AC1)
        Assert.Equal("Open", first.GetProperty("periodState").GetString()); // pilot guard: all periods Open
        // the run covers the whole active register — this test's asset must appear at its own
        // straight-line amount, and the run total must equal the sum of its per-asset lines
        var lines = first.GetProperty("lines").EnumerateArray().ToList();
        Assert.Contains(lines, l => l.GetProperty("assetId").GetString() == assetId
            && l.GetProperty("amount").GetDecimal() == 10.00m);
        Assert.Equal(lines.Sum(l => l.GetProperty("amount").GetDecimal()), first.GetProperty("totalAmount").GetDecimal());
        var journalId = first.GetProperty("journalId").GetString()!;
        var total = first.GetProperty("totalAmount").GetDecimal();
        var journalsAfterRun = (await GetAsync("/api/v1/journals", Checker)).GetProperty("data").GetArrayLength();
        Assert.Equal(journalsBefore + 1, journalsAfterRun);

        // same key → replayed response, one journal total (048 AC1)
        res = await PostAsync("/api/v1/assets/depreciation-runs", new { periodId = "2026-11" }, Checker, key);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var replay = (await Json(res)).GetProperty("data");
        Assert.Equal(key, replay.GetProperty("id").GetString());
        Assert.Equal(journalId, replay.GetProperty("journalId").GetString());
        Assert.Equal(journalsAfterRun, (await GetAsync("/api/v1/journals", Checker)).GetProperty("data").GetArrayLength());

        // the run journal is balanced: Dr 6400 Depreciation Expense / Cr 1500 Accumulated Depreciation
        var journals = await GetAsync("/api/v1/journals", Checker);
        var jrn = journals.GetProperty("data").EnumerateArray().First(j => j.GetProperty("id").GetString() == journalId);
        Assert.Equal(total, SumLines(jrn, "debit", "6400"));
        Assert.Equal(total, SumLines(jrn, "credit", "1500"));
        Assert.Equal(total, SumLines(jrn, "debit"));
        Assert.Equal(total, SumLines(jrn, "credit"));

        // our asset accumulated exactly one month of straight-line depreciation
        var row = AssetRow(await GetAsync("/api/v1/assets", Checker), assetId);
        Assert.Equal(10.00m, row.GetProperty("accumulatedDepreciation").GetDecimal());
        Assert.Equal(590.00m, row.GetProperty("bookValue").GetDecimal());
    }

    [Fact(Skip = "blocked on T5 periods — ERP-ASSETS-048 AC2 needs the periods table; the pilot guard "
        + "AssetsPeriods.IsOpen treats every period as Open (Depreciation_Run_Replay_Same_Key_Posts_One_Journal "
        + "pins that contract). When T5 lands: create a closed period, POST a run into it, assert 409 PERIOD_LOCKED.")]
    public async Task Depreciation_Run_Into_Closed_Period_Returns_409_PERIOD_LOCKED()
    {
        await SeedActiveAsset("DEP-T5", 600m);
        // once periods exist: mark period "2026-12" Closed, then this run must 409 with PERIOD_LOCKED
        var res = await PostAsync("/api/v1/assets/depreciation-runs", new { periodId = "2026-12" }, Checker);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("PERIOD_LOCKED", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disposal_At_Loss_Posts_Balanced_Journal_And_Stops_Schedule()
    {
        var asset1 = await SeedActiveAsset("DIS-T6A", 600m);
        var asset2 = await SeedActiveAsset("DIS-T6B", 600m);

        // one month of straight-line depreciation first (10.00 each) so the disposal carries
        // accumulated depreciation — the representative at-loss case
        var res = await PostAsync("/api/v1/assets/depreciation-runs", new { periodId = "2026-11" }, Checker, "DEP-RUN-T6-PRE");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // dispose asset1 with proceeds 100 → carrying 590, loss 490 (055 AC2 preview + 056 handoff)
        res = await PostAsync($"/api/v1/assets/{asset1}/disposals", new { proceeds = 100m, reason = "Scrapped" }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var proposed = await Json(res);
        var preview = proposed.GetProperty("data").GetProperty("preview");
        Assert.Equal(590m, preview.GetProperty("carryingValue").GetDecimal());
        Assert.Equal(-490m, preview.GetProperty("gainLoss").GetDecimal());
        var eventId = proposed.GetProperty("data").GetProperty("event").GetProperty("id").GetString()!;

        res = await PostAsync($"/api/v1/assets/{asset1}/disposals/{eventId}/approvals", new { }, Checker);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Disposed", (await Json(res)).GetProperty("data").GetProperty("asset").GetProperty("status").GetString());

        // GL handoff: Dr 1500 accum + Dr 1200 proceeds + Dr 6500 loss plug | Cr 1600 cost — balanced
        var journals = await GetAsync("/api/v1/journals", Checker);
        var jrn = journals.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("memo").GetString()!.Contains(eventId, StringComparison.Ordinal));
        Assert.Equal(10m, SumLines(jrn, "debit", "1500"));
        Assert.Equal(100m, SumLines(jrn, "debit", "1200"));
        Assert.Equal(490m, SumLines(jrn, "debit", "6500"));
        Assert.Equal(600m, SumLines(jrn, "credit", "1600"));
        Assert.Equal(SumLines(jrn, "debit"), SumLines(jrn, "credit"));
        // exactly one of 6500/6100 — never both
        Assert.True(HasLine(jrn, "6500", 490m));
        Assert.DoesNotContain(jrn.GetProperty("lines").EnumerateArray(),
            l => l.GetProperty("accountCode").GetString() == "6100");

        // disposal is terminal and stops depreciation: a run now excludes the disposed asset
        res = await PostAsync("/api/v1/assets/depreciation-runs", new { periodId = "2026-12" }, Checker, "DEP-RUN-T6");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var run = (await Json(res)).GetProperty("data");
        var lines = run.GetProperty("lines").EnumerateArray().ToList();
        Assert.Contains(lines, l => l.GetProperty("assetId").GetString() == asset2
            && l.GetProperty("amount").GetDecimal() == 10.00m);
        Assert.DoesNotContain(lines, l => l.GetProperty("assetId").GetString() == asset1);
        Assert.Equal(lines.Sum(l => l.GetProperty("amount").GetDecimal()), run.GetProperty("totalAmount").GetDecimal());

        var outbox = await GetAsync("/api/v1/outbox", Checker);
        Assert.Contains("assets.asset.disposed", outbox.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList());
    }

    [Fact]
    public async Task Project_Close_With_Open_Tasks_Returns_409_Listing_Tasks()
    {
        var res = await PostAsync("/api/v1/projects", new
        { name = "CIP Build T7", budget = 250000m, managerId = Writer, customerId = (string?)null }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var projectData = (await Json(res)).GetProperty("data");
        var projectId = projectData.GetProperty("id").GetString()!;
        Assert.Equal("BDT", projectData.GetProperty("currency").GetString());

        var taskIds = new List<string>();
        foreach (var name in new[] { "Excavation", "Foundation" })
        {
            res = await PostAsync($"/api/v1/projects/{projectId}/tasks", new { name, hoursEstimate = 40m }, Writer);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            taskIds.Add((await Json(res)).GetProperty("data").GetProperty("id").GetString()!);
        }

        // closure gate: open tasks are listed with their ids (019 AC1)
        res = await PostAsync($"/api/v1/projects/{projectId}/closure", new { }, Checker);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var gateBody = await Json(res);
        Assert.Equal("PROJECT_CLOSE_ERROR", gateBody.GetProperty("error").GetProperty("code").GetString());
        var message = gateBody.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains(taskIds[0], message);
        Assert.Contains(taskIds[1], message);

        // complete both tasks, then closure succeeds and is outboxed
        foreach (var taskId in taskIds)
        {
            res = await PostAsync($"/api/v1/projects/{projectId}/tasks/{taskId}/completion", new { hoursActual = 42.5m }, Writer);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
        res = await PostAsync($"/api/v1/projects/{projectId}/closure", new { }, Checker);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Closed", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        var outbox = await GetAsync("/api/v1/outbox", Checker);
        Assert.Contains("assets.project.closed", outbox.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList());
    }

    [Fact]
    public async Task Case_Illegal_Transition_Rejected_With_Code()
    {
        var res = await PostAsync("/api/v1/service-cases", new
        { title = "Pump leak T8", channel = "phone", requesterId = "U-004", priority = "High" }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var caseId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // Open → Resolved skips triage (024 AC1)
        res = await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "Resolved", reason = (string?)null }, Writer);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CASE_TRANSITION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // legal forward flow plus reopen; Closed is terminal
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "InProgress", reason = (string?)null }, Writer)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "Closed", reason = (string?)null }, Writer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "Waiting", reason = "Waiting for parts" }, Writer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "InProgress", reason = (string?)null }, Writer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "Resolved", reason = (string?)null }, Writer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "Open", reason = "Reopened by customer" }, Writer)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "Closed", reason = (string?)null }, Writer)).StatusCode); // Open → Closed illegal
    }

    [Fact]
    public async Task Case_Resolution_Without_Code_Returns_400()
    {
        var res = await PostAsync("/api/v1/service-cases", new
        { title = "Bearing noise T9", channel = "email", requesterId = "U-004", priority = "Medium" }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var caseId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // resolution needs a code (024 AC2)
        res = await PostAsync($"/api/v1/service-cases/{caseId}/resolution", new { resolutionCode = "" }, Writer);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // resolution from Open is an illegal transition
        res = await PostAsync($"/api/v1/service-cases/{caseId}/resolution", new { resolutionCode = "PARTS_REPLACED" }, Writer);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CASE_TRANSITION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/service-cases/{caseId}/transitions", new { status = "InProgress", reason = (string?)null }, Writer)).StatusCode);
        res = await PostAsync($"/api/v1/service-cases/{caseId}/resolution", new { resolutionCode = "PARTS_REPLACED" }, Writer);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await Json(res)).GetProperty("data");
        Assert.Equal("Resolved", data.GetProperty("status").GetString());
        Assert.Equal("PARTS_REPLACED", data.GetProperty("resolutionCode").GetString());

        var outbox = await GetAsync("/api/v1/outbox", Checker);
        Assert.Contains("assets.case.resolved", outbox.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList());
    }

    [Fact]
    public async Task Case_DueAt_Computed_From_Priority_And_Replay_Reuses_Case()
    {
        // R8d: flat priority-hours — High 24h (plain clock)
        var key = "CASE-CH-T10-1";
        var res = await PostAsync("/api/v1/service-cases", new
        { title = "Conveyor stop T10", channel = "portal", requesterId = "U-004", priority = "High" }, Writer, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var first = (await Json(res)).GetProperty("data");
        var created = first.GetProperty("createdAt").GetDateTimeOffset();
        var dueAt = first.GetProperty("dueAt").GetDateTimeOffset();
        Assert.InRange((dueAt - created).TotalHours, 23.9, 24.1);

        // same channel request key → the existing request is returned (023 AC2)
        res = await PostAsync("/api/v1/service-cases", new
        { title = "Conveyor stop T10", channel = "portal", requesterId = "U-004", priority = "High" }, Writer, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var replay = (await Json(res)).GetProperty("data");
        Assert.Equal(first.GetProperty("id").GetString(), replay.GetProperty("id").GetString());
        Assert.Equal(dueAt, replay.GetProperty("dueAt").GetDateTimeOffset());

        // Low → 72h
        res = await PostAsync("/api/v1/service-cases", new
        { title = "Paint scratch T10", channel = "email", requesterId = "U-004", priority = "Low" }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var low = (await Json(res)).GetProperty("data");
        Assert.InRange((low.GetProperty("dueAt").GetDateTimeOffset() - low.GetProperty("createdAt").GetDateTimeOffset()).TotalHours, 71.9, 72.1);
    }

    [Fact]
    public async Task Impairment_Amount_Bounds_And_Approval_Posts_6600_1600()
    {
        var assetId = await SeedActiveAsset("IMP-T11", 600m);

        // amount <= 0 → 400; amount > bookValue → 400 (053 AC2)
        var res = await PostAsync($"/api/v1/assets/{assetId}/impairments", new { amount = 0m, reason = "Damage" }, Writer);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
        res = await PostAsync($"/api/v1/assets/{assetId}/impairments", new { amount = 999999m, reason = "Damage" }, Writer);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // valid proposal carries the carrying-amount preview
        res = await PostAsync($"/api/v1/assets/{assetId}/impairments", new { amount = 150m, reason = "Flood damage" }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var proposed = await Json(res);
        var preview = proposed.GetProperty("data").GetProperty("preview");
        Assert.Equal(600m, preview.GetProperty("priorCarryingAmount").GetDecimal());
        Assert.Equal(450m, preview.GetProperty("resultingAmount").GetDecimal());
        var eventId = proposed.GetProperty("data").GetProperty("event").GetProperty("id").GetString()!;

        // maker-checker, then journal Dr 6600 / Cr 1600
        res = await PostAsync($"/api/v1/assets/{assetId}/impairments/{eventId}/approvals", new { }, Writer);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("APPROVAL_MAKER_CHECKER", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        res = await PostAsync($"/api/v1/assets/{assetId}/impairments/{eventId}/approvals", new { }, Checker);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var journals = await GetAsync("/api/v1/journals", Checker);
        var jrn = journals.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("memo").GetString()!.Contains(eventId, StringComparison.Ordinal));
        Assert.Equal(150m, SumLines(jrn, "debit", "6600"));
        Assert.Equal(150m, SumLines(jrn, "credit", "1600"));
        Assert.Equal(450m, AssetRow(await GetAsync("/api/v1/assets", Checker), assetId).GetProperty("bookValue").GetDecimal());

        var outbox = await GetAsync("/api/v1/outbox", Checker);
        Assert.Contains("assets.asset.impaired", outbox.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList());
    }

    [Fact]
    public async Task Transfer_Moves_Location_Records_History_And_404s_Unknown_Location()
    {
        var assetId = await SeedActiveAsset("TRF-T12", 600m);

        var res = await PostAsync($"/api/v1/assets/{assetId}/transfers", new
        { toLocationId = "LOC-B1", effectiveAt = "2026-11-24T10:00:00+00:00" }, Writer);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var evt = (await Json(res)).GetProperty("data").GetProperty("event");
        Assert.Equal("transfer", evt.GetProperty("eventType").GetString());
        Assert.Equal("LOC-A1", evt.GetProperty("fromLocationId").GetString());
        Assert.Equal("LOC-B1", evt.GetProperty("toLocationId").GetString());
        Assert.Equal("LOC-B1", AssetRow(await GetAsync("/api/v1/assets", Checker), assetId).GetProperty("locationId").GetString());

        // unknown location → 404; same location → 400
        res = await PostAsync($"/api/v1/assets/{assetId}/transfers", new
        { toLocationId = "LOC-NOPE", effectiveAt = "2026-11-24T10:00:00+00:00" }, Writer);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("LOCATION_NOT_FOUND", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        res = await PostAsync($"/api/v1/assets/{assetId}/transfers", new
        { toLocationId = "LOC-B1", effectiveAt = "2026-11-24T11:00:00+00:00" }, Writer);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }
}
