using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

// PEOPLE deep slice (workers 004/005/006/007/024, time 025/026/027, payroll runs
// 034/035/037/038/039/046/047/048/049/051/053/055/056). Controller rulings R-PEO1..11 are
// binding: ONE-STEP worker register (no separate person entity — R-PEO3), pay runs anchor to
// FiscalPeriod rows via PayDate (R-PEO5), the period guard evaluates the run's PayDate
// (R-PEO11), R-HR makes and R-FIN approves/posts (R-PEO9), BDT only (R-PEO7).
//
// The tests are split across four classes ON PURPOSE: every test class gets its own throwaway
// database (TestApiFactory), and payroll calculations apply every ACTIVE deduction code in the
// entity — sharing one database would bleed TAX-10 (10%) into the golden posting's totals. The
// split keeps each database's deduction catalog exactly the set its assertions assume:
//   - PeopleWorkerTests         register/lifecycle/compensation/membership (no payroll codes)
//   - PeopleTimeTests           capture idempotency + correction trail
//   - PeoplePayrollTests        catalog gate, run key, frozen calc, reconciliation, validations,
//                               maker-checker approval, closed-period guard, payslip scope (TAX-10 only)
//   - PeoplePayrollPostingTests golden posting + reversal (FLAT-100 only, exact Dr/Cr legs)

namespace Erp.Api.Tests;

public abstract class PeopleTestBase : IClassFixture<TestApiFactory>
{
    protected readonly TestApiFactory Factory;
    protected PeopleTestBase(TestApiFactory factory) => Factory = factory;

    protected const string Admin = "U-001";
    protected const string Fin = "U-003";   // R-FIN — payroll:approve / paycodes:write
    protected const string Hr = "U-007";    // R-HR — workers/time/payroll:calculate

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    protected static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    protected async Task<HttpResponseMessage> PostAsync(string path, object body, string? token, string? idemKey = null)
    {
        var client = Factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        if (idemKey is not null) client.DefaultRequestHeaders.Add("Idempotency-Key", idemKey);
        return await client.PostAsJsonAsync(path, body);
    }

    protected async Task<JsonElement> GetAsync(string path, string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await Json(await client.GetAsync(path));
    }

    protected async Task<HttpResponseMessage> GetRawAsync(string path, string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.GetAsync(path);
    }

    protected static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    protected static string Tag(string prefix) => $"{prefix}{Guid.NewGuid():N}".ToUpperInvariant()[..12];

