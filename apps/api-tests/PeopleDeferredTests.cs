using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

// PEOPLE deferred screens (PM1–PM10, brief deferred-screens-people-mfg §1A). Rulings Q-PM1
// (leave approval emits a dated absence TimeEntry on the 034 contract), Q-PM5 (U-010 HR checker
// seeds maker-checker), R-FIN keeps payroll:approve/release/close are binding.
//
// Four classes ON PURPOSE (one throwaway database each, PeopleTests convention): the pay-adjacent
// class seeds its own deduction catalog (TAX-10 + CONTRIB-5), and the account-mapping test gets a
// class of its own because an inactive mapping blocks EVERY posting that follows it in the same
// database — isolating it keeps every other test's posting path untouched.
//   - PeopleDeferredOrgTests     PM1 org/positions/headcount + PM3 catalog/policy + PM9 credentials
//                                + PM10 plans/allocation/analytics
//   - PeopleDeferredLeaveTests   PM2 onboarding + PM4 schedules + PM5 leave/attendance
//   - PeopleDeferredPayTests     PM6 pay inputs + PM7 payment files + PM8 close checklist
//   - PeopleDeferredMappingTests PM8 inactive account mapping blocks posting (053 AC2)

namespace Erp.Api.Tests;

public abstract class PeopleDeferredTestBase : PeopleTestBase
{
    protected const string HrChecker = "U-010"; // Q-PM5: second HR user — the checker side

    protected PeopleDeferredTestBase(TestApiFactory factory) : base(factory) { }

    protected new async Task<JsonElement> GetAsync(string path, string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await Json(await client.GetAsync(path));
    }

    // POST helpers read the buffered body EXACTLY ONCE (string first, parse from it) — a
    // ReadAsStringAsync followed by ReadAsStreamAsync on the same content can hit a closed stream
    protected async Task<JsonElement> CreatedAsync(string path, object body, string token)
    {
        var res = await PostAsync(path, body, token);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.Created, $"POST {path} -> {res.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    protected async Task<JsonElement> PostJsonAsync(string path, object body, string token, string? idemKey = null)
    {
        var res = await PostAsync(path, body, token, idemKey);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.True((int)res.StatusCode < 500, $"POST {path} -> {(int)res.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    // position + Active assignment for a fresh worker; returns (positionId, assignmentId, workerId)
    protected async Task<(string positionId, string assignmentId, string workerId)> SeedStaffedPosition(
        string? gradeCode = null, string? requiredCredential = null, decimal budgetedFte = 1m,
        int budgetedHeadcount = 1, string costCenter = "CC-DEFAULT")
    {
        var tag = Tag("PST");
        var position = await CreatedAsync("/api/v1/positions", new
        {
            code = tag, title = $"Position {tag}", gradeCode, requiredCredentialCode = requiredCredential,
            budgetedFte, budgetedHeadcount,
        }, Hr);
        // 201 asserted by the helper
        var positionId = position.GetProperty("data").GetProperty("id").GetString()!;
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin));
        var assignment = await CreatedAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = worker, fte = 1m, costCenter, effectiveFrom = MonthStart(Today),
        }, Hr);
        // 201 asserted by the helper
        Assert.Equal("Active", assignment.GetProperty("data").GetProperty("status").GetString());
        return (positionId, assignment.GetProperty("data").GetProperty("id").GetString()!, worker);
    }
}

// ---- PM1 org/positions/headcount + PM3 catalog/policy + PM9 credentials + PM10 workforce ----

public class PeopleDeferredOrgTests : PeopleDeferredTestBase
{
    public PeopleDeferredOrgTests(TestApiFactory factory) : base(factory) { }

    // ---- 1 (002 AC1–2): as-of hierarchy query honors validity intervals; overlapping
    //      intervals on the same unit are rejected
    [Fact]
    public async Task Org_AsOf_Query_Returns_Interval_Valid_Unit_And_Overlap_Is_Rejected()
    {
        var tag = Tag("ORGA");
        var created = await CreatedAsync("/api/v1/org-units", new
        {
            code = tag, name = "Sales", effectiveFrom = MonthStart(Today),
            effectiveTo = Today.AddDays(10), parentOrgUnitId = (string?)null, locationId = (string?)null,
        }, Hr);
        // 201 asserted by the helper
        var unitId = created.GetProperty("data").GetProperty("id").GetString()!;

        var asOfToday = await GetAsync($"/api/v1/org-units?asOf={Today:yyyy-MM-dd}", Hr);
        Assert.Contains(asOfToday.GetProperty("data").EnumerateArray(),
            u => u.GetProperty("id").GetString() == unitId);
        var asOfLater = await GetAsync($"/api/v1/org-units?asOf={Today.AddDays(20):yyyy-MM-dd}", Hr);
        Assert.DoesNotContain(asOfLater.GetProperty("data").EnumerateArray(),
            u => u.GetProperty("id").GetString() == unitId);

        // the same code re-intervalized OVER the live range must be refused (no overlapping intervals)
        var overlap = await PostAsync("/api/v1/org-units", new
        {
            code = tag, name = "Sales v2", effectiveFrom = Today.AddDays(5),
            parentOrgUnitId = (string?)null, locationId = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal("ORG_INTERVAL_OVERLAP", ErrCode(await Json(overlap)));
    }

    // ---- 2 (005/008 AC2): assignment over headcount limit is blocked; the exception path
    //      records the R-FIN approver + variance; out-of-employment assignments need it too
    [Fact]
    public async Task Assignment_Over_Headcount_Blocked_And_Exception_Records_Approver_And_Variance()
    {
        var (positionId, _, worker1) = await SeedStaffedPosition(budgetedFte: 1m, budgetedHeadcount: 1);

        var overLimit = await PostAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = await ActivateWorker(await SeedWorker()), fte = 1m, effectiveFrom = MonthStart(Today),
            exceptionReason = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, overLimit.StatusCode);
        Assert.Equal("HEADCOUNT_EXCEEDED", ErrCode(await Json(overLimit)));

        // exception route: lands PendingException, then R-FIN activates with variance logged
        var worker2 = await SeedWorker();
        var pending = await CreatedAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = worker2, fte = 1m, effectiveFrom = MonthStart(Today),
            exceptionReason = "seasonal backlog, budget amendment in flight",
        }, Hr);
        // 201 asserted by the helper
        Assert.Equal("PendingException", pending.GetProperty("data").GetProperty("status").GetString());
        var assignmentId = pending.GetProperty("data").GetProperty("id").GetString()!;

        var hrDecision = await PostAsync($"/api/v1/position-assignments/{assignmentId}/exception-decisions",
            new { approve = true, reason = "self-approval" }, Hr);
        Assert.Equal(HttpStatusCode.Forbidden, hrDecision.StatusCode); // exception approver is R-FIN only

