using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

// FINANCE-deep P1 (ERP-FINANCE-004/005/062/063): entity fiscal calendars (4-4-5 + ADJUSTMENT
// periods + the 53-week edge) with activation overlap/gap rejection, the close-checklist
// dashboard (evidence/waiver/NA states) and the 063 money gate on fiscal-period close.
// Brief §7 items 1–6 + item 28 negative sweep. Seeds exercised: FCAL-0001 (draft 4-4-5 FY2027),
// CTK-0001 (mandatory reconciliation task on the dedicated seeded FP-P1-DEMO period), R-FIN2/U-015.
// ORDER-INDEPENDENCE: activations follow the 004 AC2 chain rule (a new year must be contiguous
// with existing coverage or be the first), so every test fills coverage with EnsureCoverage()
// up to the year it needs, and probes overlap/gap on ranges no other test touches (2030-inside
// draft; 2040 gap). Whichever order xUnit picks, coverage stays a gapless 2029..2033 chain.

namespace Erp.Api.Tests;

public class FinanceDeepP1Tests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public FinanceDeepP1Tests(TestApiFactory factory) => _factory = factory;

    private const string Admin = "U-001";
    private const string Fin = "U-003";
    private const string FinMaker = "U-015"; // R-FIN2: no close:write / close:approve / periods:write

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? token)
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
        var res = await GetRawAsync(path, token);
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    // keeps the 004 AC2 coverage chain gapless from 2029 up to `throughYear`: creates + activates
    // any missing calendar year (200/409 both count — another test may have activated it already)
    private async Task EnsureCoverage(int throughYear)
    {
        for (var year = 2029; year <= throughYear; year++)
        {
            var start = $"{year}-01-01";
            var existing = (await GetAsync("/api/v1/fiscal-calendars", Fin)).GetProperty("data")
                .EnumerateArray().FirstOrDefault(c => c.GetProperty("startDate").GetString() == start
                    && c.GetProperty("pattern").GetString() == "FourFourFive");
            if (existing.ValueKind is not JsonValueKind.Undefined && existing.GetProperty("activated").GetBoolean()) continue;
            var id = existing.ValueKind is not JsonValueKind.Undefined
                ? existing.GetProperty("id").GetString()!
                : await CreateCalendar($"FY{year} 4-4-5", year, "FourFourFive", start);
            var res = await Activate(id);
            Assert.True(res.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"ensure {year}: {(int)res.StatusCode}");
        }
    }

    private async Task<string> CreateCalendar(string name, int year, string pattern, string startDate, string token = Fin)
    {
        var res = await PostAsync("/api/v1/fiscal-calendars", new { name, year, pattern, startDate }, token);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<HttpResponseMessage> Activate(string calendarId, string token = Fin) =>
        await PostAsync($"/api/v1/fiscal-calendars/{calendarId}/activations", new { }, token);

    // a dedicated probe period months ahead of the seeded window (which ends at current+1) and
    // before the 2029 calendar coverage
    private async Task<string> CreatePeriod(int monthsAhead, string tag)
    {
        var start = MonthStart(Today).AddMonths(monthsAhead);
        var res = await PostAsync("/api/v1/fiscal-periods",
            new { name = $"{start:yyyy-MM}-{tag}", startDate = start, endDate = start.AddMonths(1).AddDays(-1) }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<List<JsonElement>> CreateChecklist(string periodId, object[] tasks)
    {
        var res = await PostAsync("/api/v1/close-checklists", new { periodId, tasks }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").EnumerateArray().ToList();
    }

    private async Task<List<JsonElement>> TasksFor(string periodId)
    {
        var list = await GetAsync("/api/v1/close-checklists", Fin);
        return list.GetProperty("data").EnumerateArray()
            .Where(t => t.GetProperty("periodId").GetString() == periodId).ToList();
    }

    // 004 AC2 — activation rejects overlapping coverage (CALENDAR_OVERLAP) and holes in the
    // year chain (CALENDAR_GAP); a contiguous year activates and materializes its periods.
    [Fact]
    public async Task Calendar_Activation_Rejects_Overlap_And_Gap_And_Chain_Continues()
    {
        await EnsureCoverage(2030); // gapless 2029..2030 chain in every test order

        // a mid-year draft is refused AT CREATE naming the Jan-1 rule (P1 review 4) — inverted
        // rows can never be stored, so the overlap probe uses a same-year different-pattern
        // calendar instead (allowed at create, refused at activation)
        var shifted = await PostAsync("/api/v1/fiscal-calendars",
            new { name = "FY2030 months shifted", year = 2030, pattern = "CalendarMonths", startDate = "2030-06-01" }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, shifted.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(shifted)).GetProperty("error").GetProperty("code").GetString());
        var overlapDraft = await CreateCalendar("FY2030 months", 2030, "CalendarMonths", "2030-01-01");
        var overlap = await Activate(overlapDraft);
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
        Assert.Equal("CALENDAR_OVERLAP", (await Json(overlap)).GetProperty("error").GetProperty("code").GetString());

        // a hole (FY2040 while FY2031..2039 are unconfigured) is refused — accounting coverage
        // must form a gapless chain (postings in the hole would be PERIOD_NOT_CONFIGURED)
        var gapDraft = await CreateCalendar("FY2040 4-4-5", 2040, "FourFourFive", "2040-01-01");
        var gap = await Activate(gapDraft);
        Assert.Equal(HttpStatusCode.BadRequest, gap.StatusCode);
        Assert.Equal("CALENDAR_GAP", (await Json(gap)).GetProperty("error").GetProperty("code").GetString());

        // the missing link activates (contiguous with FY2030) and materializes its periods
        var fy31 = await CreateCalendar("FY2031 4-4-5", 2031, "FourFourFive", "2031-01-01");
        Assert.Equal(HttpStatusCode.OK, (await Activate(fy31)).StatusCode);
        var periods = await GetAsync("/api/v1/fiscal-periods", Admin);
        Assert.Contains(periods.GetProperty("data").EnumerateArray(),
            p => p.GetProperty("name").GetString() == "2031-P01");

        // double activation is a state conflict; unknown id is a 404
        Assert.Equal(HttpStatusCode.Conflict, (await Activate(fy31)).StatusCode);
        var missing = await Activate("FCAL-9999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("CALENDAR_NOT_FOUND", (await Json(missing)).GetProperty("error").GetProperty("code").GetString());
    }

    // 004 AC1 — a 4-4-5 calendar maps boundary accounting dates to the configured year/period/
    // adjustment period: 12 periods walk 4-4-5 weeks from the start, the ADJ period carries the
    // year remainder (52-week engine — see UnnameableLayout) and is flagged on the materialized
    // period rows.
    [Fact]
    public async Task Four_Five_Calendar_Maps_Boundary_Dates_To_Periods_And_Flags_Adjustment()
    {
        await EnsureCoverage(2029);
        var calendar = (await GetAsync("/api/v1/fiscal-calendars", Fin)).GetProperty("data")
            .EnumerateArray().First(c => c.GetProperty("startDate").GetString() == "2029-01-01");
        var periods = calendar.GetProperty("periods").EnumerateArray().ToList();
        Assert.Equal(13, periods.Count); // 12 accounting periods + ADJ

        // quarter boundary: P03 is the 5-week period — it ends on start + 90 days (day 91),
        // and P04 starts the very next day; 12 periods of 4-4-5 cover 364 days, so ADJ (the
        // year remainder, 1-2 days on a Gregorian year) starts on day 365
        var p1 = periods[0]; var p3 = periods[2]; var p4 = periods[3]; var adj = periods[12];
        Assert.Equal(new DateOnly(2029, 1, 1), DateOnly.Parse(p1.GetProperty("startDate").GetString()!));
        var p3End = DateOnly.Parse(p3.GetProperty("endDate").GetString()!);
        Assert.Equal(new DateOnly(2029, 1, 1).AddDays(90), p3End);
        Assert.Equal(p3End.AddDays(1), DateOnly.Parse(p4.GetProperty("startDate").GetString()!));
        Assert.False(p3.GetProperty("isAdjustment").GetBoolean());
        Assert.Equal(new DateOnly(2029, 1, 1).AddDays(364), DateOnly.Parse(adj.GetProperty("startDate").GetString()!));
        Assert.Equal(new DateOnly(2029, 12, 31), DateOnly.Parse(adj.GetProperty("endDate").GetString()!));
        Assert.True(adj.GetProperty("isAdjustment").GetBoolean());

        // activation materializes the periods with the adjustment flag ON the period rows
        var fiscalPeriods = await GetAsync("/api/v1/fiscal-periods", Admin);
        var materialized = fiscalPeriods.GetProperty("data").EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "2029-ADJ");
        Assert.True(materialized.GetProperty("isAdjustment").GetBoolean());
        Assert.Equal("Open", materialized.GetProperty("status").GetString());
        var regular = fiscalPeriods.GetProperty("data").EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "2029-P01");
        Assert.False(regular.GetProperty("isAdjustment").GetBoolean());
    }

    // 063 AC1 — close with an incomplete mandatory close task is rejected naming the task;
    // completing it with evidence clears the gate.
    [Fact]
    public async Task Close_With_Incomplete_Mandatory_Task_Is_Blocked_Naming_The_Task()
    {
        var periodId = await CreatePeriod(2, "p1gate");
        var tasks = await CreateChecklist(periodId, new[] { new { title = "Bank reconciliation", mandatory = true } });
        var taskId = tasks[0].GetProperty("id").GetString()!;

        var res = await PostAsync($"/api/v1/fiscal-periods/{periodId}/close", new { reason = "month end" }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("CLOSE_GATE_BLOCKED", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(taskId, body.GetProperty("error").GetProperty("message").GetString());

        // evidence completion clears the gate — the period then closes
        var done = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks/{taskId}/completions",
            new { evidenceRef = "recon-workpaper.pdf" }, Fin);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal("Completed", (await Json(done)).GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/fiscal-periods/{periodId}/close", new { reason = "month end" }, Admin)).StatusCode);
    }

    // 063 AC2 — the waiver variant records reason/user/time and RETAINS the unresolved item in
    // close evidence (task rows keep Waived + reason + who + when; audit event carries them).
    [Fact]
    public async Task Waiver_Close_Records_Reason_User_Time_And_Retains_The_Unresolved_Item()
    {
        var periodId = await CreatePeriod(3, "p1waiver");
        var tasks = await CreateChecklist(periodId, new[] { new { title = "AP subledger recon", mandatory = true } });
        var taskId = tasks[0].GetProperty("id").GetString()!;

        // waiver without a reason is refused — the reason is the evidence
        var noReason = await PostAsync($"/api/v1/fiscal-periods/{periodId}/close",
            new { waiveBlockers = true }, Admin);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var res = await PostAsync($"/api/v1/fiscal-periods/{periodId}/close",
            new { waiveBlockers = true, reason = "CFO authorized: recon evidence pending audit pack" }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Closed", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        // the unresolved item is retained — never removed — with the waiver's reason/user/time
        var retained = (await TasksFor(periodId)).Single(t => t.GetProperty("id").GetString() == taskId);
        Assert.Equal("Waived", retained.GetProperty("status").GetString());
        Assert.Contains("CFO authorized", retained.GetProperty("waiverReason").GetString());
        Assert.Equal(Admin, retained.GetProperty("completedBy").GetString());
        Assert.False(string.IsNullOrEmpty(retained.GetProperty("completedAt").GetString()));

        var events = await GetAsync("/api/v1/audit-events", Admin);
        Assert.Contains(events.GetProperty("data").EnumerateArray(),
            e => e.GetProperty("entityType").GetString() == "close_task"
              && ((e.GetProperty("summary").GetString() ?? "").Contains(taskId)));
    }

    // 062 AC1/AC3 + dashboard buckets — completion without evidence or waiver → 400; N/A
    // without reason → 400; N/A is retained with its reason (never silently removed); the
    // task list carries dueDate/dependsOnTaskId so the dashboard can bucket overdue/blocked.
    [Fact]
    public async Task Completion_Needs_Evidence_Or_Waiver_Na_Needs_Reason_And_Is_Retained()
    {
        var periodId = await CreatePeriod(4, "p1tasks");
        var overdueDate = MonthStart(Today).AddDays(-3);
        var tasks = await CreateChecklist(periodId, new object[]
        {
            new { title = "Revenue tie-out", mandatory = true, dueDate = (DateOnly?)overdueDate },
            new { title = "Ad-hoc inventory sample count", mandatory = false },
        });
        var first = tasks[0].GetProperty("id").GetString()!;
        var third = tasks[1].GetProperty("id").GetString()!;

        // the additive route adds a task DEPENDENT on the first — a blocked-task fixture
        var addRes = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks",
            new { title = "Intercompany elimination", mandatory = true, dependsOnTaskId = first }, Fin);
        Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);
        var second = (await Json(addRes)).GetProperty("data").GetProperty("id").GetString()!;

        var noEvidence = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks/{first}/completions", new { }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, noEvidence.StatusCode);
        Assert.Equal("CLOSE_TASK_EVIDENCE_REQUIRED", (await Json(noEvidence)).GetProperty("error").GetProperty("code").GetString());

        var noReason = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks/{third}/na", new { }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("CLOSE_TASK_REASON_REQUIRED", (await Json(noReason)).GetProperty("error").GetProperty("code").GetString());

        var done = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks/{first}/completions",
            new { evidenceRef = "revenue-tie-out.xlsx" }, Fin);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var completed = (await Json(done)).GetProperty("data");
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal("revenue-tie-out.xlsx", completed.GetProperty("evidenceRef").GetString());
        Assert.Equal(Fin, completed.GetProperty("completedBy").GetString());

        // re-completing a terminal task is a state conflict
        var again = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks/{first}/completions",
            new { evidenceRef = "again.xlsx" }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("CLOSE_TASK_STATE_ERROR", (await Json(again)).GetProperty("error").GetProperty("code").GetString());

        var na = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks/{third}/na",
            new { reason = "no inventory movements this period" }, Fin);
        Assert.Equal(HttpStatusCode.OK, na.StatusCode);
        var naData = (await Json(na)).GetProperty("data");
        Assert.Equal("NotApplicable", naData.GetProperty("status").GetString());
        Assert.Contains("no inventory movements", naData.GetProperty("naReason").GetString());

        // N/A is retained in the list with its reason — never silently removed — and the rows
        // carry the dashboard fields (overdue dueDate, blocked dependency)
        var rows = await TasksFor(periodId);
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, t => t.GetProperty("status").GetString() == "NotApplicable"
            && ((t.GetProperty("naReason").GetString() ?? "").Contains("no inventory movements")));
        var overdueRow = rows.Single(t => t.GetProperty("id").GetString() == first);
        Assert.Equal(overdueDate, DateOnly.Parse(overdueRow.GetProperty("dueDate").GetString()!));
        var blockedRow = rows.Single(t => t.GetProperty("id").GetString() == second);
        Assert.Equal(first, blockedRow.GetProperty("dependsOnTaskId").GetString());
        Assert.Equal("Pending", blockedRow.GetProperty("status").GetString());

        // an unknown task id is a 404
        var ghost = await PostAsync($"/api/v1/close-checklists/{periodId}/tasks/CTK-9999/na",
            new { reason = "x" }, Fin);
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
    }

    // 063 edge — a late source event after closure routes to the adjustment workflow (the open
    // designated adjustment period), never a backdated posting into the closed period.
    [Fact]
    public async Task Late_Source_Event_After_Closure_Routes_To_The_Adjustment_Period()
    {
        await EnsureCoverage(2033);
        var periods = await GetAsync("/api/v1/fiscal-periods", Admin);
        var rows = periods.GetProperty("data").EnumerateArray().ToList();
        var p01 = rows.First(p => p.GetProperty("name").GetString() == "2033-P01");
        var adj = rows.First(p => p.GetProperty("name").GetString() == "2033-ADJ");
        var p01Date = DateOnly.Parse(p01.GetProperty("startDate").GetString()!).AddDays(10);
        // dated INSIDE the adjustment period (2029+ 4-4-5 years leave ADJ just 1-2 days wide)
        var adjStart = DateOnly.Parse(adj.GetProperty("startDate").GetString()!);
        var adjEnd = DateOnly.Parse(adj.GetProperty("endDate").GetString()!);
        var adjDate = adjStart <= adjEnd ? adjStart : adjEnd;

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/fiscal-periods/{p01.GetProperty("id").GetString()}/close", new { reason = "period lock probe" }, Admin)).StatusCode);

        object[] Lines(decimal amount) => new object[]
        {
            new { accountCode = "1400", accountName = "Inventory", debit = amount, credit = 0m },
            new { accountCode = "2100", accountName = "Accounts Payable", debit = 0m, credit = amount },
        };

        // a draft dated in the closed period is refused at approval, naming the adjustment route
        var draft = await PostAsync("/api/v1/journals", new { memo = "late vendor bill", lines = Lines(10m), date = p01Date }, Fin);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var draftId = (await Json(draft)).GetProperty("data").GetProperty("id").GetString()!;
        var approve = await PostAsync($"/api/v1/journals/{draftId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        var locked = await Json(approve);
        Assert.Equal("PERIOD_LOCKED", locked.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("2033-ADJ", locked.GetProperty("error").GetProperty("message").GetString());

        // the SAME event dated in the OPEN adjustment period posts — the adjustment workflow
        var adjusted = await PostAsync("/api/v1/journals", new { memo = "late vendor bill (adjustment)", lines = Lines(10m), date = adjDate }, Fin);
        Assert.Equal(HttpStatusCode.Created, adjusted.StatusCode);
        var adjustedId = (await Json(adjusted)).GetProperty("data").GetProperty("id").GetString()!;
        var posted = await PostAsync($"/api/v1/journals/{adjustedId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal("Posted", (await Json(posted)).GetProperty("data").GetProperty("status").GetString());
    }

    // Brief §7 item 28 — R-FIN2 maker (U-015) gets envelope errors on every checklist mutation
    // and on the waive-close; reads stay open (periods:read covers calendar/checklist lists) and
    // R-FIN (U-003) carries close:write + close:approve.
    [Fact]
    public async Task Authz_Sweep_R_FIN2_Blocked_On_Close_Mutations_U003_Succeeds()
    {
        var periodId = await CreatePeriod(5, "p1authz");
        var seeded = await GetAsync("/api/v1/close-checklists", FinMaker);
        Assert.True(seeded.GetProperty("success").GetBoolean()); // reads stay open for the maker
        var calendars = await GetRawAsync("/api/v1/fiscal-calendars", FinMaker);
        Assert.Equal(HttpStatusCode.OK, calendars.StatusCode);

        async Task Sweep(string path, object? body)
        {
            var res = await PostAsync(path, body, FinMaker);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            var err = await Json(res);
            Assert.False(err.GetProperty("success").GetBoolean());
            Assert.Equal("FORBIDDEN", err.GetProperty("error").GetProperty("code").GetString());
        }

        await Sweep("/api/v1/fiscal-calendars", new { name = "maker calendar", year = 2035, pattern = "FourFourFive", startDate = "2035-01-01" });
        await Sweep("/api/v1/fiscal-calendars/FCAL-0001/activations", new { });
        await Sweep("/api/v1/close-checklists", new { periodId, tasks = new[] { new { title = "x", mandatory = true } } });
        await Sweep($"/api/v1/close-checklists/{periodId}/tasks", new { title = "y", mandatory = false });
        await Sweep($"/api/v1/close-checklists/{periodId}/tasks/CTK-0001/completions", new { evidenceRef = "e" });
        await Sweep($"/api/v1/close-checklists/{periodId}/tasks/CTK-0001/na", new { reason = "r" });
        await Sweep($"/api/v1/fiscal-periods/{periodId}/close", new { waiveBlockers = true, reason = "maker waive" });

        // U-003 (R-FIN) succeeds where the maker was blocked — calendar create + checklist create
        var okCal = await PostAsync("/api/v1/fiscal-calendars",
            new { name = "FY2035 4-4-5", year = 2035, pattern = "FourFourFive", startDate = "2035-01-01" }, Fin);
        Assert.Equal(HttpStatusCode.Created, okCal.StatusCode);
        var okChecklist = await PostAsync("/api/v1/close-checklists",
            new { periodId, tasks = new[] { new { title = "FX revaluation check", mandatory = true } } }, Fin);
        Assert.Equal(HttpStatusCode.Created, okChecklist.StatusCode);
    }

    // 062 — the seeded demo checklist is present on a fresh database: a mandatory pending
    // reconciliation task on the dedicated seeded demo period FP-P1-DEMO (a far-future month the
    // P1 migration seeds itself — AddFinanceSeed's rolling months stop at the current month, so
    // no apply-time-computed period id is guaranteed to exist), and the PERIOD ROW is asserted
    // to exist so the gate is demonstrable from the UI out of the box, never orphaned.
    [Fact]
    public async Task Seeded_Demo_Checklist_Carries_A_Mandatory_Pending_Task()
    {
        var list = await GetAsync("/api/v1/close-checklists", Fin);
        var rows = list.GetProperty("data").EnumerateArray().ToList();
        var seeded = rows.FirstOrDefault(t => t.GetProperty("id").GetString() == "CTK-0001");
        Assert.False(seeded.ValueKind == JsonValueKind.Undefined, "seeded CTK-0001 demo task missing");
        Assert.True(seeded.GetProperty("mandatory").GetBoolean());
        Assert.Equal("Pending", seeded.GetProperty("status").GetString());
        Assert.Contains("reconcil", seeded.GetProperty("title").GetString()!.ToLowerInvariant());

        // the task's period is REACHABLE: the migration seeds FP-P1-DEMO and points the task at it
        Assert.Equal("FP-P1-DEMO", seeded.GetProperty("periodId").GetString());
        var periods = await GetAsync("/api/v1/fiscal-periods", Admin);
        var demo = periods.GetProperty("data").EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("id").GetString() == "FP-P1-DEMO");
        Assert.False(demo.ValueKind == JsonValueKind.Undefined, "seeded demo period FP-P1-DEMO missing");
        Assert.Equal("Open", demo.GetProperty("status").GetString());
    }

    // 004 edge (P1 review 3): the engine is 52-WEEK 4-4-5 ONLY — a stored calendar whose year
    // cannot be laid out as 12 × 4-4-5 periods plus a ≤1-week adjustment remainder must refuse
    // activation loudly (CALENDAR_VALIDATION naming the year), never materialize inverted rows.
    // The degenerate draft is planted straight into the store (JournalRaceHardening raw-context
    // convention): the API create path refuses mid-year starts, so only stored data reaches this
    // guard. FY2045 touches no other suite's coverage or probes; the finally-block removes the
    // calendar (its owned rows cascade) and any materialized 2045 periods.
    [Fact]
    public async Task Unnameable_Calendar_Layout_Is_Refused_At_Activation_With_Calendar_Validation()
    {
        const string calId = "FCAL-P1GUARD";
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        using (var db = new ErpDbContext(options))
        {
            var degenerate = new Erp.Api.FiscalCalendar(calId, "T-001", "LE-001", "FY2045 shifted (guard probe)",
                2045, Erp.Api.CalendarPattern.FourFourFive, new DateOnly(2045, 6, 1), new DateOnly(2045, 12, 31), Activated: false);
            var cursor = new DateOnly(2045, 6, 1);
            int[] weeks = [4, 4, 5];
            for (var q = 0; q < 4; q++)
                for (var i = 0; i < 3; i++)
                {
                    var periodEnd = cursor.AddDays(weeks[i] * 7 - 1);
                    degenerate.Periods.Add(new Erp.Api.CalendarPeriod($"2045-P{degenerate.Periods.Count + 1:00}",
                        cursor, periodEnd, IsAdjustment: false));
                    cursor = periodEnd.AddDays(1);
                }
            // the 364-day walk overruns the year — the ADJ row is INVERTED (start > end)
            degenerate.Periods.Add(new Erp.Api.CalendarPeriod("2045-ADJ", cursor, new DateOnly(2045, 12, 31), IsAdjustment: true));
            db.FiscalCalendars.Add(degenerate);
            db.SaveChanges();
        }
        try
        {
            var res = await Activate(calId);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var body = await Json(res);
            Assert.Equal("CALENDAR_VALIDATION", body.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("2045", body.GetProperty("error").GetProperty("message").GetString());
        }
        finally
        {
            using var db = new ErpDbContext(options);
            await db.FiscalPeriods.Where(p => p.Name.StartsWith("2045-")).ExecuteDeleteAsync();
            await db.FiscalCalendars.Where(c => c.Id == calId).ExecuteDeleteAsync();
        }
    }

    // 004 (P1 review 4): a mid-year StartDate walks its period layout past Dec 31 and would
    // materialize INVERTED rows (StartDate > EndDate) that show up in the gate dropdown. The
    // create refuses naming the requirement — StartDate must be January 1 of Year; the same
    // rule is re-checked at activation on stored rows. FY2036 touches no other suite.
    [Fact]
    public async Task Mid_Year_StartDate_Is_Refused_At_Create_Naming_The_Jan_1_Rule()
    {
        var res = await PostAsync("/api/v1/fiscal-calendars",
            new { name = "FY2036 shifted", year = 2036, pattern = "FourFourFive", startDate = "2036-06-01" }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("January 1", body.GetProperty("error").GetProperty("message").GetString());
    }
}