    protected static string ErrCode(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;
    protected static string ErrMessage(JsonElement body) => body.GetProperty("error").GetProperty("message").GetString()!;

    // ---- shared flow seeds -----------------------------------------------------------

    protected async Task<string> SeedPeriodCovering(DateOnly date, string tag)
    {
        var list = await GetAsync("/api/v1/fiscal-periods", Admin);
        var existing = list.GetProperty("data").EnumerateArray().FirstOrDefault(p =>
            DateOnly.Parse(p.GetProperty("startDate").GetString()!) <= date
            && DateOnly.Parse(p.GetProperty("endDate").GetString()!) >= date);
        if (existing.ValueKind == JsonValueKind.Object)
            return existing.GetProperty("id").GetString()!;
        var start = new DateOnly(date.Year, date.Month, 1);
        var res = await PostAsync("/api/v1/fiscal-periods", new
        {
            name = $"{start:yyyy-MM}-{tag}",
            startDate = start,
            endDate = start.AddMonths(1).AddDays(-1),
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    // current-month period, kept OPEN for every posting path (reversals post at today)
    protected Task<string> SeedOpenTodayPeriod(string tag) => SeedPeriodCovering(Today, tag);

    protected async Task<string> SeedWorker(string? nationalId = null, string? managerUserId = null,
        DateOnly? hireDate = null, decimal weeklyHours = 40m)
    {
        var tag = Tag("W");
        var res = await PostAsync("/api/v1/workers", new
        {
            fullName = $"Worker {tag}",
            nationalId = nationalId ?? $"NID-{tag}",
            email = $"{tag.ToLowerInvariant()}@worker.test",
            categoryCode = "STAFF",
            hireDate = hireDate ?? MonthStart(Today).AddMonths(-1),
            weeklyPlannedHours = weeklyHours,
            managerUserId,
        }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    // compensation + Draft→Prehire→Active so the worker can take time
    protected async Task<string> ActivateWorker(string workerId, decimal amount = 10000m, string payBasis = "Monthly")
    {
        var res = await PostAsync($"/api/v1/workers/{workerId}/compensations", new
        {
            amount, currency = "BDT", payBasis, effectiveFrom = MonthStart(Today).AddMonths(-1),
            reason = "initial hire terms",
        }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/workers/{workerId}/transitions",
            new { status = "Prehire", effectiveDate = MonthStart(Today).AddMonths(-1), reason = "onboarded" }, Hr)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/workers/{workerId}/transitions",
            new { status = "Active", effectiveDate = MonthStart(Today).AddMonths(-1), reason = "started" }, Hr)).StatusCode);
        return workerId;
    }

    // each fixture creates its OWN pay group — the run population is group-scoped, so this is
    // what keeps concurrent tests' workers out of each other's runs
    protected async Task<string> SeedPayGroup()
    {
        var code = Tag("PG");
        var res = await PostAsync("/api/v1/pay-groups", new { code, name = $"Group {code}", frequency = "Monthly" }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    protected async Task AssignPayGroup(string workerId, string groupId)
    {
        var res = await PostAsync($"/api/v1/workers/{workerId}/pay-group-memberships",
            new { payGroupId = groupId, effectiveFrom = MonthStart(Today).AddMonths(-1) }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    // catalog rows are tenant-wide and immutable in the pilot — a replay of the same code meets
    // the already-seeded row (409 PAY_CODE_EXISTS), which keeps per-database catalogs deterministic
    protected async Task SeedPayCode(string code, string kind, string basis, decimal ratePct,
        string debit = "6100", string credit = "2300")
    {
        var res = await PostAsync("/api/v1/pay-codes", new
        { code, kind, name = $"{kind} {code}", calcBasis = basis, ratePct, debitAccount = debit, creditAccount = credit }, Fin);
        Assert.True(res.StatusCode == HttpStatusCode.Created || res.StatusCode == HttpStatusCode.Conflict,
            $"pay code {code}: unexpected {(int)res.StatusCode}");
    }

    // manager-entered time is attested at entry (Source=Manager → Approved); requester-entered
    // time lands Pending and needs a time:approve decision. ADMIN plays the manager in fixtures
    // because R-FIN deliberately holds no time:write (R-PEO9 SoD)
    protected async Task<(string entryId, string status)> SeedTime(string workerId, DateOnly workDate,
        decimal hours, decimal ot, string? asUser = null)
    {
        var res = await PostAsync("/api/v1/time-entries",
            new { employmentId = workerId, workDate, hours, overtimeHours = ot, note = (string?)"seed" }, asUser ?? Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var data = (await Json(res)).GetProperty("data");
        return (data.GetProperty("id").GetString()!, data.GetProperty("status").GetString()!);
    }

    // full run: own pay group + approved time + open period + BASIC/OVERTIME/TAX codes
    protected async Task<(string workerId, string runId, string periodId)> SeedCalculatedRun(
        decimal hours = 10m, decimal ot = 0m, decimal hourlyRate = 100m)
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: hourlyRate, payBasis: "Hourly");
        await AssignPayGroup(worker, groupId);
        if (hours > 0 || ot > 0) await SeedTime(worker, Today, hours, ot); // manager-entered, Approved at entry
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        await SeedPayCode("OVERTIME", "Earning", "Hours", 0, credit: "2300");
        await SeedPayCode("TAX-10", "Deduction", "Percent", 10);
        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr)).StatusCode);
        return (worker, runId, periodId);
    }

    protected async Task<JsonElement> ApproveRun(string runId, string token = Fin)
    {
        var res = await PostAsync($"/api/v1/pay-runs/{runId}/approvals", new { reason = (string?)null }, token);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await Json(res);
    }

    protected async Task<JsonElement> PostRun(string runId, string token = Fin, string? idemKey = null)
    {
        var res = await PostAsync($"/api/v1/pay-runs/{runId}/postings", new { }, token, idemKey);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await Json(res);
    }

    protected static JsonElement JournalOf(JsonElement journals, string journalId) =>
        journals.GetProperty("data").EnumerateArray().First(j => j.GetProperty("id").GetString() == journalId);

    protected static JsonElement LineOf(JsonElement journal, string accountCode) =>
        journal.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("accountCode").GetString() == accountCode);
}

// ---- register, lifecycle, compensation, pay-group membership -------------------------

public class PeopleWorkerTests : PeopleTestBase
{
    public PeopleWorkerTests(TestApiFactory factory) : base(factory) { }

    // ---- 1. rehire duplicate check (ERP-PEOPLE-004 edge, R-PEO3 one-step register) ----

    [Fact]
    public async Task Person_Rehire_With_Duplicate_NationalId_Returns_409_PERSON_DUPLICATE()
    {
        var nid = $"nid-{Tag("dup")}";
        var firstId = await SeedWorker(nationalId: nid);

        var res = await PostAsync("/api/v1/workers", new
        {
            fullName = "Rehire Candidate",
            nationalId = $"  {nid.ToUpperInvariant()}  ", // trimmed + case-folded exact match
            categoryCode = "STAFF",
            hireDate = Today,
            weeklyPlannedHours = 40m,
        }, Hr);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("PERSON_DUPLICATE", ErrCode(body));
        Assert.Contains(firstId, ErrMessage(body)); // candidate id surfaced, never auto-merged
    }

    // ---- 2. termination lifecycle (ERP-PEOPLE-007 AC2) -------------------------------

    [Fact]
    public async Task Worker_Termination_Closes_Future_Compensation_And_Blocks_Time_Entries()
    {
        var worker = await ActivateWorker(await SeedWorker());
        var futureFrom = MonthStart(Today).AddMonths(1);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/workers/{worker}/compensations", new
        {
            amount = 20000m, currency = "BDT", payBasis = "Monthly", effectiveFrom = futureFrom,
            reason = "scheduled raise",
        }, Hr)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/workers/{worker}/transitions", new
        { status = "Terminated", effectiveDate = Today, reason = "voluntary resignation" }, Hr)).StatusCode);

        // future-dated terms are closed out by the termination — never deleted
        var comps = await GetAsync($"/api/v1/workers/{worker}/compensations", Hr);
        var future = comps.GetProperty("data").GetProperty("events").EnumerateArray()
            .First(e => DateOnly.Parse(e.GetProperty("effectiveFrom").GetString()!) == futureFrom);
        Assert.Equal(Today, DateOnly.Parse(future.GetProperty("effectiveTo").GetString()!));

        // terminated workers take no new time (posted as a permission-passing user so the
        // service-level 409 WORKER_TERMINATED is what fires)
        var res = await PostAsync("/api/v1/time-entries",
            new { employmentId = worker, workDate = Today, hours = 8m, overtimeHours = 0m }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("WORKER_TERMINATED", ErrCode(await Json(res)));
    }

    // ---- 3. effective-dated compensation (ERP-PEOPLE-024 AC1) ------------------------

    [Fact]
    public async Task Future_Compensation_Not_Applied_Before_Effective_Date()
    {
        var worker = await SeedWorker();
        var thisMonthEnd = MonthStart(Today).AddMonths(1).AddDays(-1);
        var nextMonthEnd = MonthStart(Today).AddMonths(2).AddDays(-1);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/workers/{worker}/compensations", new
        { amount = 10000m, currency = "BDT", payBasis = "Monthly", effectiveFrom = MonthStart(Today), reason = "hire" }, Hr)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/workers/{worker}/compensations", new
        { amount = 20000m, currency = "BDT", payBasis = "Monthly", effectiveFrom = MonthStart(Today).AddMonths(1), reason = "raise" }, Hr)).StatusCode);

        var now = await GetAsync($"/api/v1/workers/{worker}/compensations?asOf={thisMonthEnd:yyyy-MM-dd}", Hr);
        Assert.Equal(10000m, now.GetProperty("data").GetProperty("asOf").GetProperty("amount").GetDecimal());

        var later = await GetAsync($"/api/v1/workers/{worker}/compensations?asOf={nextMonthEnd:yyyy-MM-dd}", Hr);
        Assert.Equal(20000m, later.GetProperty("data").GetProperty("asOf").GetProperty("amount").GetDecimal());
    }

    // ---- 4. one active pay-group membership (ERP-PEOPLE-035 AC2) ---------------------

    [Fact]
    public async Task Pay_Run_Duplicate_Active_Group_Membership_Returns_409()
    {
        var worker = await SeedWorker();
        await AssignPayGroup(worker, "PG-001");

        var res = await PostAsync($"/api/v1/workers/{worker}/pay-group-memberships",
            new { payGroupId = "PG-001", effectiveFrom = Today }, Hr);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("PAY_GROUP_OVERLAP", ErrCode(await Json(res)));
    }

    // ---- 5. Active requires effective compensation (ERP-PEOPLE-007 AC1) -------------

    [Fact]
    public async Task Active_Transition_Without_Compensation_Returns_409()
    {
        var worker = await SeedWorker();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/workers/{worker}/transitions",
            new { status = "Prehire", effectiveDate = Today, reason = "onboarded" }, Hr)).StatusCode);

        var res = await PostAsync($"/api/v1/workers/{worker}/transitions",
            new { status = "Active", effectiveDate = Today, reason = "started" }, Hr);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("WORKER_NO_COMPENSATION", ErrCode(await Json(res)));
    }
}

