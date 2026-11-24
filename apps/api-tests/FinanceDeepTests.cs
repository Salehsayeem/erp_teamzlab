using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

// Task-5 FINANCE deep slice (ERP-FINANCE-001/002/004/005/009/011/013/031/032/035 + 063 trimmed):
// fiscal periods with close/lock + centralized posting guard, AR aging buckets, effective-dated
// tax revisions resolved at journal approval, chart-of-accounts basics.
// xUnit v2 runs tests alphabetically; earlier tests must not poison later fixtures, so the
// destructive leg (closing the CURRENT month, which flips every posting-at-today path to
// PERIOD_LOCKED) sits at the tail of the last test that needs it.

namespace Erp.Api.Tests;

public class FinanceDeepTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public FinanceDeepTests(TestApiFactory factory) => _factory = factory;

    private const string Admin = "U-001";
    private const string Proc = "U-002";
    private const string Fin = "U-003";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

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

    private static object Line(string code, string name, decimal dr, decimal cr, string? tax = null) =>
        new { accountCode = code, accountName = name, debit = dr, credit = cr, taxCodeId = tax };

    // seeds supplier + approved PO so a receipt can be posted against it
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

    // creates a draft journal as finance officer; date is the journal's accounting date
    private async Task<(HttpResponseMessage res, string journalId)> CreateDraft(string memo, object[] lines, DateOnly date)
    {
        var res = await PostAsync("/api/v1/journals", new { memo, lines, date }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (res, (await Json(res)).GetProperty("data").GetProperty("id").GetString()!);
    }

    private async Task<string> CreatePeriod(int monthsAhead, string tag)
    {
        var start = MonthStart(Today).AddMonths(monthsAhead);
        var name = $"{start:yyyy-MM}-{tag}";
        var res = await PostAsync("/api/v1/fiscal-periods", new
        {
            name,
            startDate = start,
            endDate = start.AddMonths(1).AddDays(-1),
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<string> PeriodContaining(DateOnly date)
    {
        var list = await GetAsync("/api/v1/fiscal-periods", Admin);
        return list.GetProperty("data").EnumerateArray()
            .First(p => DateOnly.Parse(p.GetProperty("startDate").GetString()!) <= date
                     && DateOnly.Parse(p.GetProperty("endDate").GetString()!) >= date)
            .GetProperty("id").GetString()!;
    }

    private static JsonElement JournalById(JsonElement journals, string id) =>
        journals.GetProperty("data").EnumerateArray().First(j => j.GetProperty("id").GetString() == id);

    private static JsonElement LineOf(JsonElement journal, string accountCode) =>
        journal.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("accountCode").GetString() == accountCode);

    private static decimal StockOf(JsonElement stock, string sku) =>
        stock.GetProperty("data").EnumerateArray()
            .First(s => s.GetProperty("itemSku").GetString() == sku)
            .GetProperty("qtyOnHand").GetDecimal();

    // Fix round 1 — reversing a source twice posted two reversal journals and unwound receipt
    // stock twice; a retry/double-click must hit JOURNAL_ALREADY_REVERSED with nothing written
    [Fact]
    public async Task Double_Reversal_Of_Source_Returns_409_JOURNAL_ALREADY_REVERSED()
    {
        var poId = await SeedApprovedPo("DBLREV-1", 8m, 3.00m);
        var res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "DBLREV-1", qty = 8m }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var journalId = (await Json(res)).GetProperty("data").GetProperty("journal").GetProperty("id").GetString()!;

        res = await PostAsync($"/api/v1/journals/{journalId}/reversals", new { }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var stockAfterFirst = StockOf(await GetAsync("/api/v1/stock", Admin), "DBLREV-1");
        var countAfterFirst = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();

        res = await PostAsync($"/api/v1/journals/{journalId}/reversals", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("JOURNAL_ALREADY_REVERSED", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(journalId, body.GetProperty("error").GetProperty("message").GetString());

        // the rejected second reversal wrote nothing: no second reversal journal, no stock change
        Assert.Equal(countAfterFirst, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());
        Assert.Equal(stockAfterFirst, StockOf(await GetAsync("/api/v1/stock", Admin), "DBLREV-1"));
    }

    // 005 AC1 — the centralized period lock rejects approvals AND receipts into closed months
    [Fact]
    public async Task Posting_Into_Closed_Period_Returns_409_PERIOD_LOCKED()
    {
        // journal leg: draft dated inside the seeded closed month; approval must be blocked
        var closed = await GetAsync("/api/v1/fiscal-periods?status=Closed", Admin);
        var period = closed.GetProperty("data").EnumerateArray().First();
        var periodName = period.GetProperty("name").GetString()!;
        var inPeriod = DateOnly.Parse(period.GetProperty("startDate").GetString()!).AddDays(3);
        var (_, journalId) = await CreateDraft($"closed-period probe {periodName}",
            new[] { Line("1400", "Inventory", 10m, 0m), Line("2100", "Accounts Payable", 0m, 10m) }, inPeriod);

        var res = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("PERIOD_LOCKED", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(periodName, body.GetProperty("error").GetProperty("message").GetString());

        // receipt leg: close the CURRENT month (no pending drafts sit in it — earlier tests left
        // theirs in prior/future months), then a receipt — which posts at today — must be rejected
        var poId = await SeedApprovedPo("PCLOSE-1", 4m, 2.00m);
        var currentId = await PeriodContaining(Today);
        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/api/v1/fiscal-periods/{currentId}/close", new { reason = "suite lock" }, Admin)).StatusCode);
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "PCLOSE-1", qty = 4m }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("PERIOD_LOCKED", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    // 005 AC2 — reopen demands a reason and leaves an audit event + outbox message
    [Fact]
    public async Task Period_Reopen_Without_Reason_400_And_With_Reason_Audits()
    {
        var periodId = await CreatePeriod(3, "reopen");

        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/api/v1/fiscal-periods/{periodId}/close", new { reason = (string?)null }, Admin)).StatusCode);

        // reopen without a reason → 400 before anything changes
        var res = await PostAsync($"/api/v1/fiscal-periods/{periodId}/reopen", new { }, Admin);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        res = await PostAsync($"/api/v1/fiscal-periods/{periodId}/reopen", new { reason = "late vendor bill correction" }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await Json(res)).GetProperty("data");
        Assert.Equal("Open", data.GetProperty("status").GetString());
        Assert.Equal("late vendor bill correction", data.GetProperty("reopenReason").GetString());

        var events = await GetAsync("/api/v1/audit-events", Admin);
        Assert.Contains(events.GetProperty("data").EnumerateArray(),
            e => e.GetProperty("action").GetString() == "reopen" && e.GetProperty("entityId").GetString() == periodId);
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains(outbox.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("type").GetString()),
            t => t == "finance.period.reopened");
    }

    // 063 AC1 (trimmed) — close gate names the blocking draft journals
    [Fact]
    public async Task Period_Close_With_Draft_Journal_Returns_409_CLOSE_GATE_BLOCKED()
    {
        var periodId = await CreatePeriod(2, "gate");
        var start = MonthStart(Today).AddMonths(2);
        var (_, draftId) = await CreateDraft($"gate probe {periodId}",
            new[] { Line("5100", "Cost of Goods Sold", 12m, 0m), Line("2100", "Accounts Payable", 0m, 12m) },
            start.AddDays(10));

        var res = await PostAsync($"/api/v1/fiscal-periods/{periodId}/close", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("CLOSE_GATE_BLOCKED", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(draftId, body.GetProperty("error").GetProperty("message").GetString());
    }

    // 013 AC1 — maker-checker: the preparer cannot approve their own journal
    [Fact]
    public async Task Journal_Approval_By_Preparer_Returns_409()
    {
        var prior = MonthStart(Today).AddMonths(-1).AddDays(14);
        var (_, journalId) = await CreateDraft("maker-checker probe",
            new[] { Line("4000", "Sales Revenue", 50m, 0m), Line("2100", "Accounts Payable", 0m, 50m) }, prior);

        var res = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("JOURNAL_APPROVAL_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Maker-checker", body.GetProperty("error").GetProperty("message").GetString());

        // a different approver posts it
        res = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Posted", (await Json(res)).GetProperty("data").GetProperty("status").GetString());
    }

    // 009 AC1 — a one-unit imbalance is rejected with the difference shown
    [Fact]
    public async Task One_Unit_Unbalanced_Journal_Approval_Shows_Difference()
    {
        var prior = MonthStart(Today).AddMonths(-1).AddDays(14);
        var (_, journalId) = await CreateDraft("one-unit imbalance",
            new[] { Line("5100", "Cost of Goods Sold", 100.00m, 0m), Line("4000", "Sales Revenue", 0m, 99.00m) }, prior);

        var res = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("JOURNAL_UNBALANCED", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("1.00", body.GetProperty("error").GetProperty("message").GetString());

        // rejected approval leaves the draft untouched
        var journals = await GetAsync("/api/v1/journals", Admin);
        Assert.Equal("Pending", JournalById(journals, journalId).GetProperty("status").GetString());
    }

    // 011 AC1/AC2 — same key + same payload replays the reference; same key + different payload conflicts
    [Fact]
    public async Task Same_Idem_Key_Same_Payload_Replays_Reference_Different_Payload_409()
    {
        var prior = MonthStart(Today).AddMonths(-1).AddDays(14);
        object Payload(string memo) => new
        {
            memo,
            date = prior,
            lines = new[] { Line("1400", "Inventory", 5m, 0m), Line("2100", "Accounts Payable", 0m, 5m) },
        };
        var key = $"fin-idem-{Guid.NewGuid():N}";

        var res = await PostAsync("/api/v1/journals", Payload("idem original"), Fin, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var journalId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        var countAfterFirst = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();

        // AC1: identical retry replays the original reference — one journal, no second write
        res = await PostAsync("/api/v1/journals", Payload("idem original"), Fin, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(journalId, (await Json(res)).GetProperty("data").GetProperty("id").GetString());
        Assert.Equal(countAfterFirst, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());

        // AC2: same key, different payload → conflict, still no second journal
        res = await PostAsync("/api/v1/journals", Payload("idem tampered"), Fin, key);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("IDEMPOTENCY_CONFLICT", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(countAfterFirst, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());
    }

    // 031 AC1 — a fixed-as-of fixture of due dates 0/45/75/120 lands in the configured buckets
    [Fact]
    public async Task Aging_Fixture_At_Fixed_AsOf_Lands_Buckets_With_Open_Totals()
    {
        var asOf = Today;
        decimal[] amounts = [100m, 200m, 300m, 400m];
        int[] daysPastDue = [0, 45, 75, 120];
        for (var i = 0; i < 4; i++)
        {
            // due date = invoice date + 30-day terms (seeded settings) → back-date the invoice
            var invoiceDate = asOf.AddDays(-(30 + daysPastDue[i]));
            var (_, journalId) = await CreateDraft($"aging fixture {daysPastDue[i]}d",
                new[] { Line("1200", "Accounts Receivable", amounts[i], 0m), Line("4000", "Sales Revenue", 0m, amounts[i]) },
                invoiceDate);
            Assert.Equal(HttpStatusCode.OK,
                (await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        }

        var agingRes = await GetRawAsync($"/api/v1/finance/receivables/aging?asOf={asOf:yyyy-MM-dd}&basis=due", Admin);
        Assert.Equal(HttpStatusCode.OK, agingRes.StatusCode);
        var aging = await Json(agingRes);
        var buckets = aging.GetProperty("data").GetProperty("buckets");
        Assert.Equal(4, buckets.GetArrayLength());
        Assert.Equal("0-30", buckets[0].GetProperty("label").GetString());
        Assert.Equal(100.00m, buckets[0].GetProperty("amount").GetDecimal());
        Assert.Equal("31-60", buckets[1].GetProperty("label").GetString());
        Assert.Equal(200.00m, buckets[1].GetProperty("amount").GetDecimal());
        Assert.Equal("61-90", buckets[2].GetProperty("label").GetString());
        Assert.Equal(300.00m, buckets[2].GetProperty("amount").GetDecimal());
        Assert.Equal("90+", buckets[3].GetProperty("label").GetString());
        Assert.Equal(400.00m, buckets[3].GetProperty("amount").GetDecimal());
        Assert.Equal(1000.00m, aging.GetProperty("data").GetProperty("totalOpen").GetDecimal());

        // dunning-ready worklist: everything overdue past the first boundary is flagged
        var worklist = aging.GetProperty("data").GetProperty("worklist");
        Assert.Equal(4, worklist.GetArrayLength());
        Assert.False(worklist.EnumerateArray().First(w => w.GetProperty("daysPastDue").GetInt32() == 0).GetProperty("dunningReady").GetBoolean());
        Assert.True(worklist.EnumerateArray().First(w => w.GetProperty("daysPastDue").GetInt32() == 120).GetProperty("dunningReady").GetBoolean());
    }

    // 032 AC1 — the revision effective at the journal's accounting date selects the rate
    [Fact]
    public async Task Tax_Revision_By_Accounting_Date_Selects_Correct_Rate()
    {
        var p5 = await CreatePeriod(5, "tax");
        var p6 = await CreatePeriod(6, "tax");
        var start5 = MonthStart(Today).AddMonths(5);
        var start6 = MonthStart(Today).AddMonths(6);

        var res = await PostAsync("/api/v1/tax-codes", new
        {
            code = "VAT-T8",
            ratePct = 10m,
            basis = "Standard",
            recoverable = true,
            effectiveFrom = start5,
            effectiveTo = start5.AddMonths(1).AddDays(-1),
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var revision1 = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/tax-codes", new
        {
            code = "VAT-T8",
            ratePct = 20m,
            basis = "Standard",
            recoverable = true,
            effectiveFrom = start6,
            effectiveTo = (DateOnly?)null,
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // the submission always cites the first revision id — approval resolves BY CODE + DATE;
        // the AR side is funded at 100 + tax so the journal balances after the control line
        async Task<JsonElement> TaxedJournal(DateOnly date, decimal ratePct)
        {
            var tax = decimal.Round(100m * ratePct / 100m, 2);
            var (_, journalId) = await CreateDraft($"tax revision probe {date:yyyy-MM}",
                new[] { Line("1200", "Accounts Receivable", 100m + tax, 0m), Line("4000", "Sales Revenue", 0m, 100m, revision1) }, date);
            var approved = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            return await Json(approved);
        }

        var inP5 = await TaxedJournal(start5.AddDays(10), 10m);
        Assert.Equal(10.00m, LineOf(inP5.GetProperty("data"), "2200").GetProperty("credit").GetDecimal());
        Assert.Equal(-10.00m, LineOf(inP5.GetProperty("data"), "4000").GetProperty("taxAmount").GetDecimal());

        var inP6 = await TaxedJournal(start6.AddDays(10), 20m);
        Assert.Equal(20.00m, LineOf(inP6.GetProperty("data"), "2200").GetProperty("credit").GetDecimal());
        Assert.Equal(-20.00m, LineOf(inP6.GetProperty("data"), "4000").GetProperty("taxAmount").GetDecimal());
    }

    // 032 AC2 — publishing an overlapping effective range for the same code is rejected
    [Fact]
    public async Task Overlapping_Tax_Code_Range_Returns_409_TAX_CODE_OVERLAP()
    {
        _ = await CreatePeriod(7, "taxov");
        var start = MonthStart(Today).AddMonths(7);

        var res = await PostAsync("/api/v1/tax-codes", new
        {
            code = "VAT-T9",
            ratePct = 5m,
            basis = "Standard",
            recoverable = true,
            effectiveFrom = start,
            effectiveTo = (DateOnly?)null,
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        res = await PostAsync("/api/v1/tax-codes", new
        {
            code = "VAT-T9",
            ratePct = 7m,
            basis = "Standard",
            recoverable = true,
            effectiveFrom = start.AddDays(5),
            effectiveTo = (DateOnly?)null,
        }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("TAX_CODE_OVERLAP", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        var codes = await GetAsync("/api/v1/tax-codes", Admin);
        Assert.Equal(1, codes.GetProperty("data").EnumerateArray().Count(c => c.GetProperty("code").GetString() == "VAT-T9"));
    }

    // 035 AC1 golden — 1000.00 basis at the seeded 15% → 150.00 control line, balanced Dr/Cr proof
    [Fact]
    public async Task Journal_Line_Tax_Posts_Balanced_Control_Line_2200()
    {
        var prior = MonthStart(Today).AddMonths(-1).AddDays(14);

        // output tax: revenue-side line → control credit on 2200 VAT Payable (tax cited by CODE)
        var (_, outputId) = await CreateDraft("output VAT golden",
            new[] { Line("1200", "Accounts Receivable", 1150m, 0m), Line("4000", "Sales Revenue", 0m, 1000m, "VAT-STD-BD") }, prior);
        var res = await PostAsync($"/api/v1/journals/{outputId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var journal = (await Json(res)).GetProperty("data");
        Assert.Equal(-150.00m, LineOf(journal, "4000").GetProperty("taxAmount").GetDecimal());
        Assert.Equal("TC-VAT-STD-BD-V1", LineOf(journal, "4000").GetProperty("taxCodeId").GetString());
        Assert.Equal(150.00m, LineOf(journal, "2200").GetProperty("credit").GetDecimal());
        Assert.Equal(1150.00m, journal.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("debit").GetDecimal()));
        Assert.Equal(1150.00m, journal.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("credit").GetDecimal()));

        // recoverable input tax: expense-side line → control debit on 1450 Input VAT Recoverable
        var (_, inputId) = await CreateDraft("input VAT golden",
            new[] { Line("6100", "Salaries and Wages Expense", 1000m, 0m, "VAT-STD-BD"), Line("2100", "Accounts Payable", 0m, 1150m) }, prior);
        res = await PostAsync($"/api/v1/journals/{inputId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        journal = (await Json(res)).GetProperty("data");
        Assert.Equal(150.00m, LineOf(journal, "1450").GetProperty("debit").GetDecimal());
        Assert.Equal(1150.00m, journal.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("debit").GetDecimal()));
        Assert.Equal(1150.00m, journal.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("credit").GetDecimal()));
    }
}