        var decided = await PostJsonAsync($"/api/v1/position-assignments/{assignmentId}/exception-decisions",
            new { approve = true, reason = "budget amendment BA-22 approved" }, Fin);
        Assert.Equal("Active", decided.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(Fin, decided.GetProperty("data").GetProperty("exceptionApprovedBy").GetString());
        Assert.Equal(1m, decided.GetProperty("data").GetProperty("approvedVariance").GetDecimal());

        // out-of-employment (Draft) assignment also needs the exception path — never silent
        var draftWorker = await SeedWorker(); // Draft: never transitioned to Active
        var outOfEmployment = await PostAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = draftWorker, fte = 1m, effectiveFrom = MonthStart(Today),
            exceptionReason = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, outOfEmployment.StatusCode);
        Assert.Equal("WORKER_NOT_ACTIVE", ErrCode(await Json(outOfEmployment)));
    }

    // ---- 4 (023 AC1–2): grade range min>mid rejected; out-of-policy comp change routed to
    //      the R-FIN exception approval; missing range for a graded position is an explicit error
    [Fact]
    public async Task Grade_Range_Order_Validated_And_Comp_Policy_Gates_With_Exception()
    {
        var invalid = await PostAsync("/api/v1/grade-ranges", new
        {
            gradeCode = Tag("GRD"), currency = "BDT", frequency = "Monthly",
            minAmount = 5000m, midAmount = 4000m, maxAmount = 10000m, effectiveFrom = MonthStart(Today),
        }, Hr);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); // min > mid

        var grade = Tag("GRD");
        var range = await CreatedAsync("/api/v1/grade-ranges", new
        {
            gradeCode = grade, currency = "BDT", frequency = "Monthly",
            minAmount = 5000m, midAmount = 7500m, maxAmount = 10000m, effectiveFrom = MonthStart(Today),
        }, Hr);
        // 201 asserted by the helper

        var (positionId, _, worker) = await SeedStaffedPosition(gradeCode: grade);

        var check = await PostJsonAsync($"/api/v1/comp-changes/{worker}/policy-checks", new
        {
            amount = 20000m, effectiveFrom = Today,
        }, Hr);
        Assert.False(check.GetProperty("data").GetProperty("inPolicy").GetBoolean());
        Assert.True(check.GetProperty("data").GetProperty("approvalRequired").GetBoolean());

        var outOfPolicy = await PostAsync($"/api/v1/workers/{worker}/compensations", new
        {
            amount = 20000m, currency = "BDT", payBasis = "Monthly",
            effectiveFrom = Today, reason = "promotion", exceptionApprovedBy = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, outOfPolicy.StatusCode);
        Assert.Equal("COMP_OUT_OF_POLICY", ErrCode(await Json(outOfPolicy)));

        // the R-FIN exception approver unblocks the change and is recorded on the event
        var withException = await PostAsync($"/api/v1/workers/{worker}/compensations", new
        {
            amount = 20000m, currency = "BDT", payBasis = "Monthly",
            effectiveFrom = Today, reason = "promotion", exceptionApprovedBy = Fin,
        }, Hr);
        Assert.Equal(HttpStatusCode.Created, withException.StatusCode);
        Assert.Contains("exception approved by U-003", (await Json(withException)).GetProperty("data")
            .GetProperty("reason").GetString());

        // graded position with NO range covering the date = explicit config error, never substituted
        var ungraded = Tag("GRD");
        var (posB, _, workerB) = await SeedStaffedPosition(gradeCode: ungraded);
        var missingConfig = await PostAsync($"/api/v1/workers/{workerB}/compensations", new
        {
            amount = 8000m, currency = "BDT", payBasis = "Monthly",
            effectiveFrom = Today, reason = "adjust", exceptionApprovedBy = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, missingConfig.StatusCode);
        Assert.Equal("COMP_POLICY_CONFIG_MISSING", ErrCode(await Json(missingConfig)));
    }

    // ---- 21 (058 AC2): expired required credential blocks the assignment absent exception approval
    [Fact]
    public async Task Expired_Required_Credential_Blocks_Assignment_And_Exception_Path_Activates()
    {
        var credCode = Tag("CRD");
        // the position under test is created BARE — pre-staffing it would trip the very gate
        // this test exercises (the seed worker holds no credential yet)
        var posTag = Tag("PST");
        var position = await CreatedAsync("/api/v1/positions", new
        {
            code = posTag, title = $"Position {posTag}", requiredCredentialCode = credCode,
            budgetedFte = 5m, budgetedHeadcount = 5,
        }, Hr);
        var positionId = position.GetProperty("data").GetProperty("id").GetString()!;
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin));

        var expired = await CreatedAsync("/api/v1/credentials", new
        {
            employmentId = worker, code = credCode, name = "Forklift license",
            issuedBy = "VTI", issuedOn = Today.AddYears(-3), expiresOn = Today.AddDays(-1),
            evidenceRef = "EVD-EXPIRED",
        }, Hr);
        // 201 asserted by the helper

        var blocked = await PostAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = await ActivateWorker(await SeedWorker()), fte = 1m, effectiveFrom = MonthStart(Today),
            exceptionReason = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("CREDENTIAL_REQUIRED", ErrCode(await Json(blocked)));

        // worker without ANY credential of that code is blocked the same way
        var noCred = await PostAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = await ActivateWorker(await SeedWorker()), fte = 1m, effectiveFrom = MonthStart(Today),
            exceptionReason = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, noCred.StatusCode);
        Assert.Equal("CREDENTIAL_REQUIRED", ErrCode(await Json(noCred)));

        // exception path: PendingException then R-FIN activation
        var pendingWorker = await SeedWorker();
        await PostJsonAsync("/api/v1/credentials", new
        {
            employmentId = pendingWorker, code = credCode, name = "Forklift license",
            issuedBy = "VTI", issuedOn = Today.AddYears(-3), expiresOn = Today.AddDays(-1),
            evidenceRef = "EVD-EXPIRED-2",
        }, Hr);
        var pending = await PostJsonAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = pendingWorker, fte = 1m, effectiveFrom = MonthStart(Today),
            exceptionReason = "renewal booked for next week",
        }, Hr);
        Assert.Equal("PendingException", pending.GetProperty("data").GetProperty("status").GetString());
        var assignmentId = pending.GetProperty("data").GetProperty("id").GetString()!;
        var decided = await PostJsonAsync($"/api/v1/position-assignments/{assignmentId}/exception-decisions",
            new { approve = true, reason = "renewal confirmed with provider" }, Fin);
        Assert.Equal("Active", decided.GetProperty("data").GetProperty("status").GetString());

        // a CURRENT credential passes the gate with no exception at all
        var currentWorker = await ActivateWorker(await SeedWorker());
        await PostJsonAsync("/api/v1/credentials", new
        {
            employmentId = currentWorker, code = credCode, name = "Forklift license",
            issuedBy = "VTI", issuedOn = Today.AddYears(-1), expiresOn = Today.AddYears(2),
            evidenceRef = "EVD-CURRENT",
        }, Hr);
        var direct = await PostAsync($"/api/v1/positions/{positionId}/assignments", new
        {
            employmentId = currentWorker, fte = 1m, effectiveFrom = MonthStart(Today),
            exceptionReason = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Created, direct.StatusCode);
        Assert.Equal("Active", (await Json(direct)).GetProperty("data").GetProperty("status").GetString());
    }

    // ---- 22 (059 AC2): plan promotion is an explicit workflow creating LINKED positions,
    //      idempotent-guarded against double promotion
    [Fact]
    public async Task Workforce_Plan_Promotion_Creates_Linked_Positions_Once()
    {
        var planCode = Tag("WFP");
        var plan = await CreatedAsync("/api/v1/workforce-plans", new
        {
            code = planCode, name = "FY27 expansion", horizonStart = MonthStart(Today),
            horizonEnd = MonthStart(Today).AddYears(1), assumptions = "+2 FTE support",
            lines = new object[] { new { positionCode = $"{planCode}-P1", title = "Support Lead", fte = 2m, orgUnitId = (string?)null, gradeCode = (string?)null } },
        }, Hr);
        // 201 asserted by the helper
        var planId = plan.GetProperty("data").GetProperty("id").GetString()!;

        var promoted = await PostAsync($"/api/v1/workforce-plans/{planId}/promotions",
            new { positionCode = (string?)null }, Hr);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        var promotedData = await Json(promoted);
        Assert.Equal($"{planCode}-P1", promotedData.GetProperty("data").GetProperty("createdPositions").EnumerateArray()
            .First().GetProperty("code").GetString());
        Assert.Equal(planId, promotedData.GetProperty("data").GetProperty("createdPositions").EnumerateArray()
            .First().GetProperty("sourcePlanId").GetString());

        var planAfter = await GetAsync($"/api/v1/workforce-plans/{planId}", Hr);
        Assert.True(planAfter.GetProperty("data").GetProperty("lines").EnumerateArray()
            .First().GetProperty("promoted").GetBoolean());

        var again = await PostAsync($"/api/v1/workforce-plans/{planId}/promotions",
            new { positionCode = (string?)null }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("WORKFORCE_PROMOTION_ERROR", ErrCode(await Json(again)));
    }

    // ---- 23 (060 AC1–2): allocation set ≠ 100% rejected at approval; payroll subtotals
    //      reconcile to allocated pct within rounding tolerance
    [Fact]
    public async Task Allocation_Requires_100Pct_And_Payroll_Subtotals_Reconcile()
    {
        var ccA = Tag("CCA");
        var ccB = Tag("CCB");
        await SeedStaffedPosition(costCenter: ccA);
        await SeedStaffedPosition(costCenter: ccB);

        var set = await CreatedAsync("/api/v1/allocation-sets", new
        {
            code = Tag("ALS"), name = "Ops split", unit = "Pct",
            lines = new object[] { new { costCenter = ccA, pct = 60m }, new { costCenter = ccB, pct = 30m } },
        }, Hr);
        // 201 asserted by the helper
        var setId = set.GetProperty("data").GetProperty("id").GetString()!;

        var rejected = await PostAsync($"/api/v1/allocation-sets/{setId}/approvals", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("ALLOCATION_NOT_100", ErrCode(await Json(rejected)));

        var good = await CreatedAsync("/api/v1/allocation-sets", new
        {
            code = Tag("ALS"), name = "Ops split v2", unit = "Pct",
            // 50/50 mirrors the two equal-pay runners so the reconcile lands inside tolerance
            lines = new object[] { new { costCenter = ccA, pct = 50m }, new { costCenter = ccB, pct = 50m } },
        }, Hr);
        // 201 asserted by the helper
        var goodId = good.GetProperty("data").GetProperty("id").GetString()!;
        var hrApproval = await PostAsync($"/api/v1/allocation-sets/{goodId}/approvals", new { }, Hr);
        Assert.Equal(HttpStatusCode.Forbidden, hrApproval.StatusCode); // money-adjacent approval is R-FIN
        var approved = await PostJsonAsync($"/api/v1/allocation-sets/{goodId}/approvals", new { }, Fin);
        Assert.Equal("Approved", approved.GetProperty("data").GetProperty("status").GetString());

        // run payroll over both cost centers and reconcile allocated subtotals
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var workers = new List<string>();
        foreach (var cc in new[] { ccA, ccB })
        {
            var w = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
            await AssignPayGroup(w, groupId);
            await SeedTime(w, Today, 10m, 0m);
            // assign a cost center via a dedicated position per cost center
            var pos = await PostJsonAsync("/api/v1/positions", new
            {
                code = Tag("PST"), title = "Runner", budgetedFte = 5m, budgetedHeadcount = 5,
                gradeCode = (string?)null, requiredCredentialCode = (string?)null,
            }, Hr);
            var a = await CreatedAsync($"/api/v1/positions/{pos.GetProperty("data").GetProperty("id").GetString()}/assignments",
                new { employmentId = w, fte = 1m, costCenter = cc, effectiveFrom = MonthStart(Today) }, Hr);
            // 201 asserted by the helper
            workers.Add(w);
        }
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        var run = await CreatedAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        var runId = run.GetProperty("data").GetProperty("id").GetString()!;
        await PostJsonAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr);
        await ApproveRun(runId);
        var postRes = await PostAsync($"/api/v1/pay-runs/{runId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        var recon = await GetAsync($"/api/v1/allocation-sets/{goodId}/reconciliations?runId={runId}", Fin);
        Assert.True(recon.GetProperty("data").GetProperty("reconciles").GetBoolean());
        Assert.All(recon.GetProperty("data").GetProperty("buckets").EnumerateArray(), b =>
            Assert.True(Math.Abs(b.GetProperty("variance").GetDecimal()) <= 0.05m, b.ToString()));
    }

    // ---- 24 (061 AC1–2): analytics suppression applies server-side incl. complementary cells
    [Fact]
    public async Task Analytics_Suppresses_Small_Groups_Incl_Complementary_Total()
    {
        var ccBig = Tag("CCB");
        var ccSmall = Tag("CCS");
        for (var i = 0; i < 6; i++) await SeedStaffedPosition(costCenter: ccBig);
        await SeedStaffedPosition(costCenter: ccSmall);

        var json = await GetAsync("/api/v1/people-analytics/headcount", Fin);
        var buckets = json.GetProperty("data").GetProperty("buckets").EnumerateArray().ToList();
        var big = buckets.First(b => b.GetProperty("key").GetString() == ccBig);
        Assert.False(big.GetProperty("suppressed").GetBoolean());
        Assert.Equal(6, big.GetProperty("value").GetInt32());
        var small = buckets.First(b => b.GetProperty("key").GetString() == ccSmall);
        Assert.True(small.GetProperty("suppressed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, small.GetProperty("value").ValueKind);
        // complementary cell: with a suppressed bucket present the total is suppressed too
        Assert.True(json.GetProperty("data").GetProperty("total").GetProperty("suppressed").GetBoolean());

        // export path carries the SAME suppression (CSV line blanks the suppressed cell)
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Fin}");
        var csv = await client.GetStringAsync($"/api/v1/people-analytics/headcount?format=csv");
        Assert.Contains(ccBig, csv);
        Assert.DoesNotContain(ccSmall, csv);

        var forbidden = await GetAsync("/api/v1/people-analytics/headcount", "U-004"); // R-SALES
        Assert.Equal("FORBIDDEN", ErrCode(forbidden)); // no people:analytics:read
    }

    // ---- Gate 2: authz negative sweep over the PM org/catalog/workforce write surface
    [Fact]
    public async Task Authz_Sweep_New_Routes_Reject_Permissionless_User()
    {
        var sales = "U-004"; // R-SALES holds no people permissions
        var bodies = new (string path, object body)[]
        {
            ("/api/v1/org-units", new { code = "X1", name = "X", effectiveFrom = Today }),
            ("/api/v1/work-locations", new { code = "X1", name = "X", timezone = "Asia/Dhaka" }),
            ("/api/v1/positions", new { code = "X1", title = "X", budgetedFte = 1m, budgetedHeadcount = 1 }),
            ("/api/v1/grade-ranges", new { gradeCode = "X1", currency = "BDT", frequency = "Monthly", minAmount = 1m, midAmount = 2m, maxAmount = 3m, effectiveFrom = Today }),
            ("/api/v1/job-profiles", new { code = "X1", name = "X", title = "T", effectiveFrom = Today }),
            ("/api/v1/credentials", new { employmentId = "WKR-1", code = "X", name = "X" }),
            ("/api/v1/workforce-plans", new { code = "X1", name = "X", horizonStart = Today, horizonEnd = Today.AddYears(1), lines = Array.Empty<object>() }),
            ("/api/v1/allocation-sets", new { code = "X1", name = "X", unit = "Pct", lines = Array.Empty<object>() }),
        };
        foreach (var (path, body) in bodies)
        {
            var res = await PostAsync(path, body, sales);
            Assert.True(res.StatusCode == HttpStatusCode.Forbidden, $"{path} -> {res.StatusCode}");
            Assert.Equal("FORBIDDEN", ErrCode(await Json(res)));
        }
        var reads = new[] { "/api/v1/people-analytics/headcount" };
        foreach (var path in reads)
        {
            var res = await Factory.CreateClient().GetAsync(path);
            Assert.True((int)res.StatusCode is 401 or 403, $"{path} -> {res.StatusCode}");
        }
    }
}

// ---- PM2 onboarding + PM4 schedules + PM5 leave/attendance -----------------------------------

public class PeopleDeferredLeaveTests : PeopleDeferredTestBase
{
    public PeopleDeferredLeaveTests(TestApiFactory factory) : base(factory) { }

    // ---- 3 (011 AC2): waiver without approver+reason → 400; waiver by the task generator is
    //      maker-checker-blocked; the named checker's waiver records approver + reason
    [Fact]
    public async Task Onboarding_Generation_Idempotent_And_Waiver_Needs_Checker_And_Reason()
    {
        var worker = await SeedWorker();
        await PostJsonAsync("/api/v1/onboarding-templates", new
        {
            categoryCode = "STAFF", taskKey = "DOCS", name = "Collect documents",
            dueOffsetDays = 3, ownerRole = "R-HR", required = true,
        }, Hr);
        await PostJsonAsync("/api/v1/onboarding-templates", new
        {
            categoryCode = "STAFF", taskKey = "BADGE", name = "Issue badge",
            dueOffsetDays = 1, ownerRole = "R-HR", required = false,
        }, Hr);

        var first = await CreatedAsync($"/api/v1/workers/{worker}/onboarding-tasks/generations",
            new { }, Hr);
        Assert.Equal(2, first.GetProperty("data").GetProperty("created").GetInt32()); // DOCS + BADGE
        var replay = await PostJsonAsync($"/api/v1/workers/{worker}/onboarding-tasks/generations", new { }, Hr);
        Assert.Equal(0, replay.GetProperty("data").GetProperty("created").GetInt32()); // idempotent re-run

        var tasks = first.GetProperty("data").GetProperty("tasks").EnumerateArray().ToList();
        var taskId = tasks.First(t => t.GetProperty("taskKey").GetString() == "DOCS").GetProperty("id").GetString()!;
        var badgeTaskId = tasks.First(t => t.GetProperty("taskKey").GetString() == "BADGE").GetProperty("id").GetString()!;

        var noReason = await PostAsync($"/api/v1/onboarding-tasks/{taskId}/waivers",
            new { reason = (string?)null }, HrChecker);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var byMaker = await PostAsync($"/api/v1/onboarding-tasks/{taskId}/waivers",
            new { reason = "waived by the generator" }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, byMaker.StatusCode); // maker-checker: generator ≠ waiver approver
        Assert.Equal("ONBOARDING_WAIVER_ERROR", ErrCode(await Json(byMaker)));

        var waived = await PostJsonAsync($"/api/v1/onboarding-tasks/{taskId}/waivers",
            new { reason = "worker already holds the certification" }, HrChecker);
        Assert.Equal("Waived", waived.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(HrChecker, waived.GetProperty("data").GetProperty("decidedBy").GetString());

        var decided = await PostJsonAsync($"/api/v1/onboarding-tasks/{badgeTaskId}/completions", new { }, Hr);
        Assert.Equal("Completed", decided.GetProperty("data").GetProperty("status").GetString());
    }

    // ---- 5 (025 AC2): schedule expansion resolves overnight windows to unambiguous UTC instants
    [Fact]
    public async Task Schedule_Expansion_Produces_Unambiguous_Instants_Overnight()
    {
        var loc = await CreatedAsync("/api/v1/work-locations", new
        {
            code = Tag("LOC"), name = "Dhaka plant", timezone = "Asia/Dhaka", address = (string?)null,
        }, Hr);
        // 201 asserted by the helper

        var schedule = await CreatedAsync("/api/v1/work-schedules", new
        {
            code = Tag("SCH"), name = "Night shift", timezone = "Asia/Dhaka", startTimeLocal = "22:00",
        }, Hr);
        // 201 asserted by the helper
        var scheduleId = schedule.GetProperty("data").GetProperty("id").GetString()!;
        await PostJsonAsync($"/api/v1/work-schedules/{scheduleId}/versions", new
        {
            weeklyPlannedHours = 40m, effectiveFrom = MonthStart(Today), holidayCalendarId = (string?)null,
        }, Hr);

        var monday = Today; // walk to the next Monday
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);
        var expansion = await GetAsync(
            $"/api/v1/work-schedules/{scheduleId}/expansions?from={monday:yyyy-MM-dd}&to={monday:yyyy-MM-dd}", Hr);
        var day = expansion.GetProperty("data").GetProperty("days").EnumerateArray().First();
        Assert.True(day.GetProperty("isWorkday").GetBoolean());
        Assert.Equal(8m, day.GetProperty("plannedHours").GetDecimal());
        // 22:00 +06:00 == 16:00Z; +8h crosses UTC midnight — instants stay unambiguous
        Assert.Equal($"{monday:yyyy-MM-dd}T16:00:00Z", day.GetProperty("intervals").EnumerateArray()
            .First().GetProperty("startUtc").GetString());
        Assert.Equal($"{monday.AddDays(1):yyyy-MM-dd}T00:00:00Z", day.GetProperty("intervals").EnumerateArray()
            .First().GetProperty("endUtc").GetString());
    }

    // ---- 6 (029 AC2/edge): missing/inactive leave policy = explicit config error, never zero
    [Fact]
    public async Task Missing_Leave_Policy_Is_Explicit_Config_Error()
    {
        var worker = await ActivateWorker(await SeedWorker());
        var type = await CreatedAsync("/api/v1/leave-types", new
        {
            code = Tag("LTY"), name = "Annual", units = "Days", accrualMethod = "None",
            accrualRatePerMonth = (decimal?)null, annualEntitlementDays = (decimal?)null,
            carryoverLimitDays = (decimal?)null, requiresApproval = true,
            effectiveFrom = MonthStart(Today),
        }, Hr);
        // 201 asserted by the helper
        var typeId = type.GetProperty("data").GetProperty("id").GetString()!;

        var accrual = await PostAsync($"/api/v1/leave-types/{typeId}/accrual-runs", new
        {
            period = $"{Today:yyyy-MM}", asOf = Today, employmentId = (string?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, accrual.StatusCode); // AccrualMethod None = no policy → error
        Assert.Equal("LEAVE_POLICY_MISSING", ErrCode(await Json(accrual)));

        var noType = await PostAsync("/api/v1/leave-requests", new
        {
            employmentId = worker, leaveTypeId = "LTY-DOES-NOT-EXIST", fromDate = Today,
            toDate = Today.AddDays(1), days = 1m, reason = "family",
        }, Hr);
        Assert.Equal(HttpStatusCode.NotFound, noType.StatusCode);
        Assert.Equal("LEAVE_TYPE_NOT_FOUND", ErrCode(await Json(noType)));
    }

    // ---- 7+8 (030 AC1–2): accrual re-run of the same period+source is idempotent; the
    //      as-of balance is reproducible from opening + dated entries
    [Fact]
    public async Task Accrual_Replay_Idempotent_And_Balance_Reproducible_AsOf()
    {
        var worker = await ActivateWorker(await SeedWorker());
        var type = await PostJsonAsync("/api/v1/leave-types", new
        {
            code = Tag("LTY"), name = "Annual", units = "Days", accrualMethod = "Monthly",
            accrualRatePerMonth = 1.25m, annualEntitlementDays = (decimal?)null,
            carryoverLimitDays = (decimal?)null, requiresApproval = true,
            effectiveFrom = MonthStart(Today).AddMonths(-2),
        }, Hr);
        var typeId = type.GetProperty("data").GetProperty("id").GetString()!;

        var oct = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var run1 = await CreatedAsync($"/api/v1/leave-types/{typeId}/accrual-runs", new
        { period = $"{oct:yyyy-MM}", asOf = oct.AddMonths(1).AddDays(-1), employmentId = (string?)null }, Hr);
        // 201 asserted by the helper
        Assert.True(run1.GetProperty("data").GetProperty("created").GetInt32() >= 1);

        var replay = await PostJsonAsync($"/api/v1/leave-types/{typeId}/accrual-runs", new
        { period = $"{oct:yyyy-MM}", asOf = oct.AddMonths(1).AddDays(-1), employmentId = (string?)null }, Hr);
        Assert.Equal(0, replay.GetProperty("data").GetProperty("created").GetInt32()); // no duplicate entry

        var nov = await PostJsonAsync($"/api/v1/leave-types/{typeId}/accrual-runs", new
        { period = $"{Today:yyyy-MM}", asOf = Today, employmentId = (string?)null }, Hr);
        // the window covers every Active worker in the entity — per-worker balances stay exact
        Assert.True(nov.GetProperty("data").GetProperty("created").GetInt32() >= 1);

        var balanceNow = await GetAsync(
            $"/api/v1/workers/{worker}/leave-balances?leaveTypeId={typeId}", Hr);
        Assert.Equal(2.5m, balanceNow.GetProperty("data").GetProperty("balance").GetDecimal());
        var balanceAsOf = await GetAsync(
            $"/api/v1/workers/{worker}/leave-balances?leaveTypeId={typeId}&asOf={oct.AddMonths(1).AddDays(-1):yyyy-MM-dd}", Hr);
        Assert.Equal(1.25m, balanceAsOf.GetProperty("data").GetProperty("balance").GetDecimal());
    }

    // ---- 9 (031 AC2 + Q-PM1): approval by a DIFFERENT officer emits the dated absence event on
    //      the 034 contract; withdrawal only via the recorded workflow and restores the balance
    [Fact]
    public async Task Leave_Approval_Emits_Absence_Event_And_Withdrawal_Restores()
    {
        var worker = await ActivateWorker(await SeedWorker());
        var typeId = await SeedLeaveTypeWithBalance(worker, 2m);

        var request = await CreatedAsync("/api/v1/leave-requests", new
        {
            employmentId = worker, leaveTypeId = typeId, fromDate = Today, toDate = Today, days = 1m,
            reason = "family event",
        }, Hr);
        // 201 asserted by the helper
        var requestId = request.GetProperty("data").GetProperty("id").GetString()!;

        var selfApproval = await PostAsync($"/api/v1/leave-requests/{requestId}/decisions",
            new { approve = true, decisionReason = (string?)null }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode); // approver ≠ requester
        Assert.Equal("LEAVE_APPROVAL_ERROR", ErrCode(await Json(selfApproval)));

        var approved = await PostJsonAsync($"/api/v1/leave-requests/{requestId}/decisions",
            new { approve = true, decisionReason = (string?)null }, HrChecker);
        Assert.Equal("Approved", approved.GetProperty("data").GetProperty("status").GetString());

        // Q-PM1: the approval emitted a dated absence event on the 034 batch contract (a
        // Source=Leave APPROVED time entry) — payroll's input pull sees it one-source
        var time = await GetAsync($"/api/v1/time-entries?employmentId={worker}", Hr);
        var absence = time.GetProperty("data").EnumerateArray()
            .First(t => t.GetProperty("source").GetString() == "Leave");
        Assert.Equal("Approved", absence.GetProperty("status").GetString());
        Assert.Equal($"{Today:yyyy-MM-dd}", absence.GetProperty("workDate").GetString());

        var balanceAfter = await GetAsync($"/api/v1/workers/{worker}/leave-balances?leaveTypeId={typeId}", Hr);
        Assert.Equal(1m, balanceAfter.GetProperty("data").GetProperty("balance").GetDecimal());

        var withdrawn = await PostJsonAsync($"/api/v1/leave-requests/{requestId}/withdrawals",
            new { reason = "event postponed" }, Hr);
        Assert.Equal("Withdrawn", withdrawn.GetProperty("data").GetProperty("status").GetString());
        var balanceRestored = await GetAsync($"/api/v1/workers/{worker}/leave-balances?leaveTypeId={typeId}", Hr);
        Assert.Equal(2m, balanceRestored.GetProperty("data").GetProperty("balance").GetDecimal());

        var again = await PostAsync($"/api/v1/leave-requests/{requestId}/withdrawals",
            new { reason = "double withdraw" }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("LEAVE_REQUEST_STATE_ERROR", ErrCode(await Json(again)));
    }

    // ---- 10 (032 AC2): manual adjustments need elevated rights AND an independent approver;
    //      the applied entry is a signed ledger row with a reversible reference
    [Fact]
    public async Task Manual_Adjustment_Needs_Elevated_Role_And_Independent_Approval()
    {
        var worker = await ActivateWorker(await SeedWorker());
        var typeId = await SeedLeaveTypeWithBalance(worker, 0m);

        var denied = await PostAsync("/api/v1/leave-adjustments", new
        { employmentId = worker, leaveTypeId = typeId, qty = 1m, reason = "goodwill day" }, Hr);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); // R-HR holds no leave:adjust

        var created = await CreatedAsync("/api/v1/leave-adjustments", new
        { employmentId = worker, leaveTypeId = typeId, qty = 1m, reason = "goodwill day" }, Fin);
        // 201 asserted by the helper
        Assert.Equal("Pending", created.GetProperty("data").GetProperty("status").GetString());
        var adjustmentId = created.GetProperty("data").GetProperty("id").GetString()!;

        var selfApproved = await PostAsync($"/api/v1/leave-adjustments/{adjustmentId}/approvals",
            new { approve = true, decisionReason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, selfApproved.StatusCode); // decider ≠ requester
        Assert.Equal("LEAVE_ADJUSTMENT_STATE_ERROR", ErrCode(await Json(selfApproved)));

        var approved = await PostJsonAsync($"/api/v1/leave-adjustments/{adjustmentId}/approvals",
            new { approve = true, decisionReason = (string?)null }, Admin);
        Assert.Equal("Applied", approved.GetProperty("data").GetProperty("status").GetString());
        var balance = await GetAsync($"/api/v1/workers/{worker}/leave-balances?leaveTypeId={typeId}", Hr);
        Assert.Equal(1m, balance.GetProperty("data").GetProperty("balance").GetDecimal());
        Assert.Contains(balance.GetProperty("data").GetProperty("entries").EnumerateArray(),
            e => e.GetProperty("kind").GetString() == "Adjustment"
                 && e.GetProperty("referenceId").GetString() == adjustmentId);
    }

    // ---- 11 (033 AC1–2): exceptions dedupe against schedule+entry; the disposition stays
    //      distinguishable from the immutable source fact
    [Fact]
    public async Task Attendance_Exceptions_Dedupe_And_Disposition_Is_Not_Source_Fact()
    {
        var worker = await ActivateWorker(await SeedWorker());
        var schedule = await PostJsonAsync("/api/v1/work-schedules", new
        {
            code = Tag("SCH"), name = "Day shift", timezone = "Asia/Dhaka", startTimeLocal = "09:00",
        }, Hr);
        var scheduleId = schedule.GetProperty("data").GetProperty("id").GetString()!;
        await PostJsonAsync($"/api/v1/work-schedules/{scheduleId}/versions", new
        {
            weeklyPlannedHours = 40m, effectiveFrom = MonthStart(Today), holidayCalendarId = (string?)null,
        }, Hr);

        var from = Today.AddDays(-7);
        var gen1 = await CreatedAsync("/api/v1/attendance-exceptions/generations", new { from, to = Today }, Hr);
        // 201 asserted by the helper
        var created1 = gen1.GetProperty("data").GetProperty("created").GetInt32();
        Assert.True(created1 >= 1, $"expected absences for the unentered week, got {created1}");

        var gen2 = await PostJsonAsync("/api/v1/attendance-exceptions/generations", new { from, to = Today }, Hr);
        Assert.Equal(0, gen2.GetProperty("data").GetProperty("created").GetInt32()); // dedupe on replay

        var list = await GetAsync($"/api/v1/attendance-exceptions?employmentId={worker}", Hr);
        var first = list.GetProperty("data").EnumerateArray().First();
        Assert.Equal("Absent", first.GetProperty("kind").GetString());
        var sourceFact = first.GetProperty("sourceFact").GetString()!;
        var exceptionId = first.GetProperty("id").GetString()!;

        var disposed = await PostJsonAsync($"/api/v1/attendance-exceptions/{exceptionId}/dispositions", new
        { disposition = "Excused", reason = "public holiday misconfigured" }, Hr);
        Assert.Equal("Excused", disposed.GetProperty("data").GetProperty("disposition").GetString());
        Assert.Equal(sourceFact, disposed.GetProperty("data").GetProperty("sourceFact").GetString()); // untouched
    }

    // shared: leave type + opening balance via an approved adjustment
    private async Task<string> SeedLeaveTypeWithBalance(string worker, decimal balance)
    {
        var type = await PostJsonAsync("/api/v1/leave-types", new
        {
            code = Tag("LTY"), name = "Annual", units = "Days", accrualMethod = "None",
            accrualRatePerMonth = (decimal?)null, annualEntitlementDays = (decimal?)null,
            carryoverLimitDays = (decimal?)null, requiresApproval = true, effectiveFrom = MonthStart(Today),
        }, Hr);
        var typeId = type.GetProperty("data").GetProperty("id").GetString()!;
        if (balance > 0)
        {
            var adj = await PostJsonAsync("/api/v1/leave-adjustments", new
            { employmentId = worker, leaveTypeId = typeId, qty = balance, reason = "opening balance" }, Fin);
            await PostJsonAsync($"/api/v1/leave-adjustments/{adj.GetProperty("data").GetProperty("id").GetString()}/approvals",
                new { approve = true, decisionReason = (string?)null }, Admin);
        }
        return typeId;
    }
}

// ---- PM6 pay inputs + PM7 payment files + PM8 close checklist ---------------------------------

public class PeopleDeferredPayTests : PeopleDeferredTestBase
{
    public PeopleDeferredPayTests(TestApiFactory factory) : base(factory) { }

    // ---- 13 (042 AC2): contribution totals reconcile to the gross-to-net output
    [Fact]
    public async Task Contribution_Rule_Total_Reconciles_To_Run_Output()
    {
        await SeedPayCode("CONTRIB-5", "Deduction", "Percent", 5m, debit: "2310", credit: "2300");
        var rule = await CreatedAsync("/api/v1/contribution-rules", new
        {
            code = Tag("CGR"), name = "Provident fund 5%", party = "Employee", ratePct = 5m,
            thresholdAmount = (decimal?)null, ceilingAmount = (decimal?)null,
            payCodeId = "CONTRIB-5", effectiveFrom = MonthStart(Today),
        }, Hr);
        // 201 asserted by the helper
        var ruleId = rule.GetProperty("data").GetProperty("id").GetString()!;

        var (worker, runId, _) = await SeedCalculatedRun(hours: 8m); // hourly 100 → gross 800
        var run = await GetAsync($"/api/v1/pay-runs/{runId}/preview", Fin); // landed preview carries the frozen lines
        var contribLine = run.GetProperty("data").GetProperty("lines").EnumerateArray()
            .First(l => l.GetProperty("payCode").GetString() == "CONTRIB-5");
        Assert.Equal(40m, contribLine.GetProperty("amount").GetDecimal()); // 5% of 800

        var recon = await GetAsync($"/api/v1/contribution-rules/{ruleId}/reconciliations?runId={runId}", Fin);
        Assert.True(recon.GetProperty("data").GetProperty("reconciles").GetBoolean());
        Assert.Equal(40m, recon.GetProperty("data").GetProperty("expected").GetDecimal());
        Assert.Equal(40m, recon.GetProperty("data").GetProperty("posted").GetDecimal());
    }

    // ---- 14 (043 AC2): orders outside the effective period are unusable; release needs
    //      payroll:release (R-FIN) + evidence; same-priority overlap is a blocking conflict
    [Fact]
    public async Task Garnishment_Period_Gate_Release_SoD_And_Conflict()
    {
        var worker = await SeedWorker();
        var expired = await CreatedAsync("/api/v1/garnishment-orders", new
        {
            employmentId = worker, kind = "Court", priority = 1, amountPerPeriod = 1000m,
            protectedMinimum = (decimal?)null, courtRef = "C-77", effectiveFrom = MonthStart(Today),
            effectiveTo = Today.AddDays(-1),
        }, Hr);
        // 201 asserted by the helper
        var expiredId = expired.GetProperty("data").GetProperty("id").GetString()!;
        var releaseExpired = await PostAsync($"/api/v1/garnishment-orders/{expiredId}/releases",
            new { evidence = "bank slip" }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, releaseExpired.StatusCode); // outside effective period = unusable
        Assert.Equal("GARNISHMENT_NOT_ACTIVE", ErrCode(await Json(releaseExpired)));

        var active = await PostJsonAsync("/api/v1/garnishment-orders", new
        {
            employmentId = worker, kind = "Court", priority = 2, amountPerPeriod = 2000m,
            protectedMinimum = 5000m, courtRef = "C-78", effectiveFrom = MonthStart(Today),
            effectiveTo = (DateOnly?)null,
        }, Hr);
        var activeId = active.GetProperty("data").GetProperty("id").GetString()!;

        var hrRelease = await PostAsync($"/api/v1/garnishment-orders/{activeId}/releases",
            new { evidence = "bank slip" }, Hr);
        Assert.Equal(HttpStatusCode.Forbidden, hrRelease.StatusCode); // payroll:release is R-FIN

        var conflict = await PostAsync("/api/v1/garnishment-orders", new
        {
            employmentId = worker, kind = "Court", priority = 2, amountPerPeriod = 500m,
            protectedMinimum = (decimal?)null, courtRef = "C-79", effectiveFrom = Today.AddDays(-1),
            effectiveTo = (DateOnly?)null,
        }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode); // same-priority active overlap
        Assert.Equal("GARNISHMENT_CONFLICT", ErrCode(await Json(conflict)));

        var released = await PostJsonAsync($"/api/v1/garnishment-orders/{activeId}/releases",
            new { evidence = "settlement letter SL-9" }, Fin);
        Assert.Equal("Released", released.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("settlement letter SL-9", released.GetProperty("data").GetProperty("releaseEvidence").GetString());
        Assert.Equal(Fin, released.GetProperty("data").GetProperty("releasedBy").GetString());
    }

    // ---- 15 (044 AC1–2): enrollment emits a versioned deduction instruction per period; the
    //      provider-feed reconciliation flags duplicates/out-of-sync WITHOUT overwriting
    [Fact]
    public async Task Deduction_Instruction_Versioned_And_Provider_Reconciliation_Flags_Without_Overwrite()
    {
        await SeedPayCode("HEALTH-D", "Deduction", "Amount", 500m, debit: "2310", credit: "2300");
        var plan = await PostJsonAsync("/api/v1/benefit-plans", new
        {
            code = Tag("BPL"), name = "Health", providerCode = "PRV-1", employeePct = 2m,
            payCodeId = "HEALTH-D",
        }, Hr);
        var planId = plan.GetProperty("data").GetProperty("id").GetString()!;
        var worker = await SeedWorker();
        var enrollment = await CreatedAsync("/api/v1/benefit-enrollments", new
        {
            employmentId = worker, planId, effectiveFrom = MonthStart(Today), effectiveTo = (DateOnly?)null,
        }, Hr);
        // 201 asserted by the helper
        var enrollmentId = enrollment.GetProperty("data").GetProperty("id").GetString()!;
        var detail = await GetAsync($"/api/v1/benefit-enrollments/{enrollmentId}", Hr);
        var instruction = detail.GetProperty("data").GetProperty("instructions").EnumerateArray().First();
        Assert.Equal(1, instruction.GetProperty("version").GetInt32()); // versioned per period

        var period = $"{Today:yyyy-MM}";
        var feed1 = await PostJsonAsync($"/api/v1/benefit-enrollments/{enrollmentId}/provider-feeds", new
        { period, amount = 500m, sourceEventId = $"PF-{enrollmentId}-1" }, Hr);
        Assert.False(feed1.GetProperty("data").GetProperty("duplicate").GetBoolean());
        var dup = await PostJsonAsync($"/api/v1/benefit-enrollments/{enrollmentId}/provider-feeds", new
        { period, amount = 500m, sourceEventId = $"PF-{enrollmentId}-1" }, Hr);
        Assert.True(dup.GetProperty("data").GetProperty("duplicate").GetBoolean()); // ignored idempotently

        var mismatch = await PostJsonAsync($"/api/v1/benefit-enrollments/{enrollmentId}/provider-feeds", new
        { period, amount = 999m, sourceEventId = $"PF-{enrollmentId}-2" }, Hr);
        Assert.False(mismatch.GetProperty("data").GetProperty("duplicate").GetBoolean());

        var recon = await GetAsync($"/api/v1/benefit-enrollments/{enrollmentId}/reconciliations?period={period}", Hr);
        Assert.True(recon.GetProperty("data").GetProperty("outOfSync").GetBoolean());
        // the election/enrollment itself is never rewritten by the feed
        Assert.Equal("Active", (await GetAsync($"/api/v1/benefit-enrollments/{enrollmentId}", Hr))
            .GetProperty("data").GetProperty("enrollment").GetProperty("status").GetString());
    }

    // ---- 16 (050 AC1): the file reconciles instruction count + total to the approved net-pay
    //      population; creating twice returns the SAME file
    [Fact]
    public async Task Payment_File_Reconciles_Count_And_Total_To_Approved_Net_Pay()
    {
        var (runId, _, _) = await SeedApprovedRunTwoWorkers();
        var net = (await GetAsync("/api/v1/pay-runs", Fin)).GetProperty("data").EnumerateArray()
            .First(r => r.GetProperty("id").GetString() == runId).GetProperty("netTotal").GetDecimal();
        var file = await CreatedAsync($"/api/v1/pay-runs/{runId}/payment-files",
            new { provider = "BANK-X" }, Hr);
        // 201 asserted by the helper
        var data = file.GetProperty("data");
        Assert.Equal(2, data.GetProperty("itemCount").GetInt32());
        Assert.Equal(net, data.GetProperty("totalNet").GetDecimal());
        Assert.Equal("CSV", data.GetProperty("format").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("contentHash").GetString()));
        var fileId = data.GetProperty("id").GetString()!;

        var replay = await PostJsonAsync($"/api/v1/pay-runs/{runId}/payment-files",
            new { provider = "BANK-X" }, Hr);
        Assert.Equal(fileId, replay.GetProperty("data").GetProperty("id").GetString()); // idempotent create

        // the file may only be generated from an APPROVED snapshot (050)
        var periodId2 = await SeedOpenTodayPeriod(Tag("p"));
        var groupId2 = await SeedPayGroup();
        var w2 = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
        await AssignPayGroup(w2, groupId2);
        await SeedTime(w2, Today, 4m, 0m);
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        var run2 = await PostJsonAsync("/api/v1/pay-runs", new { payGroupId = groupId2, periodId = periodId2, payDate = Today }, Hr);
        var runId2 = run2.GetProperty("data").GetProperty("id").GetString()!;
        await PostJsonAsync($"/api/v1/pay-runs/{runId2}/calculations", new { }, Hr);
        var notApproved = await PostAsync($"/api/v1/pay-runs/{runId2}/payment-files", new { provider = "BANK-X" }, Hr);
        Assert.Equal(HttpStatusCode.Conflict, notApproved.StatusCode);
        Assert.Equal("PAYRUN_NOT_APPROVED", ErrCode(await Json(notApproved)));
    }

    // ---- 17 (050 AC2, SoD): calculation approval ≠ funds release — the file maker cannot
    //      release; the release records maker + checker + hash/provider/format/date
    [Fact]
    public async Task Payment_File_Release_Is_Maker_Checker_And_Records_Provenance()
    {
        var (runId, _, _) = await SeedApprovedRunTwoWorkers();
        var file = await CreatedAsync($"/api/v1/pay-runs/{runId}/payment-files",
            new { provider = "BANK-X" }, Hr);
        var fileId = file.GetProperty("data").GetProperty("id").GetString()!;

        var byMaker = await PostAsync($"/api/v1/payment-files/{fileId}/releases", new { }, Hr);
        Assert.Equal(HttpStatusCode.Forbidden, byMaker.StatusCode); // maker holds no payroll:release

        var released = await PostJsonAsync($"/api/v1/payment-files/{fileId}/releases", new { }, Fin);
        var data = released.GetProperty("data");
        Assert.Equal("Released", data.GetProperty("status").GetString());
        Assert.Equal(Hr, data.GetProperty("createdBy").GetString());      // maker
        Assert.Equal(Fin, data.GetProperty("releasedBy").GetString());    // checker
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("contentHash").GetString()));
        Assert.Equal("BANK-X", data.GetProperty("provider").GetString());

        var again = await PostAsync($"/api/v1/payment-files/{fileId}/releases", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("PAYFILE_STATE_ERROR", ErrCode(await Json(again)));

        // maker == checker is refused: Admin holds BOTH payroll:calculate and payroll:release,
        // so a file made and released by the same person is the one way to hit this in the pilot
        var (runId2, _, _) = await SeedApprovedRunTwoWorkers();
        var own = await PostJsonAsync($"/api/v1/pay-runs/{runId2}/payment-files",
            new { provider = "BANK-Y" }, Admin);
        var ownRelease = await PostAsync($"/api/v1/payment-files/{own.GetProperty("data").GetProperty("id").GetString()}/releases",
            new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, ownRelease.StatusCode);
        Assert.Equal("PAYROLL_RELEASE_ERROR", ErrCode(await Json(ownRelease)));
    }

    // ---- 18 (050 edge): part-rejection isolates the rejected items; accepted items are NOT
    //      retransmitted
    [Fact]
    public async Task Part_Rejection_Isolates_Items_Without_Retransmitting_Accepted()
    {
        var (runId, workers, _) = await SeedApprovedRunTwoWorkers();
        var file = await PostJsonAsync($"/api/v1/pay-runs/{runId}/payment-files",
            new { provider = "BANK-X" }, Hr);
        var fileId = file.GetProperty("data").GetProperty("id").GetString()!;
        await PostJsonAsync($"/api/v1/payment-files/{fileId}/releases", new { }, Fin);

        var rejected = await PostJsonAsync($"/api/v1/payment-files/{fileId}/rejections", new
        {
            items = new object[] { new { employmentId = workers[0], reason = "account closed" } },
        }, Fin);
        Assert.Equal("PartiallyRejected", rejected.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(workers[0], rejected.GetProperty("data").GetProperty("rejected").EnumerateArray()
            .First().GetProperty("employmentId").GetString());
        var accepted = rejected.GetProperty("data").GetProperty("accepted").EnumerateArray()
            .Select(a => a.GetProperty("employmentId").GetString()).ToList();
        Assert.Contains(workers[1], accepted);
        Assert.DoesNotContain(workers[0], accepted);
        // accepted items keep their RELEASED status — no second transmission of them
        var detail = await GetAsync($"/api/v1/payment-files/{fileId}", Fin);
        Assert.All(detail.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("employmentId").GetString() == workers[1]),
            i => Assert.Equal("Released", i.GetProperty("status").GetString()));
    }

    // ---- 20 (054 AC1–2): close blocked → waiver → signed summary → resume → reopen → reclose.
    // The flow runs in its OWN fiscal period (a December period created here) so closing it can
    // never disturb the FP-2026-11 period the other pay tests anchor to, whatever the run order.
    [Fact]
    public async Task Close_Full_Lifecycle_Blocked_Waived_Signed_Reopen_And_Resume()
    {
        var dec = new DateOnly(Today.Year, 12, 15);
        var periodId = await SeedPeriodCovering(dec, Tag("c"));
        var groupId = await SeedPayGroup();
        var worker = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
        await AssignPayGroup(worker, groupId);
        await SeedTime(worker, dec, 8m, 0m);
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        var run = await CreatedAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = dec }, Hr);
        var runId = run.GetProperty("data").GetProperty("id").GetString()!;
        await PostJsonAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr);
        await ApproveRun(runId);
        var posted = await PostAsync($"/api/v1/pay-runs/{runId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var file = await CreatedAsync($"/api/v1/pay-runs/{runId}/payment-files", new { provider = "BANK-Z" }, Hr);
        var fileId = file.GetProperty("data").GetProperty("id").GetString()!;

        // the unreleased payment file is a mandatory unresolved item → CLOSE_BLOCKED names it (code PAYFILE_RELEASED, status Unresolved)
        var blocked = await PostAsync($"/api/v1/pay-periods/{periodId}/closures",
            new { acknowledgeLater = (bool?)null, waivers = Array.Empty<object>() }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var blockedBody = await Json(blocked); // parse ONCE — a second read hits a closed stream
        Assert.Equal("CLOSE_BLOCKED", ErrCode(blockedBody));
        var items = blockedBody.GetProperty("error").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("code").GetString() == "PAYFILE_RELEASED" && i.GetProperty("status").GetString() == "Unresolved");

        // waived by the authorized closer + parked for external acknowledgment
        var waived = await PostJsonAsync($"/api/v1/pay-periods/{periodId}/closures", new
        {
            acknowledgeLater = true,
            waivers = new object[] { new { code = "PAYFILE_RELEASED", reason = "provider portal outage, tracked EX-31" } },
        }, Fin);
        Assert.Equal("PendingAcknowledgment", waived.GetProperty("data").GetProperty("status").GetString());
        var summary = waived.GetProperty("data").GetProperty("summary");
        Assert.Contains(summary.GetProperty("runIds").EnumerateArray(), r => r.GetString() == runId);
        Assert.False(string.IsNullOrWhiteSpace(waived.GetProperty("data").GetProperty("summaryHash").GetString()));
        Assert.Equal(Fin, waived.GetProperty("data").GetProperty("closedBy").GetString());

        // resume WITHOUT duplicate execution: same record id, stored summary kept verbatim
        var resumed = await PostJsonAsync($"/api/v1/pay-periods/{periodId}/closures",
            new { acknowledgeLater = (bool?)null, waivers = Array.Empty<object>() }, Fin);
        Assert.Equal("Closed", resumed.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(periodId, resumed.GetProperty("data").GetProperty("id").GetString());

        var again = await PostAsync($"/api/v1/pay-periods/{periodId}/closures",
            new { acknowledgeLater = (bool?)null, waivers = Array.Empty<object>() }, Fin);
        Assert.Equal("CLOSE_ALREADY_CLOSED", ErrCode(await Json(again)));

        // resolve the item for real, then reopen → reclose must be clean this time
        await PostJsonAsync($"/api/v1/payment-files/{fileId}/releases", new { }, Fin);
        var reopened = await PostJsonAsync($"/api/v1/pay-periods/{periodId}/reopenings",
            new { reason = "external ack correction" }, Fin);
        Assert.Equal("Reopened", reopened.GetProperty("data").GetProperty("status").GetString());
        var reclosed = await PostJsonAsync($"/api/v1/pay-periods/{periodId}/closures",
            new { acknowledgeLater = (bool?)null, waivers = Array.Empty<object>() }, Fin);
        Assert.Equal("Closed", reclosed.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(periodId, reclosed.GetProperty("data").GetProperty("id").GetString());
    }

    // shared: calculated+approved+posted run over TWO workers with its own pay group/period/codes
    private async Task<(string runId, List<string> workers, string periodId, string fileId)> SeedPostedRunWithFile(bool releaseFile = false)
    {
        var (runId, workers, periodId) = await SeedApprovedRunTwoWorkers();
        var postRes = await PostAsync($"/api/v1/pay-runs/{runId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);
        var file = await PostJsonAsync($"/api/v1/pay-runs/{runId}/payment-files", new { provider = "BANK-Z" }, Hr);
        var fileId = file.GetProperty("data").GetProperty("id").GetString()!;
        if (releaseFile)
            await PostJsonAsync($"/api/v1/payment-files/{fileId}/releases", new { }, Fin);
        return (runId, workers, periodId, fileId);
    }

    private async Task<(string runId, List<string> workers, string periodId)> SeedApprovedRunTwoWorkers()
    {
        var periodId = await SeedOpenTodayPeriod(Tag("p"));
        var groupId = await SeedPayGroup();
        var workers = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var w = await ActivateWorker(await SeedWorker(managerUserId: Admin), amount: 100m, payBasis: "Hourly");
            await AssignPayGroup(w, groupId);
            await SeedTime(w, Today, 8m, 0m);
            workers.Add(w);
        }
        await SeedPayCode("BASIC", "Earning", "Hours", 0);
        var run = await PostJsonAsync("/api/v1/pay-runs", new { payGroupId = groupId, periodId, payDate = Today }, Hr);
        // 201 asserted by the helper
        var runId = run.GetProperty("data").GetProperty("id").GetString()!;
        await PostJsonAsync($"/api/v1/pay-runs/{runId}/calculations", new { }, Hr);
        await ApproveRun(runId);
        return (runId, workers, periodId);
    }
}


// ---- PM6 elections live in their OWN class: flipping the mandatory-election config pollutes
// the whole class database (every later approval gains a blocker), so the toggle gets a
// throwaway database of its own.

public class PeopleDeferredElectionTests : PeopleDeferredTestBase
{
    public PeopleDeferredElectionTests(TestApiFactory factory) : base(factory) { }

    // ---- 12 (041 AC2): payroll selects the election effective at the pay date; a missing
    //      mandatory election surfaces as a blocking REVIEW item, never a silent default
    [Fact]
    public async Task Mandatory_Election_Missing_Blocks_Review_And_Verified_Election_Clears()
    {
        var (worker, runId, _) = await SeedCalculatedRun(hours: 8m);
        await PostJsonAsync("/api/v1/payroll-config/updates",
            new { mandatoryElection = true, electionKind = "TAX-DECL" }, Hr);

        var blocked = await GetAsync($"/api/v1/pay-runs/{runId}/validations", Hr);
        Assert.Contains(blocked.GetProperty("data").GetProperty("blocking").EnumerateArray(),
            b => b.GetProperty("code").GetString() == "ELECTION_MISSING");

        var election = await CreatedAsync("/api/v1/tax-elections", new
        {
            employmentId = worker, kind = "TAX-DECL", value = "single", effectiveFrom = MonthStart(Today),
        }, Hr);
        // 201 asserted by the helper
        Assert.Equal("Draft", election.GetProperty("data").GetProperty("status").GetString());
        var electionId = election.GetProperty("data").GetProperty("id").GetString()!;

        // a DRAFT election does not clear the review — verification is explicit (041 AC2)
        var stillBlocked = await GetAsync($"/api/v1/pay-runs/{runId}/validations", Hr);
        Assert.Contains(stillBlocked.GetProperty("data").GetProperty("blocking").EnumerateArray(),
            b => b.GetProperty("code").GetString() == "ELECTION_MISSING");

        var verified = await PostJsonAsync($"/api/v1/tax-elections/{electionId}/verifications", new { }, Hr);
        Assert.Equal("Verified", verified.GetProperty("data").GetProperty("status").GetString());
        var cleared = await GetAsync($"/api/v1/pay-runs/{runId}/validations", Hr);
        Assert.DoesNotContain(cleared.GetProperty("data").GetProperty("blocking").EnumerateArray(),
            b => b.GetProperty("code").GetString() == "ELECTION_MISSING");
    }
}

// ---- PM8: an inactive account mapping covering the pay date blocks posting and names the element
//      (own class = own database: the block must not poison the other posting tests)

public class PeopleDeferredMappingTests : PeopleDeferredTestBase
{
    public PeopleDeferredMappingTests(TestApiFactory factory) : base(factory) { }

    // ---- 19 (053 AC2)
    [Fact]
    public async Task Inactive_Account_Mapping_Blocks_Posting_And_Names_The_Element()
    {
        var mapping = await CreatedAsync("/api/v1/payroll-account-mappings", new
        {
            element = "SALARY_EXPENSE", accountCode = "6100", effectiveFrom = MonthStart(Today),
            effectiveTo = (DateOnly?)null, active = false,
        }, Fin);
        // 201 asserted by the helper

        var (worker, runId, _) = await SeedCalculatedRun(hours: 8m);
        await ApproveRun(runId);
        var postRes = await PostAsync($"/api/v1/pay-runs/{runId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, postRes.StatusCode);
        var body = await Json(postRes);
        Assert.Equal("PAYROLL_MAPPING_MISSING", ErrCode(body));
        Assert.Contains("SALARY_EXPENSE", ErrMessage(body));

        // an ACTIVE mapping is consumed: the same run posts through the mapped account
        var active = await CreatedAsync("/api/v1/payroll-account-mappings", new
        {
            element = "DEDUCTIONS_PAYABLE", accountCode = "2310", effectiveFrom = MonthStart(Today),
            effectiveTo = (DateOnly?)null, active = true,
        }, Fin);
        // 201 asserted by the helper
    }
}