// ---- time capture + corrections ------------------------------------------------------

public class PeopleTimeTests : PeopleTestBase
{
    public PeopleTimeTests(TestApiFactory factory) : base(factory) { }

    // ---- 6. time capture idempotency (ERP-PEOPLE-026 AC2) ----------------------------

    [Fact]
    public async Task Time_Entry_Idempotent_Replay_Returns_Original_Not_A_Second_Row()
    {
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin));
        var key = $"time-{Guid.NewGuid():N}";

        var res = await PostAsync("/api/v1/time-entries",
            new { employmentId = worker, workDate = Today, hours = 8m, overtimeHours = 1m }, Admin, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var firstId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/time-entries",
            new { employmentId = worker, workDate = Today, hours = 8m, overtimeHours = 1m }, Admin, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(firstId, (await Json(res)).GetProperty("data").GetProperty("id").GetString());

        var rows = await GetAsync($"/api/v1/time-entries?employmentId={worker}", Hr);
        Assert.Single(rows.GetProperty("data").EnumerateArray());
    }

    // ---- 7. corrections keep the trail + separation of duties (ERP-PEOPLE-027) -------

    [Fact]
    public async Task Time_Correction_Preserves_Original_And_Rejects_Self_Approval()
    {
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin));
        var entryId = (await SeedTime(worker, Today, 8m, 0m)).entryId;

        var res = await PostAsync("/api/v1/time-corrections", new
        { timeEntryId = entryId, proposedHours = 6m, proposedOvertimeHours = 0m, reason = "clock drift" }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var correctionId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // the requester cannot approve their own correction (027 separation)
        var self = await PostAsync($"/api/v1/time-corrections/{correctionId}/decisions", new { approve = true }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("TIME_APPROVAL_ERROR", ErrCode(await Json(self)));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/time-corrections/{correctionId}/decisions",
            new { approve = true, decisionReason = "verified against device log" }, Admin)).StatusCode);

        // approval supersedes the payable hours; the correction row keeps previous/proposed/delta
        var rows = await GetAsync($"/api/v1/time-entries?employmentId={worker}", Hr);
        var entry = rows.GetProperty("data").EnumerateArray().First(e => e.GetProperty("id").GetString() == entryId);
        Assert.Equal(6m, entry.GetProperty("hours").GetDecimal());

        var corrections = await GetAsync($"/api/v1/time-corrections?employmentId={worker}", Hr);
        var row = corrections.GetProperty("data").EnumerateArray().First(c => c.GetProperty("id").GetString() == correctionId);
        Assert.Equal("Approved", row.GetProperty("status").GetString());
        Assert.Equal(8m, row.GetProperty("previousHours").GetDecimal());
        Assert.Equal(6m, row.GetProperty("proposedHours").GetDecimal());
    }

    // ---- entry decisions: pending → approved with approver ≠ requester (034 input) ----

    [Fact]
    public async Task Time_Entry_Decision_Flows_Pending_To_Approved_With_Maker_Checker()
    {
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin));
        var (entryId, status) = await SeedTime(worker, Today, 5m, 0m, asUser: Hr); // not the manager → Pending
        Assert.Equal("Pending", status);

        // the submitter cannot approve their own entry
        var self = await PostAsync($"/api/v1/time-entries/{entryId}/decisions", new { approve = true }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("TIME_APPROVAL_ERROR", ErrCode(await Json(self)));

        var res = await PostAsync($"/api/v1/time-entries/{entryId}/decisions", new { approve = true }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Approved", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        // a decided entry cannot be re-decided
        var again = await PostAsync($"/api/v1/time-entries/{entryId}/decisions", new { approve = false }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}

// ---- pay runs: catalog, calculation, validation, approval, payslip scope -------------

public class PeoplePayrollTests : PeopleTestBase
{
    public PeoplePayrollTests(TestApiFactory factory) : base(factory) { }

    // ---- 8. pay code catalog mapping gate (ERP-PEOPLE-037 AC1) -----------------------

    [Fact]
    public async Task Pay_Code_Without_Account_Mapping_Cannot_Activate()
    {
        var code = Tag("BAD");
        var res = await PostAsync("/api/v1/pay-codes", new
        { code, kind = "Deduction", name = "No mapping", calcBasis = "Percent", ratePct = 5m }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrCode(await Json(res)));

        // an unknown account code is a 404, not a silent mapping
        var unknown = await PostAsync("/api/v1/pay-codes", new
        {
            code = Tag("UNK"), kind = "Earning", name = "Bad account", calcBasis = "Hours",
            debitAccount = "6100", creditAccount = "9999",
        }, Fin);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("ACCOUNT_NOT_FOUND", ErrCode(await Json(unknown)));

        // with a required mapping on both sides the code activates
        await SeedPayCode(Tag("OK"), "Earning", "Hours", 0);
    }

    // ---- 9. frozen deterministic calculation (ERP-PEOPLE-038 AC1) --------------------

    [Fact]
    public async Task Calculation_Rerun_With_Frozen_Inputs_Produces_Identical_Lines()
    {
        var (_, runId, _) = await SeedCalculatedRun(hours: 8m, ot: 2m);

        var preview1 = await GetAsync($"/api/v1/pay-runs/{runId}/preview", Fin);
        var lines1 = preview1.GetProperty("data").GetProperty("lines").GetRawText();

        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr)).StatusCode);
        var preview2 = await GetAsync($"/api/v1/pay-runs/{runId}/preview", Fin);
        var lines2 = preview2.GetProperty("data").GetProperty("lines").GetRawText();

        Assert.Equal(lines1, lines2); // byte-equal lines from identical frozen inputs
    }

    // ---- 10. gross-to-net reconciliation (ERP-PEOPLE-039 AC1/AC2) --------------------

    [Fact]
    public async Task Gross_Minus_Deductions_Equals_Net_And_Reconciles_To_Lines()
    {
        var (worker, runId, _) = await SeedCalculatedRun(hours: 8m, ot: 2m, hourlyRate: 100m);

        var run = (await GetAsync("/api/v1/pay-runs", Fin)).GetProperty("data").EnumerateArray()
            .First(r => r.GetProperty("id").GetString() == runId);
        Assert.Equal(1100m, run.GetProperty("grossTotal").GetDecimal());    // 8×100 + 2×150
        Assert.Equal(110m, run.GetProperty("deductionTotal").GetDecimal()); // 10% of gross
        Assert.Equal(990m, run.GetProperty("netTotal").GetDecimal());
        Assert.Equal(run.GetProperty("grossTotal").GetDecimal() - run.GetProperty("deductionTotal").GetDecimal(),
            run.GetProperty("netTotal").GetDecimal());

        var preview = await GetAsync($"/api/v1/pay-runs/{runId}/preview", Fin);
        Assert.True(preview.GetProperty("data").GetProperty("reconciles").GetBoolean());
        var lines = preview.GetProperty("data").GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(1100m, lines.Where(l => l.GetProperty("kind").GetString() == "Earning")
            .Sum(l => l.GetProperty("amount").GetDecimal()));
        Assert.Equal(110m, lines.Where(l => l.GetProperty("kind").GetString() == "Deduction")
            .Sum(l => l.GetProperty("amount").GetDecimal()));
    }

    // ---- 11. run-key idempotency (ERP-PEOPLE-049 AC1) --------------------------------

    [Fact]
    public async Task Pay_Run_Same_Key_Replay_Returns_Same_Run()
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin));
        await AssignPayGroup(worker, groupId);
        await SeedTime(worker, Today, 8m, 0m); // manager-entered, Approved at entry
        await SeedPayCode("BASIC", "Earning", "Hours", 0);

        var key = $"run-{Guid.NewGuid():N}";
        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr, key);
        Assert.Equal(runId, (await Json(res)).GetProperty("data").GetProperty("id").GetString());

        // a different key on the same (tenant, entity, group, period) key finds the SAME run
        res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        Assert.Equal(runId, (await Json(res)).GetProperty("data").GetProperty("id").GetString());
    }

    // ---- 12. preflight validation blocks finalization (ERP-PEOPLE-046 AC1, 034 AC1) --

    [Fact]
    public async Task Run_With_Unapproved_Time_Cannot_Finalize_And_Names_The_Workers()
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var approvedWorker = await ActivateWorker(await SeedWorker(managerUserId: Admin)); // manager-attested
        var pendingWorker = await ActivateWorker(await SeedWorker());                    // self-entered → Pending
        await AssignPayGroup(approvedWorker, groupId);
        await AssignPayGroup(pendingWorker, groupId);
        await SeedTime(approvedWorker, Today, 8m, 0m); // manager-entered, Approved at entry
        var pending = await SeedTime(pendingWorker, Today, 4m, 0m, asUser: Hr);
        Assert.Equal("Pending", pending.status);
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        await SeedPayCode("TAX-10", "Deduction", "Percent", 10);

        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr)).StatusCode);

        var validations = await GetAsync($"/api/v1/pay-runs/{runId}/validations", Fin);
        var blocking = validations.GetProperty("data").GetProperty("blocking").EnumerateArray().ToList();
        var unapproved = blocking.FirstOrDefault(b => b.GetProperty("code").GetString() == "UNAPPROVED_TIME");
        Assert.NotEqual(default, unapproved);
        Assert.Equal(1, unapproved.GetProperty("workerCount").GetInt32());
        Assert.Contains(pendingWorker, unapproved.GetProperty("message").GetString());

        // approval (the finalization gate) is blocked while the validation stands
        var approve = await PostAsync($"/api/v1/pay-runs/{runId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal("PAYROLL_VALIDATION_BLOCKED", ErrCode(await Json(approve)));
    }

    // ---- 13. maker-checker on payroll approval (ERP-PEOPLE-048 AC1) ------------------

    [Fact]
    public async Task Payroll_Approval_By_Calculator_Returns_409()
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin));
        await AssignPayGroup(worker, groupId);
        await SeedTime(worker, Today, 8m, 0m); // manager-entered, Approved at entry
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        await SeedPayCode("TAX-10", "Deduction", "Percent", 10);

        // ADMIN calculates — the admin role spans both sides of the maker-checker split
        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Admin)).StatusCode);

        // HR lacks payroll:approve entirely
        var hrAttempt = await PostAsync($"/api/v1/pay-runs/{runId}/approvals", new { reason = (string?)null }, Hr);
        Assert.Equal(HttpStatusCode.Forbidden, hrAttempt.StatusCode);

        // the calculator cannot approve their own run (048 AC1)
        var self = await PostAsync($"/api/v1/pay-runs/{runId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("PAYROLL_APPROVAL_ERROR", ErrCode(await Json(self)));

        // finance approves; a second approval attempt is rejected — no double state change
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/pay-runs/{runId}/approvals", new { reason = (string?)null }, Fin)).StatusCode);
        var again = await PostAsync($"/api/v1/pay-runs/{runId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // ---- 14. period guard on the run's PayDate (R-PEO11) -----------------------------

    [Fact]
    public async Task Payroll_Posting_Into_Closed_Period_Returns_409_PERIOD_LOCKED()
    {
        var payDate = MonthStart(Today).AddMonths(2).AddDays(10);
        var periodId = await SeedPeriodCovering(payDate, Tag("c"));
        var workDate = payDate.AddDays(-1);
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
        await AssignPayGroup(worker, groupId);
        await SeedTime(worker, workDate, 8m, 0m); // manager-entered, Approved at entry
        await SeedPayCode("BASIC", "Earning", "Hours", 0);

        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate }, Hr);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr)).StatusCode);
        await ApproveRun(runId);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/fiscal-periods/{periodId}/close",
            new { reason = "month-end lock before payroll posting" }, Admin)).StatusCode);

        var post = await PostAsync($"/api/v1/pay-runs/{runId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        Assert.Equal("PERIOD_LOCKED", ErrCode(await Json(post)));
    }

    // ---- 15. payslip scope filter (ERP-PEOPLE-051 AC2) -------------------------------

    [Fact]
    public async Task Payslip_Of_Another_Worker_Returns_404()
    {
        var (worker, runId, _) = await SeedCalculatedRun(hours: 8m);
        await ApproveRun(runId);

        var slip = await GetAsync($"/api/v1/pay-runs/{runId}/payslips/{worker}", Fin);
        Assert.Equal(8m, slip.GetProperty("data").GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("payCode").GetString() == "BASIC")
            .Sum(l => l.GetProperty("hours").GetDecimal()));
        Assert.Equal(800m, slip.GetProperty("data").GetProperty("gross").GetDecimal());

        // a worker outside the run's approved population has no slip in it
        var outsider = await SeedWorker();
        var other = await GetRawAsync($"/api/v1/pay-runs/{runId}/payslips/{outsider}", Fin);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal("PAYSLIP_NOT_FOUND", ErrCode(await Json(other)));
    }

    // ---- 16b. register list, catalog/group lists and pay history (read surfaces) -----

    [Fact]
    public async Task Register_Pay_History_And_Catalog_Lists_Are_Scoped()
    {
        var (worker, runId, _) = await SeedCalculatedRun(hours: 8m);
        await ApproveRun(runId);
        await PostRun(runId);

        // register list filters by status and exposes the configured category code (006 AC2)
        var register = await GetAsync("/api/v1/workers?status=Active", Hr);
        var row = register.GetProperty("data").EnumerateArray()
            .First(w => w.GetProperty("id").GetString() == worker);
        Assert.Equal("Active", row.GetProperty("status").GetString());
        Assert.Equal("STAFF", row.GetProperty("categoryCode").GetString());
        Assert.Equal(worker, row.GetProperty("workerNo").GetString()); // WKR doc number IS the worker number

        // 056: posted results reproduced as issued
        var history = await GetAsync($"/api/v1/workers/{worker}/pay-history", Fin);
        var entry = history.GetProperty("data").EnumerateArray().First(r => r.GetProperty("runId").GetString() == runId);
        Assert.Equal("original", entry.GetProperty("kind").GetString());
        Assert.Equal(800m, entry.GetProperty("gross").GetDecimal());
        Assert.Equal(720m, entry.GetProperty("net").GetDecimal()); // TAX-10, the only deduction code in this DB

        // a worker with no posted results has an empty history
        var outsider = await SeedWorker();
        var empty = await GetAsync($"/api/v1/workers/{outsider}/pay-history", Fin);
        Assert.Empty(empty.GetProperty("data").EnumerateArray());

        // catalog + group lists are scope-filtered reads
        var codes = await GetAsync("/api/v1/pay-codes", Fin);
        Assert.Contains(codes.GetProperty("data").EnumerateArray(),
            c => c.GetProperty("code").GetString() == "BASIC");
        var groups = await GetAsync("/api/v1/pay-groups", Hr);
        Assert.NotEmpty(groups.GetProperty("data").EnumerateArray());
    }
}

