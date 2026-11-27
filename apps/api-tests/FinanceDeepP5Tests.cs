using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

// FINANCE-deep P5 foundations (ERP-FINANCE-002/003/006/007/008/016): currency/FX master with
// provenance + exactly-one-or-block resolution, mandatory dimensions at journal-create line level,
// 002 posting eligibility enforced at draft, effective-dated functional currency that never
// rewrites posted history, number-series admin view + void-with-reason.
// Brief §7 items 25–28. Seeds exercised: BDT/USD/JPY currencies, FXR-0001 (USD→BDT approved,
// provenance), FXR-0002 (JPY→BDT pending), CostCenter dimension set + values, R-FIN2/U-015.
// ORDER-INDEPENDENT: EnsureRegion() (idempotent, run at the top of every journal-creating test)
// creates the REQUIRED "Region" set — so every journal in this file carries Region dims, and no
// test depends on execution order (empirically xUnit's method order here is source order).

namespace Erp.Api.Tests;

public class FinanceDeepP5Tests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public FinanceDeepP5Tests(TestApiFactory factory) => _factory = factory;

    private const string Admin = "U-001";
    private const string Fin = "U-003";
    private const string FinMaker = "U-015"; // R-FIN2: journals:write, tax/ap maker perms, NO fx:write
    private static readonly Dictionary<string, string> Region = new() { ["Region"] = "R-1" };

    // idempotent per fixture: the required Region set must exist before any journal in this
    // class drafts, whichever test runs first. xUnit news up a class instance per test, so
    // "already exists" (409) is success — same tolerance as TestDatabase.Create.
    private async Task EnsureRegion()
    {
        var res = await PostAsync("/api/v1/dimension-sets", new { code = "Region", name = "Sales region", required = true }, Fin);
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"region set: {(int)res.StatusCode}");
        res = await PostAsync("/api/v1/dimension-values",
            new { setCode = "Region", value = "R-1", effectiveFrom = new DateOnly(2026, 1, 1) }, Fin);
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"R-1: {(int)res.StatusCode}");
        // inactive value with a CLOSED interval — a rejection fixture for new postings (fix round:
        // Active=true is required for new drafts at any date; only posted history may reference it)
        res = await PostAsync("/api/v1/dimension-values",
            new { setCode = "Region", value = "R-2", effectiveFrom = new DateOnly(2026, 1, 1), effectiveTo = new DateOnly(2026, 3, 31), active = false }, Fin);
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"R-2: {(int)res.StatusCode}");
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.PostAsJsonAsync(path, body);
    }

    private async Task<JsonElement> GetAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        var res = await client.GetAsync(path);
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> GetRawAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.GetAsync(path);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static object Line(string code, string name, decimal dr, decimal cr,
        string? currency = null, decimal? amountInCurrency = null, Dictionary<string, string>? dimensions = null) =>
        new { accountCode = code, accountName = name, debit = dr, credit = cr, currency, amountInCurrency, dimensions };

    private async Task<(HttpResponseMessage res, string journalId)> CreateDraft(string memo, object[] lines, DateOnly date)
    {
        var res = await PostAsync("/api/v1/journals", new { memo, lines, date }, Fin);
        if (res.StatusCode != HttpStatusCode.Created)
            Assert.Fail($"journal create {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (res, (await Json(res)).GetProperty("data").GetProperty("id").GetString()!);
    }

    // 002 AC1 pinned at BUILD time (brief §8: recon could not verify) — a journal line to a
    // summary-only account is rejected at CREATE, and an unknown account code names itself.
    [Fact]
    public async Task Summary_Only_Or_Unknown_Account_Rejected_At_Journal_Create()
    {
        await EnsureRegion();
        var res = await PostAsync("/api/v1/accounts",
            new { code = "9900", name = "Retained Earnings (summary)", type = "Equity", normalBalance = "Credit", directPostingAllowed = false }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var prior = MonthStart(Today).AddMonths(-1).AddDays(14);
        var summaryRes = await PostAsync("/api/v1/journals", new
        {
            memo = "summary-only probe",
            lines = new[] { Line("9900", "Retained Earnings (summary)", 10m, 0m, dimensions: Region), Line("2100", "Accounts Payable", 0m, 10m, dimensions: Region) },
            date = prior,
        }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, summaryRes.StatusCode);
        var body = await Json(summaryRes);
        Assert.Equal("ACCOUNT_NOT_POSTABLE", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("9900", body.GetProperty("error").GetProperty("message").GetString());

        var unknown = await PostAsync("/api/v1/journals", new
        {
            memo = "unknown account probe",
            lines = new[] { Line("ZZZZ", "No such account", 5m, 0m), Line("2100", "Accounts Payable", 0m, 5m) },
            date = prior,
        }, Fin);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("ACCOUNT_NOT_FOUND", (await Json(unknown)).GetProperty("error").GetProperty("code").GetString());

        // postable pair still drafts fine
        var (ok, _) = await CreateDraftProbe(prior,
            Line("1400", "Inventory", 10m, 0m, dimensions: Region),
            Line("2100", "Accounts Payable", 0m, 10m, dimensions: Region));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    // 003 AC1/AC2 — a line missing a required dimension is rejected LINE-LEVEL naming the
    // dimension; a value outside its effective interval (or not in the permitted set) is
    // rejected; NEW postings must cite an ACTIVE value (fix round) — only lines already
    // posted while the value was active keep referencing it without re-validation.
    [Fact]
    public async Task Missing_Required_Dimension_Rejected_Line_Level_And_Inactive_Value_Rejected_For_New_Postings()
    {
        await EnsureRegion(); // the required set + R-1 (open) + R-2 (inactive, 2026 Q1 only)

        // AC1: omitted required dimension → 400 naming the dimension code
        var missing = await PostAsync("/api/v1/journals", new
        {
            memo = "dimension omission probe",
            lines = new[] { Line("5100", "Cost of Goods Sold", 10m, 0m), Line("2100", "Accounts Payable", 0m, 10m) },
            date = Today,
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var body = await Json(missing);
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Region", body.GetProperty("error").GetProperty("message").GetString());

        // value not in the permitted set → rejected naming the value
        var badValue = await PostAsync("/api/v1/journals", new
        {
            memo = "dimension bad value probe",
            lines = new[] { Line("5100", "Cost of Goods Sold", 10m, 0m, dimensions: new Dictionary<string, string> { ["Region"] = "NOPE" }), Line("2100", "Accounts Payable", 0m, 10m, dimensions: new Dictionary<string, string> { ["Region"] = "NOPE" }) },
            date = Today,
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, badValue.StatusCode);
        Assert.Contains("NOPE", (await Json(badValue)).GetProperty("error").GetProperty("message").GetString());

        // value outside its effective interval → rejected with the date named
        var outside = await PostAsync("/api/v1/journals", new
        {
            memo = "dimension interval probe",
            lines = new[] { Line("5100", "Cost of Goods Sold", 10m, 0m, dimensions: new Dictionary<string, string> { ["Region"] = "R-2" }), Line("2100", "Accounts Payable", 0m, 10m, dimensions: new Dictionary<string, string> { ["Region"] = "R-2" }) },
            date = new DateOnly(2026, 6, 15),
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Contains("Region", (await Json(outside)).GetProperty("error").GetProperty("message").GetString());

        // fix round: Active=true is required for NEW postings (any date) — seeded CC-002 is
        // inactive INSIDE its 2026 interval, so a new line citing it is rejected even in-interval
        // (Region dims present so the required-dimension check is not what rejects)
        var inactive = await PostAsync("/api/v1/journals", new
        {
            memo = "dimension inactive probe",
            lines = new[] { Line("5100", "Cost of Goods Sold", 10m, 0m, dimensions: new Dictionary<string, string> { ["Region"] = "R-1", ["CostCenter"] = "CC-002" }), Line("2100", "Accounts Payable", 0m, 10m, dimensions: new Dictionary<string, string> { ["Region"] = "R-1", ["CostCenter"] = "CC-002" }) },
            date = Today,
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("CC-002", (await Json(inactive)).GetProperty("error").GetProperty("message").GetString());

        // the back-dated draft the pre-fix suite accepted is rejected the same way — AC2 now
        // means only that POSTED lines are never re-validated when a value is later deactivated
        var historical = await PostAsync("/api/v1/journals", new
        {
            memo = "dimension historical probe",
            lines = new[] { Line("5100", "Cost of Goods Sold", 10m, 0m, dimensions: new Dictionary<string, string> { ["Region"] = "R-2" }), Line("2100", "Accounts Payable", 0m, 10m, dimensions: new Dictionary<string, string> { ["Region"] = "R-2" }) },
            date = new DateOnly(2026, 1, 15),
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, historical.StatusCode);
        Assert.Contains("R-2", (await Json(historical)).GetProperty("error").GetProperty("message").GetString());
    }

    // 006 AC1/AC2 — unknown/inactive currency rejected at journal create; zero/2/3-decimal
    // settlement scale validated per the cited currency (JPY seeds 0dp).
    [Fact]
    public async Task Unknown_Or_Inactive_Currency_And_Scale_Rejected_At_Journal_Create()
    {
        await EnsureRegion();
        var prior = MonthStart(Today).AddMonths(-1).AddDays(14);

        var unknown = await PostAsync("/api/v1/journals", new
        {
            memo = "unknown currency probe",
            lines = new[] { Line("1400", "Inventory", 10m, 0m, currency: "XYZ", dimensions: Region), Line("2100", "Accounts Payable", 0m, 10m, dimensions: Region) },
            date = prior,
        }, Fin);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("CURRENCY_NOT_FOUND", (await Json(unknown)).GetProperty("error").GetProperty("code").GetString());

        // inactive currency (created here, seeded JPY/USD stay active)
        var res = await PostAsync("/api/v1/currencies",
            new { code = "TST", name = "Test krona", decimalScale = 2, active = false }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var inactive = await PostAsync("/api/v1/journals", new
        {
            memo = "inactive currency probe",
            lines = new[] { Line("1400", "Inventory", 10m, 0m, currency: "TST", dimensions: Region), Line("2100", "Accounts Payable", 0m, 10m, dimensions: Region) },
            date = prior,
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("TST", (await Json(inactive)).GetProperty("error").GetProperty("message").GetString());

        // JPY settles at 0 decimals — a fractional TRANSACTION amount is rejected naming the currency + scale
        var scale = await PostAsync("/api/v1/journals", new
        {
            memo = "jpy scale probe",
            lines = new[] { Line("1400", "Inventory", 100.50m, 0m, currency: "JPY", amountInCurrency: 100.50m, dimensions: Region), Line("2100", "Accounts Payable", 0m, 100.50m, dimensions: Region) },
            date = prior,
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, scale.StatusCode);
        var scaleBody = await Json(scale);
        Assert.Equal("VALIDATION_ERROR", scaleBody.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("JPY", scaleBody.GetProperty("error").GetProperty("message").GetString());

        // fix round: Debit/Credit are FUNCTIONAL-currency amounts (BDT, 2dp) — the LINE currency's
        // scale only governs amountInCurrency. A 2dp functional debit on a 0dp JPY line is legal
        // when the transaction amount stays 0dp; the pre-fix check falsely rejected it on JPY scale.
        var jpyOk = await PostAsync("/api/v1/journals", new
        {
            memo = "jpy functional scale probe",
            lines = new[] { Line("1400", "Inventory", 100.55m, 0m, currency: "JPY", amountInCurrency: 100m, dimensions: Region), Line("2100", "Accounts Payable", 0m, 100.55m, dimensions: Region) },
            date = prior,
        }, Fin);
        Assert.Equal(HttpStatusCode.Created, jpyOk.StatusCode);

        // ...while a functional debit beyond the FUNCTIONAL currency's own scale is rejected even
        // on a 3dp line currency whose transaction amount is perfectly in BHD scale
        await PostAsync("/api/v1/currencies", new { code = "BHD", name = "Bahraini Dinar", decimalScale = 3 }, Fin);
        var bhd = await PostAsync("/api/v1/journals", new
        {
            memo = "functional scale violation probe",
            lines = new[] { Line("1400", "Inventory", 10.555m, 0m, currency: "BHD", amountInCurrency: 10.555m, dimensions: Region), Line("2100", "Accounts Payable", 0m, 10.555m, dimensions: Region) },
            date = prior,
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, bhd.StatusCode);
        var bhdBody = await Json(bhd);
        Assert.Equal("VALIDATION_ERROR", bhdBody.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("BDT", bhdBody.GetProperty("error").GetProperty("message").GetString());

        // valid USD line drafts and stores the transaction currency on the line (007 AC1)
        var (ok, journalId) = await CreateDraft("usd line probe", new[]
        {
            Line("1400", "Inventory", 10.25m, 0m, currency: "USD", amountInCurrency: 10.25m, dimensions: Region),
            Line("2100", "Accounts Payable", 0m, 10.25m, dimensions: Region),
        }, prior);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var stored = await GetAsync("/api/v1/journals", Admin);
        var line = stored.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("id").GetString() == journalId)
            .GetProperty("lines").EnumerateArray().First(l => l.GetProperty("currency").GetString() == "USD");
        Assert.Equal(10.25m, line.GetProperty("amountInCurrency").GetDecimal());
    }

    // 008 AC1/AC2 — resolution returns EXACTLY ONE approved rate per pair/date/type or a
    // blocking error (FX_RATE_MISSING / FX_RATE_AMBIGUOUS, no silent fallback); approving a
    // second rate over an approved one is refused up front (no state change) and the recovery
    // path is withdraw-with-reason; provenance is stored (source, retrieved-at, precision,
    // rate date); approval is maker-checked.
    [Fact]
    public async Task Fx_Resolution_Exactly_One_Or_Blocked_Provenance_And_Maker_Checker()
    {
        // seeded approved pair resolves to exactly one rate with provenance
        var ok = await GetAsync("/api/v1/fx-rates/resolve?base=USD&quote=BDT&date=2026-01-01&type=Spot", Fin);
        Assert.Equal(HttpStatusCode.OK, ok.GetProperty("success").GetBoolean() ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
        Assert.Equal(122.5m, ok.GetProperty("data").GetProperty("rate").GetDecimal());
        Assert.Contains("seed", ok.GetProperty("data").GetProperty("source").GetString());
        Assert.Equal("Approved", ok.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(4, ok.GetProperty("data").GetProperty("precision").GetInt32());

        // no rate for the pair → blocking missing, never a fallback; missing reference data is
        // a 404, not a state conflict (fix round)
        var missingRes = await GetRawAsync("/api/v1/fx-rates/resolve?base=USD&quote=EUR&date=2026-01-01&type=Spot", Fin);
        var missing = await Json(missingRes);
        Assert.False(missing.GetProperty("success").GetBoolean());
        Assert.Equal("FX_RATE_MISSING", missing.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missingRes.StatusCode);

        // create a second USD→BDT rate for the same date/type; approver must differ from preparer
        var res = await PostAsync("/api/v1/fx-rates", new
        {
            baseCurrency = "USD", quoteCurrency = "BDT", rateDate = new DateOnly(2026, 1, 1),
            rateType = "Spot", rate = 120m, precision = 4, source = "p5-test-suite",
        }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await Json(res);
        var rateId = created.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal("Pending", created.GetProperty("data").GetProperty("status").GetString());

        var self = await PostAsync($"/api/v1/fx-rates/{rateId}/approvals", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("FX_RATE_APPROVAL_ERROR", (await Json(self)).GetProperty("error").GetProperty("code").GetString());

        // fix round: approving a second rate for an already-approved key is refused up front —
        // FX_RATE_AMBIGUOUS with NO state change (recovery is withdrawal, never a silent pick)
        var second = await PostAsync($"/api/v1/fx-rates/{rateId}/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("FX_RATE_AMBIGUOUS", (await Json(second)).GetProperty("error").GetProperty("code").GetString());
        var stillPending = await GetAsync("/api/v1/fx-rates", Fin);
        Assert.Equal("Pending", stillPending.GetProperty("data").EnumerateArray()
            .First(r => r.GetProperty("id").GetString() == rateId).GetProperty("status").GetString());

        // withdrawal without a reason is a 400 — the reason is the recovery audit trail
        var noReason = await PostAsync("/api/v1/fx-rates/FXR-0001/withdrawals", new { }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        // (b) recovery path: withdraw the approved rate, then the pending one approves cleanly
        var withdrawn = await PostAsync("/api/v1/fx-rates/FXR-0001/withdrawals",
            new { reason = "superseded by corrected bank feed" }, Fin);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal("Withdrawn", (await Json(withdrawn)).GetProperty("data").GetProperty("status").GetString());

        // a Pending rate withdraws too — and a withdrawn rate can never be approved afterwards
        var withdrawPending = await PostAsync("/api/v1/fx-rates/FXR-0002/withdrawals",
            new { reason = "seed pair retired" }, Admin);
        Assert.Equal(HttpStatusCode.OK, withdrawPending.StatusCode);
        var approveWithdrawn = await PostAsync("/api/v1/fx-rates/FXR-0002/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, approveWithdrawn.StatusCode);
        Assert.Equal("FX_RATE_APPROVAL_ERROR", (await Json(approveWithdrawn)).GetProperty("error").GetProperty("code").GetString());

        // ambiguity resolved: the second rate now approves and resolution returns EXACTLY ONE
        var approved = await PostAsync($"/api/v1/fx-rates/{rateId}/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var body = await Json(approved);
        Assert.Equal("Approved", body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(Admin, body.GetProperty("data").GetProperty("approvedBy").GetString());

        // re-approve is a state conflict
        var again = await PostAsync($"/api/v1/fx-rates/{rateId}/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("FX_RATE_APPROVAL_ERROR", (await Json(again)).GetProperty("error").GetProperty("code").GetString());

        var resolved = await GetAsync("/api/v1/fx-rates/resolve?base=USD&quote=BDT&date=2026-01-01&type=Spot", Fin);
        Assert.True(resolved.GetProperty("success").GetBoolean());
        Assert.Equal(120m, resolved.GetProperty("data").GetProperty("rate").GetDecimal());

        // 404 on approvals against an unknown rate id
        var noRate = await PostAsync("/api/v1/fx-rates/FXR-9999/approvals", new { }, Admin);
        Assert.Equal(HttpStatusCode.NotFound, noRate.StatusCode);
        Assert.Equal("FX_RATE_NOT_FOUND", (await Json(noRate)).GetProperty("error").GetProperty("code").GetString());
    }

    // 007 AC1/AC2 — the functional-currency change is effective-dated and recorded (setting +
    // audit/outbox event) and NEVER rewrites posted journals' stored functional values.
    [Fact]
    public async Task Functional_Currency_Change_Is_Effective_Dated_And_Never_Rewrites_Posted_History()
    {
        await EnsureRegion();
        var prior = MonthStart(Today).AddMonths(-1).AddDays(14);
        var (_, journalId) = await CreateDraft("functional history probe", new[]
        {
            Line("1400", "Inventory", 25m, 0m, dimensions: Region),
            Line("2100", "Accounts Payable", 0m, 25m, dimensions: Region),
        }, prior);
        var approved = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var before = await GetAsync("/api/v1/journals", Admin);
        var beforeJournal = before.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("id").GetString() == journalId);

        // effective NEXT month — current-month postings keep resolving against BDT
        var res = await PostAsync("/api/v1/finance-settings", new
        {
            key = "finance.functional_currency",
            value = "USD",
            effectiveFrom = MonthStart(Today).AddMonths(1),
        }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var setting = (await Json(res)).GetProperty("data");
        Assert.Equal("USD", setting.GetProperty("value").GetString());
        Assert.Equal(MonthStart(Today).AddMonths(1), DateOnly.Parse(setting.GetProperty("effectiveFrom").GetString()!));

        // ratified ruling: with a single-row upsert there is no previous value to resolve a
        // pre-effective draft against — a draft dated BEFORE the pending change is rejected,
        // naming the pending change and both dates
        var stale = await PostAsync("/api/v1/journals", new
        {
            memo = "pre-effective draft probe",
            lines = new[] { Line("1400", "Inventory", 10m, 0m, dimensions: Region), Line("2100", "Accounts Payable", 0m, 10m, dimensions: Region) },
            date = Today,
        }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        var staleBody = await Json(stale);
        Assert.Equal("VALIDATION_ERROR", staleBody.GetProperty("error").GetProperty("code").GetString());
        var staleMessage = staleBody.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("USD", staleMessage);
        Assert.Contains(MonthStart(Today).AddMonths(1).ToString("yyyy-MM-dd"), staleMessage);
        Assert.Contains(Today.ToString("yyyy-MM-dd"), staleMessage);

        // drafts dated on/after the effective date use the new value and pass
        var fresh = await PostAsync("/api/v1/journals", new
        {
            memo = "post-effective draft probe",
            lines = new[] { Line("1400", "Inventory", 10m, 0m, dimensions: Region), Line("2100", "Accounts Payable", 0m, 10m, dimensions: Region) },
            date = MonthStart(Today).AddMonths(1),
        }, Fin);
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);

        // a bogus functional currency is rejected
        var bogus = await PostAsync("/api/v1/finance-settings", new
        { key = "finance.functional_currency", value = "ZZZ" }, Fin);
        Assert.Equal(HttpStatusCode.NotFound, bogus.StatusCode);
        Assert.Equal("CURRENCY_NOT_FOUND", (await Json(bogus)).GetProperty("error").GetProperty("code").GetString());

        // AC2: the posted journal is byte-identical after the change — history never restated
        var after = await GetAsync("/api/v1/journals", Admin);
        var afterJournal = after.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("id").GetString() == journalId);
        Assert.Equal(beforeJournal.GetProperty("lines").GetRawText(), afterJournal.GetProperty("lines").GetRawText());
        Assert.Equal("Posted", afterJournal.GetProperty("status").GetString());

        // the change is recorded: outbox event + audit trail
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains(outbox.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("type").GetString()),
            t => t == "finance.functional_currency.changed");
        var events = await GetAsync("/api/v1/audit-events", Admin);
        Assert.Contains(events.GetProperty("data").EnumerateArray(),
            e => e.GetProperty("entityType").GetString() == "finance_setting"
              && (e.GetProperty("summary").GetString() ?? "").Contains("finance.functional_currency"));

        // re-review F5 — order-independence: undo the USD flip (single-row upsert) with an
        // already-effective BDT so any later test in this class drafts against the seeded
        // functional currency regardless of runner ordering
        var reset = await PostAsync("/api/v1/finance-settings", new
        {
            key = "finance.functional_currency",
            value = "BDT",
            effectiveFrom = MonthStart(Today).AddMonths(-1),
        }, Fin);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }

    // 006 — currency CRUD: seeded 0/2/3-scale family, duplicate rejection, scale ∈ {0,2,3} only
    [Fact]
    public async Task Currency_Crud_Scale_Gating_And_Duplicates()
    {
        var list = await GetAsync("/api/v1/currencies", Fin);
        var data = list.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(data, c => c.GetProperty("code").GetString() == "BDT" && c.GetProperty("decimalScale").GetInt32() == 2);
        Assert.Contains(data, c => c.GetProperty("code").GetString() == "JPY" && c.GetProperty("decimalScale").GetInt32() == 0);
        Assert.Contains(data, c => c.GetProperty("code").GetString() == "USD" && c.GetProperty("decimalScale").GetInt32() == 2);

        var dup = await PostAsync("/api/v1/currencies", new { code = "BDT", name = "Taka again", decimalScale = 2 }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("CURRENCY_EXISTS", (await Json(dup)).GetProperty("error").GetProperty("code").GetString());

        var badScale = await PostAsync("/api/v1/currencies", new { code = "XXX1", name = "One decimal", decimalScale = 1 }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, badScale.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(badScale)).GetProperty("error").GetProperty("code").GetString());

        var created = await PostAsync("/api/v1/currencies", new { code = "KWD", name = "Kuwaiti Dinar", decimalScale = 3 }, Fin);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("KWD", (await Json(created)).GetProperty("data").GetProperty("code").GetString());
    }

    // 003 — dimension-set/value CRUD guards: duplicate set, unknown set, inverted interval
    [Fact]
    public async Task Dimension_Set_And_Value_Crud_Guards()
    {
        var dup = await PostAsync("/api/v1/dimension-sets", new { code = "CostCenter", name = "Cost Center", required = false }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("DIMENSION_SET_EXISTS", (await Json(dup)).GetProperty("error").GetProperty("code").GetString());

        var noSet = await PostAsync("/api/v1/dimension-values",
            new { setCode = "GHOST", value = "V", effectiveFrom = new DateOnly(2026, 1, 1) }, Fin);
        Assert.Equal(HttpStatusCode.NotFound, noSet.StatusCode);
        Assert.Equal("DIMENSION_SET_NOT_FOUND", (await Json(noSet)).GetProperty("error").GetProperty("code").GetString());

        var inverted = await PostAsync("/api/v1/dimension-values",
            new { setCode = "CostCenter", value = "CC-9", effectiveFrom = new DateOnly(2026, 5, 1), effectiveTo = new DateOnly(2026, 1, 1) }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);

        var dupValue = await PostAsync("/api/v1/dimension-values",
            new { setCode = "CostCenter", value = "CC-001", effectiveFrom = new DateOnly(2026, 1, 1) }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, dupValue.StatusCode);
        Assert.Equal("DIMENSION_VALUE_EXISTS", (await Json(dupValue)).GetProperty("error").GetProperty("code").GetString());

        var values = await GetAsync("/api/v1/dimension-values?setCode=CostCenter", Fin);
        Assert.Contains(values.GetProperty("data").EnumerateArray().ToList(),
            v => v.GetProperty("value").GetString() == "CC-001");
        // seeded set carries required=false (ruling: a REQUIRED seeded set would break every
        // existing journal-creating test) and the inactive-in-interval demo value
        var sets = await GetAsync("/api/v1/dimension-sets", Fin);
        var cc = sets.GetProperty("data").EnumerateArray().First(s => s.GetProperty("code").GetString() == "CostCenter");
        Assert.False(cc.GetProperty("required").GetBoolean());
    }

    // 016 — number-series admin view + void-with-reason; reuse per policy (voided numbers are
    // recorded, never reissued by the sequence service)
    [Fact]
    public async Task Number_Series_Admin_View_And_Void_With_Reason()
    {
        var list = await GetAsync("/api/v1/number-series", Fin);
        var data = list.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(data, s => s.GetProperty("prefix").GetString() == "J" && s.GetProperty("lastValue").GetInt64() >= 1);
        Assert.Contains(data, s => s.GetProperty("prefix").GetString() == "FXR");

        var noReason = await PostAsync("/api/v1/number-series/J/voids", new { number = "J-0001" }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var unknownPrefix = await PostAsync("/api/v1/number-series/ZZ/voids", new { number = "ZZ-0001", reason = "misprint" }, Fin);
        Assert.Equal(HttpStatusCode.NotFound, unknownPrefix.StatusCode);
        Assert.Equal("NUMBER_SERIES_NOT_FOUND", (await Json(unknownPrefix)).GetProperty("error").GetProperty("code").GetString());

        var voided = await PostAsync("/api/v1/number-series/J/voids", new { number = "J-9999", reason = "printed then destroyed" }, Fin);
        Assert.Equal(HttpStatusCode.Created, voided.StatusCode);
        var again = await PostAsync("/api/v1/number-series/J/voids", new { number = "J-9999", reason = "retry" }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("NUMBER_ALREADY_VOIDED", (await Json(again)).GetProperty("error").GetProperty("code").GetString());

        var after = await GetAsync("/api/v1/number-series", Fin);
        Assert.Equal(1, after.GetProperty("data").EnumerateArray()
            .First(s => s.GetProperty("prefix").GetString() == "J").GetProperty("voidedCount").GetInt32());

        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains(outbox.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("type").GetString()),
            t => t == "finance.number-series.voided");
    }

    // Brief §7 item 28 (QA Gate 2 pattern) — R-FIN2 maker (U-015) gets envelope errors on every
    // fx:write/finance-admin endpoint; R-FIN (U-003) succeeds; the maker path stays open.
    [Fact]
    public async Task Authz_Sweep_R_FIN2_Blocked_On_Finance_Admin_U003_Succeeds()
    {
        await EnsureRegion();
        async Task<JsonElement> Sweep(string path, object? body)
        {
            var res = await PostAsync(path, body, FinMaker);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            var body2 = await Json(res);
            Assert.False(body2.GetProperty("success").GetBoolean());
            Assert.Equal("FORBIDDEN", body2.GetProperty("error").GetProperty("code").GetString());
            return body2;
        }

        await Sweep("/api/v1/currencies", new { code = "NOP", name = "Nope", decimalScale = 2 });
        await Sweep("/api/v1/fx-rates", new { baseCurrency = "USD", quoteCurrency = "BDT", rateDate = new DateOnly(2026, 1, 1), rateType = "Spot", rate = 100m, source = "sweep" });
        await Sweep("/api/v1/fx-rates/FXR-0002/approvals", new { });
        await Sweep("/api/v1/finance-settings", new { key = "finance.functional_currency", value = "USD" });
        await Sweep("/api/v1/number-series/J/voids", new { number = "J-0002", reason = "sweep" });
        await Sweep("/api/v1/dimension-sets", new { code = "NOPE", name = "Nope", required = false });

        // the maker path is open: R-FIN2 carries journals:write (dated on/after any pending
        // functional-currency change this class may have recorded — see the 007 test)
        var nextMonthMid = MonthStart(Today).AddMonths(1).AddDays(14);
        var (made, _) = await CreateDraftAs(FinMaker, "r-fin2 maker probe", new[]
        {
            Line("1400", "Inventory", 5m, 0m, dimensions: Region),
            Line("2100", "Accounts Payable", 0m, 5m, dimensions: Region),
        }, nextMonthMid);
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);

        // R-FIN succeeds where the maker was blocked
        var okCurrency = await PostAsync("/api/v1/currencies", new { code = "SGP", name = "Singapore Dollar", decimalScale = 2 }, Fin);
        Assert.Equal(HttpStatusCode.Created, okCurrency.StatusCode);
    }

    private async Task<(HttpResponseMessage res, string journalId)> CreateDraftProbe(DateOnly date, params object[] lines) =>
        await CreateDraftAs(Fin, "p5 probe", lines, date);

    private async Task<(HttpResponseMessage res, string journalId)> CreateDraftAs(string token, string memo, object[] lines, DateOnly date)
    {
        var res = await PostAsync("/api/v1/journals", new { memo, lines, date }, token);
        if (res.StatusCode != HttpStatusCode.Created)
            Assert.Fail($"journal create {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (res, (await Json(res)).GetProperty("data").GetProperty("id").GetString()!);
    }

    // 016 fix round — concurrent double-void race: the AlreadyVoided pre-check is read-then-write,
    // so two racing voids of one number can both pass it. Deterministic barrier (house race form,
    // JournalRaceHardeningTests): an UNCOMMITTED void row for the same number is invisible to the
    // request's READ COMMITTED pre-check, so its INSERT parks on the unique index; committing the
    // barrier wakes it with a 23505 that must surface as 409 NUMBER_ALREADY_VOIDED — never a 500.
    // The FXR prefix keeps the J-series voidedCount assertion in the 016 test order-independent.
    [Fact]
    public async Task Concurrent_Void_Of_The_Same_Number_Is_A_Conflict_Not_A_Server_Error()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        await using var db = new ErpDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync();
        db.NumberSeriesVoids.Add(new Erp.Api.NumberSeriesVoid("NSV-RACE-BARRIER-FXR-9999", "T-001", "LE-001", "FXR", "FXR-9999",
            "barrier: parked so the HTTP void races the unique index", "U-001", DateTimeOffset.UtcNow));
        db.SaveChanges(); // inside the explicit transaction — deliberately not committed yet

        var httpVoid = PostAsync("/api/v1/number-series/FXR/voids", new { number = "FXR-9999", reason = "printed twice" }, Fin);
        await Task.Delay(2000); // the HTTP insert is now parked behind the barrier's index entry
        await tx.CommitAsync();

        var res = await httpVoid;
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("NUMBER_ALREADY_VOIDED", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    // 008 re-review F1 — concurrent approve race: the ApproveRate pre-check is check-then-act.
    // An UNCOMMITTED approved rate with the same key is invisible to the READ COMMITTED pre-check,
    // so the HTTP approval's UPDATE parks on the partial unique index; committing the barrier
    // wakes it with a 23505 that must surface as 409 FX_RATE_AMBIGUOUS — never a 500 INTERNAL.
    [Fact]
    public async Task Concurrent_Approve_Of_A_Competing_Fx_Rate_Is_A_Conflict_Not_A_Server_Error()
    {
        var fresh = await PostAsync("/api/v1/fx-rates", new
        {
            baseCurrency = "JPY", quoteCurrency = "BDT", rateDate = new DateOnly(2026, 2, 1),
            rateType = "Spot", rate = 0.82m, precision = 4, source = "p5-race-test",
        }, Fin);
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);
        var rateId = (await Json(fresh)).GetProperty("data").GetProperty("id").GetString()!;

        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        await using var db = new ErpDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync();
        db.FxRates.Add(new Erp.Api.FxRate("FXR-RACE-BARRIER", "T-001", "LE-001", "JPY", "BDT", new DateOnly(2026, 2, 1),
            Erp.Api.FxRateType.Spot, 0.81m, 4, "barrier", DateTimeOffset.UtcNow,
            Erp.Api.FxRateStatus.Approved, "system", "system", DateTimeOffset.UtcNow));
        db.SaveChanges(); // inside the explicit transaction — deliberately not committed yet

        var httpApprove = PostAsync($"/api/v1/fx-rates/{rateId}/approvals", new { }, Admin);
        await Task.Delay(2000); // the HTTP UPDATE is now parked behind the barrier's index entry
        await tx.CommitAsync();

        var res = await httpApprove;
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("FX_RATE_AMBIGUOUS", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }
}
