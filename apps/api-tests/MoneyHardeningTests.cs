using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Money-integrity hardening (T7 review follow-ups):
// 1. Aging buckets must age invoice-backed AR by the invoice's FROZEN DueDate (053 AC2), not the
//    global finance_settings.creditTermsDays — a NET60 customer's invoice must not bucket early;
//    the finance aging surface and the T7 collections worklist (which already uses DueDate) must
//    agree for every customer. Bucket boundaries stay R9b settings data; journal-only AR entries
//    (no invoice document exists) keep the terms-derived date.
// 2. JournalService execution races (the T7 fix-round-1 family): Approve from a stale Pending
//    snapshot must not double-post control lines (status concurrency token → 409
//    JOURNAL_ALREADY_POSTED, mirroring INVOICE_ALREADY_POSTED), and Reverse — multi-write
//    (reversal journal + receipt stock unwind) — must serialize check+commit on the source row
//    (SELECT … FOR UPDATE inside the request transaction, mirroring the exposure path).

namespace Erp.Api.Tests;

public class MoneyHardeningTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public MoneyHardeningTests(TestApiFactory factory) => _factory = factory;

    private const string Admin = "U-001";
    private const string Proc = "U-002";
    private const string Fin = "U-003";
    private const string Sales = "U-004";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

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

    private static string Tag(string prefix) => $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant()[..^8];

    private static JsonElement BucketOf(JsonElement aging, string label) =>
        aging.GetProperty("data").GetProperty("buckets").EnumerateArray()
            .First(b => b.GetProperty("label").GetString() == label);

    private static decimal StockOf(JsonElement stock, string sku) =>
        stock.GetProperty("data").EnumerateArray()
            .First(s => s.GetProperty("itemSku").GetString() == sku)
            .GetProperty("qtyOnHand").GetDecimal();

    private static JsonElement JournalById(JsonElement journals, string id) =>
        journals.GetProperty("data").EnumerateArray().First(j => j.GetProperty("id").GetString() == id);

    // ---- shared flow seeds (mirror the FinanceDeep/SalesDeep fixtures) ----------------

    private async Task<string> SeedStock(string sku, decimal qty, decimal price)
    {
        var res = await PostAsync("/api/v1/suppliers", new { name = $"Sup {sku}", taxId = $"TRX-{sku}", email = $"{sku.ToLowerInvariant()}@sup.test" }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = price } },
        }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, Proc)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, Proc)).StatusCode);
        return poId;
    }

    private async Task<string> SeedCustomer(int? paymentTermNetDays = null)
    {
        var tag = Tag("CUST");
        var payload = (object)new { name = $"Buyer {tag}", taxId = $"TRX-{tag}", email = $"ap.{tag.ToLowerInvariant()}@buyer.test" };
        if (paymentTermNetDays is { } days)
            payload = new { name = $"Buyer {tag}", taxId = $"TRX-{tag}", email = $"ap.{tag.ToLowerInvariant()}@buyer.test", paymentTermNetDays = days };
        var res = await PostAsync("/api/v1/customers", payload, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<(string customerId, string soId)> SeedShippedSo(string sku, decimal qty, decimal price, int? paymentTermNetDays = null)
    {
        var customerId = await SeedCustomer(paymentTermNetDays);
        var res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = price } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var soId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty }, Sales)).StatusCode);
        return (customerId, soId);
    }

    // shipped SO → posted invoice (drafter U-004 ≠ poster U-003, maker-checker)
    private async Task<(string customerId, string invoiceId, string journalId)> PostInvoiceFor(
        string sku, decimal qty, decimal price, int? paymentTermNetDays = null)
    {
        await SeedStock(sku, qty, 10m);
        var (customerId, soId) = await SeedShippedSo(sku, qty, price, paymentTermNetDays);
        var res = await PostAsync($"/api/v1/sales-orders/{soId}/invoice-drafts", new { }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var draftId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/invoices/{draftId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await Json(res)).GetProperty("data");
        var invoiceId = data.GetProperty("invoice").GetProperty("id").GetString()!;
        var journalId = data.GetProperty("journal").GetProperty("id").GetString()!;
        return (customerId, invoiceId, journalId);
    }

    private async Task<string> SeedReceiptJournal(string sku, decimal qty, decimal unitPrice)
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
        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, Proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("journal").GetProperty("id").GetString()!;
    }

    // expense + recoverable input VAT draft: approval appends the 1450 control line — the
    // duplicated-write damage of a stale second approval is observable on 1450, and the journal
    // touches no AR account (aging fixtures elsewhere stay unpolluted)
    private async Task<string> CreateTaxedDraft(string memo)
    {
        var res = await PostAsync("/api/v1/journals", new
        {
            memo,
            date = Today,
            lines = new object[]
            {
                new { accountCode = "6100", accountName = "Salaries and Wages Expense", debit = 1000m, credit = 0m, taxCodeId = "VAT-STD-BD" },
                new { accountCode = "2100", accountName = "Accounts Payable", debit = 0m, credit = 1150m },
            },
        }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static int VatInputControlLines(JsonElement journals, string journalId) =>
        JournalById(journals, journalId).GetProperty("lines").EnumerateArray()
            .Count(l => l.GetProperty("accountCode").GetString() == "1450");

    // ---- 1. aging ages by the frozen invoice DueDate (R-T7j follow-up) ---------------

    [Fact]
    public async Task Aging_Buckets_Use_The_Frozen_Invoice_DueDate_Not_Global_Credit_Terms()
    {
        // NET60 customer: invoice dated today, due TODAY+60, frozen at posting (053 AC2).
        // Probed at asOf = due+45 = today+105 it must land in 31-60 — NOT the 61-90 the global
        // 30-day credit terms produce (105-30 = 75). A NET30 invoice probed at ITS due+45
        // (today+75) stays 31-60 — its terms equal the global setting, so nothing moves.
        var asOf60 = Today.AddDays(105); // due60 + 45, known before posting (today + NET60)
        var asOf30 = Today.AddDays(75);  // due30 + 45
        var query60 = $"/api/v1/finance/receivables/aging?asOf={asOf60:yyyy-MM-dd}&basis=due";
        var query30 = $"/api/v1/finance/receivables/aging?asOf={asOf30:yyyy-MM-dd}&basis=due";

        // baselines BEFORE the fixtures (order independence within the class)
        var before60 = await Json(await GetRawAsync(query60, Fin));
        var before30 = await Json(await GetRawAsync(query30, Fin));

        var sku60 = Tag("NET60");
        var (customer60, invoice60, journal60) = await PostInvoiceFor(sku60, 2m, 50.00m, paymentTermNetDays: 60);
        var sku30 = Tag("NET30");
        var (_, invoice30, journal30) = await PostInvoiceFor(sku30, 1m, 40.00m, paymentTermNetDays: 30);

        // the documents carry the frozen due dates — the aging surface must agree with them
        var invoices = await GetAsync("/api/v1/invoices", Sales);
        var doc60 = invoices.GetProperty("data").EnumerateArray().First(i => i.GetProperty("id").GetString() == invoice60);
        var doc30 = invoices.GetProperty("data").EnumerateArray().First(i => i.GetProperty("id").GetString() == invoice30);
        var due60 = DateOnly.Parse(doc60.GetProperty("dueDate").GetString()!);
        var due30 = DateOnly.Parse(doc30.GetProperty("dueDate").GetString()!);
        Assert.Equal(Today.AddDays(60), due60);
        Assert.Equal(Today.AddDays(30), due30);

        var after60 = await Json(await GetRawAsync(query60, Fin));
        var after30 = await Json(await GetRawAsync(query30, Fin));

        // NET60 at due+45: 31-60 — today the full AR total (2 × 50 net + 15% VAT = 115.00) lands
        // in 61-90 instead. The 61-90 delta must be EXACTLY the NET30 invoice (due today+30 →
        // 75 days past due at this asOf), proving the NET60 invoice is not contributing there.
        Assert.Equal(115.00m, BucketOf(after60, "31-60").GetProperty("amount").GetDecimal()
            - BucketOf(before60, "31-60").GetProperty("amount").GetDecimal());
        Assert.Equal(46.00m, BucketOf(after60, "61-90").GetProperty("amount").GetDecimal()
            - BucketOf(before60, "61-90").GetProperty("amount").GetDecimal());

        // NET30 at due+45: unchanged — 31-60 both before and after the fix (1 × 40 + VAT = 46.00)
        Assert.Equal(46.00m, BucketOf(after30, "31-60").GetProperty("amount").GetDecimal()
            - BucketOf(before30, "31-60").GetProperty("amount").GetDecimal());

        // the worklist rows carry the FROZEN due dates and the settings boundaries still hold
        var wl60 = after60.GetProperty("data").GetProperty("worklist").EnumerateArray()
            .First(w => w.GetProperty("journalId").GetString() == journal60);
        Assert.Equal(due60, DateOnly.Parse(wl60.GetProperty("dueDate").GetString()!));
        Assert.Equal(45, wl60.GetProperty("daysPastDue").GetInt32());
        Assert.True(wl60.GetProperty("dunningReady").GetBoolean());
        Assert.Equal(customer60, wl60.GetProperty("customerId").GetString());
        Assert.Equal(30, after60.GetProperty("data").GetProperty("termsDays").GetInt32());

        var wl30 = after30.GetProperty("data").GetProperty("worklist").EnumerateArray()
            .First(w => w.GetProperty("journalId").GetString() == journal30);
        Assert.Equal(due30, DateOnly.Parse(wl30.GetProperty("dueDate").GetString()!));
        Assert.Equal(45, wl30.GetProperty("daysPastDue").GetInt32());
    }

    // ---- 2. sequential double approval (deterministic guard) -------------------------

    [Fact]
    public async Task Double_Approval_Second_Submission_Is_409_With_No_Second_Control_Line()
    {
        var journalId = await CreateTaxedDraft($"double approve guard {Tag("J")}");

        var res = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Posted", (await Json(res)).GetProperty("data").GetProperty("status").GetString());
        var countAfterFirst = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();
        Assert.Equal(1, VatInputControlLines(await GetAsync("/api/v1/journals", Admin), journalId));

        // the second, fresh-read submission is refused by the state check — and writes nothing
        res = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("JOURNAL_APPROVAL_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(countAfterFirst, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());
        Assert.Equal(1, VatInputControlLines(await GetAsync("/api/v1/journals", Admin), journalId));
    }

    // ---- 3. stale-snapshot approval race (the in-memory check cannot catch it) -------

    // T7 fix-round-1 family, mirrored: scope A holds a stale Pending snapshot while scope B
    // (HTTP) commits the approval, then A approves from its stale view. On a plain journal the
    // recomputation reproduces the same balance, so TODAY the second approval FULLY COMMITS —
    // a second approval audit event + a second finance.journal.approved outbox emission for one
    // journal. The status-guarded UPDATE (journals.status concurrency token) must make the stale
    // resubmission match ZERO rows → 409 JOURNAL_ALREADY_POSTED with nothing written. (On a
    // TAXED journal the stale recomputation instead re-taxes the committed control lines —
    // tax-on-tax — and dies in JOURNAL_UNBALANCED before Save; the disposition of a stale
    // resubmission must not depend on where a recomputation happens to diverge.)
    [Fact]
    public async Task Journal_Stale_Approval_Is_Rejected_By_The_Status_Guard()
    {
        // plain draft — no tax lines, so the stale recomputation would balance and commit
        var res = await PostAsync("/api/v1/journals", new
        {
            memo = $"stale approval race {Tag("J")}",
            date = Today,
            lines = new object[]
            {
                new { accountCode = "1400", accountName = "Inventory", debit = 250m, credit = 0m },
                new { accountCode = "2100", accountName = "Accounts Payable", debit = 0m, credit = 250m },
            },
        }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var journalId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // scope A: a second officer's context loads the journal BEFORE the race
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        await using var dbA = new ErpDbContext(options);
        var ecA = new EntityContext();
        var serviceA = new JournalService(new JournalRepository(dbA, ecA), new PurchaseOrderRepository(dbA, ecA),
            new InventoryRepository(dbA, ecA), new NumberSeries(dbA), new AuditRepository(dbA, new NumberSeries(dbA), ecA),
            new AccountRepository(dbA, ecA), new TaxCodeRepository(dbA, ecA), dbA, ecA);
        var stale = new JournalRepository(dbA, ecA).Find(journalId);
        Assert.Equal(Erp.Api.JournalStatus.Pending, stale!.Status); // scope A believes it is still pending

        // scope B: the first officer approves via HTTP and commits
        res = await PostAsync($"/api/v1/journals/{journalId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var countAfterFirst = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();
        var eventsAfterFirst = ApprovalEventsFor(journalId);

        // scope A: approves the SAME journal from its stale snapshot — the guard must refuse
        // and no second approval trail may be written
        var approver = new Erp.Api.CurrentUser("U-001", "admin@teamzlab.dev", "Tenant Admin", "R-ADMIN", new[] { "*" });
        var (data, code, _) = serviceA.Approve(approver, journalId, null);
        Assert.Equal("JOURNAL_ALREADY_POSTED", code);
        Assert.Null(data);
        Assert.Equal(countAfterFirst, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());
        Assert.Equal(eventsAfterFirst, ApprovalEventsFor(journalId));
    }

    private int ApprovalEventsFor(string journalId)
    {
        var outbox = GetAsync("/api/v1/outbox", Admin).GetAwaiter().GetResult();
        return outbox.GetProperty("data").EnumerateArray()
            .Count(e => e.GetProperty("type").GetString() == "finance.journal.approved"
                     && e.GetProperty("payloadJson").GetString()!.Contains(journalId, StringComparison.Ordinal));
    }

    // ---- 4. reversal from a stale snapshot (deterministic guard) ----------------------

    // The stale-snapshot form of the double-reverse race: scope A holds a Posted source with no
    // reversal visible, scope B (HTTP) reverses and commits, then A reverses from its stale
    // view. The HasReversal existence check is a FRESH database query (not the tracked
    // snapshot), so this must be refused with stock untouched — and it stays refused once the
    // check moves INSIDE the request transaction after the source-row FOR UPDATE (the fix for
    // the true concurrent window, where both callers pass the check before either commits; a
    // true two-client race is flaky and rejected as evidence per the T7 fix-round convention).
    [Fact]
    public async Task Journal_Reversal_From_A_Stale_Snapshot_Is_Refused_And_Stock_Is_Untouched()
    {
        const decimal qty = 5m;
        var sku = Tag("MHSTALE");
        var journalId = await SeedReceiptJournal(sku, qty, 4.00m);

        // scope A: a second officer's context loads the source BEFORE the race
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(_factory.ConnectionString).Options;
        await using var dbA = new ErpDbContext(options);
        var ecA = new EntityContext();
        var serviceA = new JournalService(new JournalRepository(dbA, ecA), new PurchaseOrderRepository(dbA, ecA),
            new InventoryRepository(dbA, ecA), new NumberSeries(dbA), new AuditRepository(dbA, new NumberSeries(dbA), ecA),
            new AccountRepository(dbA, ecA), new TaxCodeRepository(dbA, ecA), dbA, ecA);
        var stale = new JournalRepository(dbA, ecA).Find(journalId);
        Assert.Equal(Erp.Api.JournalStatus.Posted, stale!.Status);
        Assert.Null(stale.ReversalOfId); // scope A sees no reversal

        // scope B: the first officer reverses via HTTP and commits — stock unwinds once
        var res = await PostAsync($"/api/v1/journals/{journalId}/reversals", new { }, Fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var stockAfterFirst = StockOf(await GetAsync("/api/v1/stock", Admin), sku);
        Assert.Equal(0m, stockAfterFirst);
        var countAfterFirst = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();

        // scope A: reverses the SAME source from its stale snapshot — refused, nothing written
        var userA = new Erp.Api.CurrentUser("U-001", "admin@teamzlab.dev", "Tenant Admin", "R-ADMIN", new[] { "*" });
        var (reversal, code, message) = serviceA.Reverse(userA, journalId);
        Assert.Null(reversal);
        Assert.Equal("JOURNAL_ALREADY_REVERSED", code);
        Assert.Contains(journalId, message);
        Assert.Equal(countAfterFirst, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());
        Assert.Equal(stockAfterFirst, StockOf(await GetAsync("/api/v1/stock", Admin), sku));
    }
}