// ---- golden posting + reversal: the only deduction code in this database is FLAT-100 --

public class PeoplePayrollPostingTests : PeopleTestBase
{
    public PeoplePayrollPostingTests(TestApiFactory factory) : base(factory) { }

    // ---- 16. golden posting (ERP-PEOPLE-053 AC1 + 049 AC2) ---------------------------

    [Fact]
    public async Task Payroll_Posting_Golden_Balanced_Dr_6100_Cr_2310_Cr_2300()
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
        await AssignPayGroup(worker, groupId);
        await SeedTime(worker, Today, 10m, 0m); // manager-entered, Approved at entry
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        await SeedPayCode("FLAT-100", "Deduction", "Amount", 100);

        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr)).StatusCode);
        await ApproveRun(runId);

        var key = $"post-{Guid.NewGuid():N}";
        var posted = await PostRun(runId, idemKey: key);
        var journalId = posted.GetProperty("data").GetProperty("journal").GetProperty("id").GetString()!;

        var journals = await GetAsync("/api/v1/journals", Fin);
        var journal = JournalOf(journals, journalId);
        Assert.Equal(1000.00m, LineOf(journal, "6100").GetProperty("debit").GetDecimal());
        Assert.Equal(100.00m, LineOf(journal, "2310").GetProperty("credit").GetDecimal());
        Assert.Equal(900.00m, LineOf(journal, "2300").GetProperty("credit").GetDecimal());
        Assert.Equal(1000.00m, journal.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("debit").GetDecimal()));
        Assert.Equal(1000.00m, journal.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("credit").GetDecimal()));

        // replay with the SAME key returns the SAME journal — never a second posting
        var replay = await PostRun(runId, idemKey: key);
        Assert.Equal(journalId, replay.GetProperty("data").GetProperty("journal").GetProperty("id").GetString());

        // a fresh retry (new key) hits the state guard: zero additional journals
        var again = await PostAsync($"/api/v1/pay-runs/{runId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("PAYRUN_ALREADY_POSTED", ErrCode(await Json(again)));
        journals = await GetAsync("/api/v1/journals", Fin);
        Assert.Equal(1, journals.GetProperty("data").EnumerateArray()
            .Count(j => j.GetProperty("memo").GetString()!.Contains(runId)));

        // outbox v1 carries the FINANCE handoff payload (PlatformController: /api/v1/outbox)
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        var evt = outbox.GetProperty("data").EnumerateArray().First(e =>
            e.GetProperty("type").GetString() == "people.payrun.posted"
            && e.GetProperty("payloadJson").GetString()!.Contains(runId));
        Assert.Equal(1, evt.GetProperty("version").GetInt32());
        Assert.Contains(runId, evt.GetProperty("payloadJson").GetString());
    }

    // ---- 17. reversal posts compensating journal under a NEW run id (055 AC1, 049) ---

    [Fact]
    public async Task Reversal_Posts_Compensating_Journal_And_Correction_Uses_A_New_Run_Id()
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
        await AssignPayGroup(worker, groupId);
        await SeedTime(worker, Today, 10m, 0m); // manager-entered, Approved at entry
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        await SeedPayCode("FLAT-100", "Deduction", "Amount", 100);

        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr)).StatusCode);
        await ApproveRun(runId);
        var posted = await PostRun(runId);
        var originalJournalId = posted.GetProperty("data").GetProperty("journal").GetProperty("id").GetString()!;

        var missingReason = await PostAsync($"/api/v1/pay-runs/{runId}/reversals",
            new { reason = "", fundsReleased = true }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var revRes = await PostAsync($"/api/v1/pay-runs/{runId}/reversals",
            new { reason = "duplicate bonus included", fundsReleased = false }, Fin);
        Assert.Equal(HttpStatusCode.Created, revRes.StatusCode);
        var reversal = (await Json(revRes)).GetProperty("data");

        // the correction run is a NEW identity linked back to the original (049 exception)
        var reversalRunId = reversal.GetProperty("run").GetProperty("id").GetString()!;
        Assert.NotEqual(runId, reversalRunId);
        Assert.Equal(runId, reversal.GetProperty("run").GetProperty("reversalOfRunId").GetString());
        Assert.Equal("Reversed", reversal.GetProperty("run").GetProperty("status").GetString());
        Assert.False(reversal.GetProperty("run").GetProperty("fundsReleased").GetBoolean());

        var journals = await GetAsync("/api/v1/journals", Fin);
        var reversalJournal = JournalOf(journals, reversal.GetProperty("journal").GetProperty("id").GetString()!);
        Assert.Equal(900.00m, LineOf(reversalJournal, "2300").GetProperty("debit").GetDecimal());
        Assert.Equal(100.00m, LineOf(reversalJournal, "2310").GetProperty("debit").GetDecimal());
        Assert.Equal(1000.00m, LineOf(reversalJournal, "6100").GetProperty("credit").GetDecimal());
        Assert.Equal(originalJournalId, reversalJournal.GetProperty("reversalOfId").GetString());

        // the original run is marked Reversed; reversing it again is rejected
        var runs = await GetAsync("/api/v1/pay-runs", Fin);
        var original = runs.GetProperty("data").EnumerateArray().First(r => r.GetProperty("id").GetString() == runId);
        Assert.Equal("Reversed", original.GetProperty("status").GetString());
        var again = await PostAsync($"/api/v1/pay-runs/{runId}/reversals",
            new { reason = "double click", fundsReleased = false }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // ---- 18. late corrections against a posted entry wait (027 AC2 trim, Q10) --------

    [Fact]
    public async Task Correction_Against_A_Posted_Entry_Waits_For_The_Next_Period()
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
        await AssignPayGroup(worker, groupId);
        var (entryId, _) = await SeedTime(worker, Today, 10m, 0m);
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        await SeedPayCode("FLAT-100", "Deduction", "Amount", 100);

        var res = await PostAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        var runId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr)).StatusCode);
        await ApproveRun(runId);
        await PostRun(runId);

        var correction = await PostAsync("/api/v1/time-corrections", new
        { timeEntryId = entryId, proposedHours = 6m, proposedOvertimeHours = 0m, reason = "late drift" }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
        Assert.Equal("TIME_PERIOD_POSTED", ErrCode(await Json(correction)));
    }
}
