using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

// Task-7 SALES deep slice (ERP-SALES-003/004/005/007/008/015/016/017/042/043/044/045/046/048/049/
// 052/053/057/058/059): leads + conversion + duplicate detection, CRM interactions/cases,
// opportunities with governed transitions, invoice documents with per-line tax + maker-checker
// posting, credit/debit adjustments, collections worklist/reminders/disputes, credit exposure
// with SO holds + override. Controller rulings R-T7a..l are binding (see task-7-brief-draft.md).
// Test data uses unique SKUs/tax ids per test — the class fixture shares one throwaway database.

namespace Erp.Api.Tests;

public class SalesDeepTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public SalesDeepTests(TestApiFactory factory) => _factory = factory;

    private const string Admin = "U-001";
    private const string Fin = "U-003";
    private const string Sales = "U-004";

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

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static string Tag(string prefix) => $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant()[..^8];

    // ---- shared flow seeds -----------------------------------------------------------

    // supplier + approved PO + receipt → stock exists for the sku
    private async Task SeedStock(string sku, decimal qty, decimal price)
    {
        var res = await PostAsync("/api/v1/suppliers", new { name = $"Sup {sku}", taxId = $"TRX-{sku}", email = $"{sku.ToLowerInvariant()}@sup.test" }, "U-002");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = price } },
        }, "U-002");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, "U-002")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, "U-002")).StatusCode);
    }

    private async Task<string> SeedCustomer(string? creditLimit = null, string? taxId = null)
    {
        var tag = Tag("CUST");
        var payload = (object)new { name = $"Buyer {tag}", taxId = taxId ?? $"TRX-{tag}", email = $"ap.{tag.ToLowerInvariant()}@buyer.test" };
        if (creditLimit is not null) payload = new { name = $"Buyer {tag}", taxId = taxId ?? $"TRX-{tag}", email = $"ap.{tag.ToLowerInvariant()}@buyer.test", creditLimit };
        var res = await PostAsync("/api/v1/customers", payload, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    // customer + confirmed SO (approved by admin — sales cannot clear its own maker-checker)
    private async Task<(string customerId, string soId)> SeedConfirmedSo(string sku, decimal qty, decimal price, string? customerId = null, string? creditLimit = null)
    {
        customerId ??= await SeedCustomer(creditLimit);
        var res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = price } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var soId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        return (customerId, soId);
    }

    private async Task<string> SeedShippedSo(string sku, decimal qty, decimal price, string? creditLimit = null)
    {
        var (_, soId) = await SeedConfirmedSo(sku, qty, price, creditLimit: creditLimit);
        var res = await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return soId;
    }

    // shipped SO → invoice draft (editor = sales); invoiceDate null = today
    private async Task<JsonElement> DraftInvoice(string soId, DateOnly? invoiceDate = null)
    {
        var body = (object)(invoiceDate is null ? new { } : new { invoiceDate });
        var res = await PostAsync($"/api/v1/sales-orders/{soId}/invoice-drafts", body, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data");
    }

    private async Task<(string invoiceId, JsonElement journal)> PostInvoice(string invoiceId, string token, string? idemKey = null)
    {
        var res = await PostAsync($"/api/v1/invoices/{invoiceId}/postings", new { }, token, idemKey);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await Json(res)).GetProperty("data");
        return (data.GetProperty("invoice").GetProperty("id").GetString()!, data.GetProperty("journal"));
    }

    private async Task<(string invoiceId, decimal total)> SeedPostedInvoice(decimal qty, decimal price, DateOnly? invoiceDate = null, string? creditLimit = null)
    {
        var sku = Tag("SKU");
        await SeedStock(sku, qty, 10m);
        var soId = await SeedShippedSo(sku, qty, price, creditLimit);
        var draft = await DraftInvoice(soId, invoiceDate);
        var (invoiceId, _) = await PostInvoice(draft.GetProperty("id").GetString()!, Fin);
        var inv = await GetInvoice(invoiceId);
        return (invoiceId, inv.GetProperty("total").GetDecimal());
    }

    private async Task<JsonElement> GetInvoice(string invoiceId)
    {
        var invoices = await GetAsync("/api/v1/invoices", Sales); // invoices:read lives on R-SALES
        return invoices.GetProperty("data").EnumerateArray()
            .First(i => i.GetProperty("id").GetString() == invoiceId);
    }

    private async Task<string> SeedLead(string? taxId = null)
    {
        var tag = Tag("LEAD");
        var res = await PostAsync("/api/v1/leads", new
        {
            company = $"Prospect {tag}",
            contactName = "Amara Chowdhury",
            email = $"amara.{tag.ToLowerInvariant()}@prospect.test",
            phone = "+8801700000000",
            source = "Web",
            sourceRef = $"lp/{tag}",
            taxId = taxId,
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var leadId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Working" }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Qualified" }, Sales)).StatusCode);
        return leadId;
    }

    // ---- 1. leads: conversion + idempotency (ERP-SALES-004 AC2) ----------------------

    [Fact]
    public async Task Lead_Conversion_Same_Key_Replay_Returns_Same_Customer_And_Opportunity()
    {
        var leadId = await SeedLead();
        var key = $"conv-{Guid.NewGuid():N}";

        var res = await PostAsync($"/api/v1/leads/{leadId}/conversions", new { }, Sales, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var first = await Json(res);
        var customerId = first.GetProperty("data").GetProperty("customer").GetProperty("id").GetString()!;
        var opportunityId = first.GetProperty("data").GetProperty("opportunity").GetProperty("id").GetString()!;
        Assert.Equal(leadId, first.GetProperty("data").GetProperty("opportunity").GetProperty("leadId").GetString());

        // same key replays the SAME records — no second customer/opportunity
        res = await PostAsync($"/api/v1/leads/{leadId}/conversions", new { }, Sales, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var replay = await Json(res);
        Assert.Equal(customerId, replay.GetProperty("data").GetProperty("customer").GetProperty("id").GetString());
        Assert.Equal(opportunityId, replay.GetProperty("data").GetProperty("opportunity").GetProperty("id").GetString());

        var lead = (await GetAsync("/api/v1/leads", Sales)).GetProperty("data").EnumerateArray()
            .First(l => l.GetProperty("id").GetString() == leadId);
        Assert.Equal("Converted", lead.GetProperty("status").GetString());
    }

    // ---- 2. duplicate detection on normalized tax id (ERP-SALES-005 AC1/AC2) ---------

    [Fact]
    public async Task Lead_Conversion_With_Duplicate_TaxId_Returns_409_CUSTOMER_DUPLICATE()
    {
        var taxId = $"TRX-DUP-{Tag("X")}";
        var existingCustomerId = await SeedCustomer(taxId: taxId);
        var leadId = await SeedLead(taxId: taxId.ToLowerInvariant()); // normalized exact match

        var res = await PostAsync($"/api/v1/leads/{leadId}/conversions", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("CUSTOMER_DUPLICATE", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(existingCustomerId, body.GetProperty("error").GetProperty("message").GetString());

        // never auto-merge: the lead stays Qualified, no customer was created from it
        var lead = (await GetAsync("/api/v1/leads", Sales)).GetProperty("data").EnumerateArray()
            .First(l => l.GetProperty("id").GetString() == leadId);
        Assert.Equal("Qualified", lead.GetProperty("status").GetString());
    }

    // ---- 3. interaction timeline (ERP-SALES-007 AC1) ---------------------------------

    [Fact]
    public async Task Interaction_Timeline_Orders_By_OccurredAt_Not_RecordedAt()
    {
        var customerId = await SeedCustomer();
        var now = DateTimeOffset.UtcNow;

        // first RECORDED interaction HAPPENED earlier — order must follow OccurredAt
        var res = await PostAsync("/api/v1/crm/interactions", new
        {
            partyType = "Customer", partyId = customerId, channel = "Call",
            subject = "earlier call", occurredAt = now.AddDays(-2),
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var firstId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/crm/interactions", new
        {
            partyType = "Customer", partyId = customerId, channel = "Email",
            subject = "later email", occurredAt = now.AddDays(-1),
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var timeline = await GetAsync($"/api/v1/crm/interactions?partyType=Customer&partyId={customerId}", Sales);
        var rows = timeline.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(firstId, rows[0].GetProperty("id").GetString()); // occurred earlier, recorded earlier
        Assert.True(rows[0].GetProperty("occurredAt").GetDateTimeOffset() < rows[1].GetProperty("occurredAt").GetDateTimeOffset());
        // both timestamps shown separately (007 AC1)
        Assert.True(rows[0].GetProperty("recordedAt").GetDateTimeOffset() <= rows[1].GetProperty("recordedAt").GetDateTimeOffset());
    }

    // ---- 4. CRM cases: overdue filter + resolution needs text (ERP-SALES-008) --------

    [Fact]
    public async Task Crm_Case_Overdue_Filter_And_Resolution_Requires_Resolution_Text()
    {
        var customerId = await SeedCustomer();
        var res = await PostAsync("/api/v1/crm/cases", new { customerId, category = "Billing question", priority = "High" }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var caseData = (await Json(res)).GetProperty("data");
        var caseId = caseData.GetProperty("id").GetString()!;
        Assert.Equal("Open", caseData.GetProperty("status").GetString());
        Assert.True(caseData.GetProperty("responseDueAt").GetDateTimeOffset() > DateTimeOffset.UtcNow); // computed from priority

        // a fresh case is not yet overdue — the filter excludes it (boundary holds)
        var overdue = await GetAsync("/api/v1/crm/cases?overdue=true", Sales);
        Assert.DoesNotContain(overdue.GetProperty("data").EnumerateArray(),
            c => c.GetProperty("id").GetString() == caseId);

        // Resolved requires resolution text → 400 before anything changes
        res = await PostAsync($"/api/v1/crm/cases/{caseId}/transitions", new { status = "Resolved" }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/crm/cases/{caseId}/transitions", new { status = "InProgress" }, Sales)).StatusCode);
        res = await PostAsync($"/api/v1/crm/cases/{caseId}/transitions", new { status = "Resolved", resolution = "Credit note issued" }, Sales);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Credit note issued", (await Json(res)).GetProperty("data").GetProperty("resolution").GetString());
    }

    // ---- 5. closed-won needs an order link (ERP-SALES-016 AC2) ------------------------

    [Fact]
    public async Task Opportunity_Closed_Won_Without_Order_Link_Returns_409()
    {
        var customerId = await SeedCustomer();
        var oppId = await CreateOpportunity(customerId);

        var res = await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new { status = "Won", stage = "Closed" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OPPORTUNITY_WON_REQUIRES_ORDER", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // with a confirmed SO link it lands
        var (_, soId) = await SeedConfirmedSo(Tag("SKU"), 1m, 10m, customerId);
        res = await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new { status = "Won", stage = "Closed", orderId = soId }, Sales);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var won = (await Json(res)).GetProperty("data");
        Assert.Equal("Won", won.GetProperty("status").GetString());
        Assert.Equal(soId, won.GetProperty("orderId").GetString());
    }

    // ---- 6. opportunity totals + lost needs reason (ERP-SALES-017 AC1, 019 AC2) -------

    [Fact]
    public async Task Opportunity_Total_Sums_Lines_And_Lost_Requires_Reason()
    {
        var customerId = await SeedCustomer();
        var oppId = await CreateOpportunity(customerId);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/opportunities/{oppId}/lines", new { itemSku = "A", description = "Alpha", qty = 2m, targetPrice = 100m }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/opportunities/{oppId}/lines", new { itemSku = "B", description = "Beta", qty = 3m, targetPrice = 50.50m }, Sales)).StatusCode);

        var opps = await GetAsync("/api/v1/opportunities", Sales);
        var opp = opps.GetProperty("data").EnumerateArray().First(o => o.GetProperty("id").GetString() == oppId);
        Assert.Equal(351.50m, opp.GetProperty("total").GetDecimal()); // Σ Round(qty × targetPrice, 2)

        // 019 AC2: Lost without a loss reason is rejected
        var res = await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new { status = "Lost", stage = "Closed" }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        res = await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new { status = "Lost", stage = "Closed", lossReason = "Price", competitor = "RivalCo" }, Sales);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var lost = (await Json(res)).GetProperty("data");
        Assert.Equal("Price", lost.GetProperty("lossReason").GetString());
        Assert.Equal("RivalCo", lost.GetProperty("competitor").GetString());
    }

    // ---- 7. no double billing (ERP-SALES-042 AC2) -------------------------------------

    [Fact]
    public async Task Double_Invoice_For_Same_Shipment_Returns_409_ALREADY_BILLED()
    {
        var sku = Tag("SKU");
        await SeedStock(sku, 10m, 5m);
        var soId = await SeedShippedSo(sku, 10m, 100m);

        var draft = await DraftInvoice(soId);
        var sourceEventId = draft.GetProperty("sourceEventId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(sourceEventId));

        // a second draft for the same shipment source event is refused
        var res = await PostAsync($"/api/v1/sales-orders/{soId}/invoice-drafts", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("ALREADY_BILLED", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(sourceEventId, body.GetProperty("error").GetProperty("message").GetString());
    }

    // ---- 8. editor ≠ poster (ERP-SALES-043) -------------------------------------------

    [Fact]
    public async Task Invoice_Posting_By_Drafter_Returns_409()
    {
        var sku = Tag("SKU");
        await SeedStock(sku, 4m, 2m);
        var soId = await SeedShippedSo(sku, 4m, 25m);
        var draft = await DraftInvoice(soId);
        var invoiceId = draft.GetProperty("id").GetString()!;

        var res = await PostAsync($"/api/v1/invoices/{invoiceId}/postings", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("INVOICE_APPROVAL_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // a different officer with invoices:approve posts it
        res = await PostAsync($"/api/v1/invoices/{invoiceId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Posted", (await Json(res)).GetProperty("data").GetProperty("invoice").GetProperty("status").GetString());
    }

    // ---- 9. golden posting: Dr 1200 total / Cr 4000 net / Cr 2200 tax (048 AC1, 035) ---

    [Fact]
    public async Task Invoice_Posting_Golden_Balanced_Dr_1200_Cr_4000_Cr_2200()
    {
        var sku = Tag("SKU");
        await SeedStock(sku, 10m, 5m);
        var soId = await SeedShippedSo(sku, 10m, 100m); // 1000.00 basis
        var draft = await DraftInvoice(soId);
        var invoiceId = draft.GetProperty("id").GetString()!;

        var (_, journal) = await PostInvoice(invoiceId, Fin);
        var invoice = await GetInvoice(invoiceId);

        Assert.Equal(1000.00m, invoice.GetProperty("subTotal").GetDecimal());
        Assert.Equal(150.00m, invoice.GetProperty("taxTotal").GetDecimal());
        Assert.Equal(1150.00m, invoice.GetProperty("total").GetDecimal());
        // per-line pinned components (048 AC2): revision id + version + amount stored
        var line = invoice.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("TC-VAT-STD-BD-V1", line.GetProperty("taxCodeId").GetString());
        Assert.Equal(1, line.GetProperty("taxCodeVersion").GetInt32());
        Assert.Equal(150.00m, line.GetProperty("taxAmount").GetDecimal());

        // balanced AR journal (FINANCE-035 golden: 15% output tax)
        Assert.Equal("1200", journal.GetProperty("lines")[0].GetProperty("accountCode").GetString());
        Assert.Equal(1150.00m, journal.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal("4000", journal.GetProperty("lines")[1].GetProperty("accountCode").GetString());
        Assert.Equal(1000.00m, journal.GetProperty("lines")[1].GetProperty("credit").GetDecimal());
        Assert.Equal("2200", journal.GetProperty("lines")[2].GetProperty("accountCode").GetString());
        Assert.Equal(150.00m, journal.GetProperty("lines")[2].GetProperty("credit").GetDecimal());

        // payment terms frozen at posting (053 AC1/AC2): NET30 from the customer row
        Assert.Equal("NET30", invoice.GetProperty("paymentTermCode").GetString());
        Assert.Equal(invoice.GetProperty("invoiceDate").GetDateOnly().AddDays(30), invoice.GetProperty("dueDate").GetDateOnly());

        // fully billed SO flips to Invoiced
        var so = (await GetAsync("/api/v1/sales-orders", Sales)).GetProperty("data").EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == soId);
        Assert.Equal("Invoiced", so.GetProperty("status").GetString());

        // 052: the receivables handoff payload rides the outbox event (match THIS test's event —
        // earlier tests post invoices too)
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        var payload = outbox.GetProperty("data").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "sales.invoice.posted")
            .Select(e => JsonDocument.Parse(e.GetProperty("payloadJson").GetString()!).RootElement)
            .First(p => p.GetProperty("invoiceId").GetString() == invoiceId);
        Assert.Equal(journal.GetProperty("id").GetString(), payload.GetProperty("journalId").GetString());
        Assert.Equal(1150.00m, payload.GetProperty("total").GetDecimal());
        Assert.Equal(150.00m, payload.GetProperty("taxTotal").GetDecimal());
        Assert.Equal("BDT", payload.GetProperty("currency").GetString());
        Assert.True(payload.TryGetProperty("dueDate", out _));
        Assert.True(payload.TryGetProperty("customerId", out _));
    }

    // ---- 10. posting replay: one journal (ERP-SALES-045 AC1/AC2) ----------------------

    [Fact]
    public async Task Invoice_Posting_Same_Key_Replay_Posts_One_Journal()
    {
        var sku = Tag("SKU");
        await SeedStock(sku, 6m, 1m);
        var soId = await SeedShippedSo(sku, 6m, 50m);
        var draft = await DraftInvoice(soId);
        var invoiceId = draft.GetProperty("id").GetString()!;
        var key = $"post-{Guid.NewGuid():N}";

        var journalsBefore = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();
        var (firstId, _) = await PostInvoice(invoiceId, Fin, key);

        var res = await PostAsync($"/api/v1/invoices/{invoiceId}/postings", new { }, Fin, key);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(firstId, (await Json(res)).GetProperty("data").GetProperty("invoice").GetProperty("id").GetString());
        Assert.Equal(journalsBefore + 1, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());
    }

    // ---- 11. credit memo bounded by open amount (ERP-SALES-046 AC1/AC2) ---------------

    [Fact]
    public async Task Credit_Memo_Over_Open_Amount_Returns_409_ADJUSTMENT_OVER_OPEN()
    {
        var (invoiceId, total) = await SeedPostedInvoice(5m, 100m); // total 575.00 (15% VAT)
        Assert.Equal(575.00m, total);

        // credit larger than the open amount → 409, nothing approved
        var over = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit",
            invoiceId,
            reason = "Full cancellation credit",
            lines = new[] { new { itemSku = "X", qty = 10m, unitPrice = 100m, taxCodeId = "VAT-STD-BD" } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, over.StatusCode);
        var adjustmentId = (await Json(over)).GetProperty("data").GetProperty("id").GetString()!;
        var res = await PostAsync($"/api/v1/adjustments/{adjustmentId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ADJUSTMENT_OVER_OPEN", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // within bounds: credit 2 × 100 + 15% tax = 230.00 → approved, reversing journal
        var journalsBefore = (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength();
        var credit = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit",
            invoiceId,
            reason = "Returned 2 units",
            lines = new[] { new { itemSku = "X", qty = 2m, unitPrice = 100m, taxCodeId = "VAT-STD-BD" } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, credit.StatusCode);
        var creditId = (await Json(credit)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync($"/api/v1/adjustments/{creditId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var approved = (await Json(res)).GetProperty("data");
        Assert.Equal(230.00m, approved.GetProperty("total").GetDecimal());
        Assert.NotNull(approved.GetProperty("journalId").GetString());

        // the reversing journal hit the ledger (Dr 4000 + Dr 2200 / Cr 1200)
        Assert.Equal(journalsBefore + 1, (await GetAsync("/api/v1/journals", Admin)).GetProperty("data").GetArrayLength());

        // 046 AC2: the posted invoice row itself is untouched — only the open amount moves
        var invoice = await GetInvoice(invoiceId);
        Assert.Equal(575.00m, invoice.GetProperty("total").GetDecimal());
        Assert.Equal(345.00m, invoice.GetProperty("openAmount").GetDecimal()); // 575 − 230 credit

        // settled invoice → reminder suppressed; disputed/suppressed share the code (058 AC1)
        var remainder = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit",
            invoiceId,
            reason = "Settle remainder",
            lines = new[] { new { itemSku = "X", qty = 3m, unitPrice = 100m, taxCodeId = "VAT-STD-BD" } },
        }, Sales);
        var remainderId = (await Json(remainder)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/adjustments/{remainderId}/approvals", new { reason = (string?)null }, Fin)).StatusCode);

        res = await PostAsync("/api/v1/collections/reminders", new { invoiceId, channel = "Email", recipient = "ap@buyer.test" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("REMINDER_SUPPRESSED", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    // ---- 12. credit exposure + hold + override (ERP-SALES-057 AC2) ---------------------

    [Fact]
    public async Task Exposure_Over_Limit_Holds_SO_And_Release_Requires_Override()
    {
        var (invoiceId, _) = await SeedPostedInvoice(5m, 100m, creditLimit: "500"); // exposure 575.00 > 500

        // second order pushes exposure past the limit → approval lands CreditHold, not Confirmed
        var customerId = (await GetInvoice(invoiceId)).GetProperty("customerId").GetString()!;
        var sku = Tag("SKU");
        await SeedStock(sku, 10m, 1m);
        var (_, soId) = await SeedConfirmedSo(sku, 10m, 100m, customerId); // 1000.00 on top

        var orders = await GetAsync("/api/v1/sales-orders", Sales);
        var so = orders.GetProperty("data").EnumerateArray().First(o => o.GetProperty("id").GetString() == soId);
        Assert.Equal("CreditHold", so.GetProperty("status").GetString());

        // release requires credit:override — sales lacks it
        var res = await PostAsync($"/api/v1/sales-orders/{soId}/credit-releases", new { }, Sales);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        // finance holds credit:override (R-FIN seed) → Confirmed + audited outbox event
        res = await PostAsync($"/api/v1/sales-orders/{soId}/credit-releases", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Confirmed", (await Json(res)).GetProperty("data").GetProperty("status").GetString());
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        Assert.Contains(outbox.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("type").GetString()),
            t => t == "sales.credit.released");

        // exposure read: open invoiced 575 + open order 1000 = 1575 vs limit 500
        var exposure = await GetAsync($"/api/v1/customers/{customerId}/credit-exposure", Fin);
        var data = exposure.GetProperty("data");
        Assert.Equal(575.00m, data.GetProperty("openInvoiced").GetDecimal());
        Assert.Equal(1000.00m, data.GetProperty("openOrders").GetDecimal());
        Assert.Equal(1575.00m, data.GetProperty("total").GetDecimal());
        Assert.Equal(500m, data.GetProperty("creditLimit").GetDecimal());
        Assert.True(data.GetProperty("overLimit").GetBoolean());
        Assert.True(data.GetProperty("hasData").GetBoolean());

        // a customer with no invoices/orders says so explicitly — never silent (057 edge)
        var freshCustomer = await SeedCustomer();
        var fresh = (await GetAsync($"/api/v1/customers/{freshCustomer}/credit-exposure", Fin)).GetProperty("data");
        Assert.False(fresh.GetProperty("hasData").GetBoolean());
        Assert.Equal(0m, fresh.GetProperty("total").GetDecimal());
    }

    // ---- 13. reminder suppressed while disputed (058 AC1, 059 AC1) ---------------------

    [Fact]
    public async Task Reminder_For_Disputed_Invoice_Returns_409_REMINDER_SUPPRESSED()
    {
        var (invoiceId, _) = await SeedPostedInvoice(2m, 50m);

        var res = await PostAsync("/api/v1/collections/disputes", new
        {
            invoiceId,
            reason = "Pricing dispute on line 1",
            promiseDueDate = Today.AddDays(7),
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dispute = (await Json(res)).GetProperty("data"); // read once — the content stream closes
        var disputeId = dispute.GetProperty("id").GetString()!;
        Assert.Equal("Promised", dispute.GetProperty("status").GetString());

        // reminder while the dispute is open/promised → suppressed
        res = await PostAsync("/api/v1/collections/reminders", new { invoiceId, channel = "Email", recipient = "ap@buyer.test" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("REMINDER_SUPPRESSED", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // resolution requires an outcome
        res = await PostAsync($"/api/v1/collections/disputes/{disputeId}/resolutions", new { }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // after resolution reminders flow again (059)
        res = await PostAsync($"/api/v1/collections/disputes/{disputeId}/resolutions", new { outcome = "Discount agreed" }, Sales);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        res = await PostAsync("/api/v1/collections/reminders", new { invoiceId, channel = "Email", templateVersion = "v1", recipient = "ap@buyer.test" }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var reminder = (await Json(res)).GetProperty("data");
        Assert.Equal("Email", reminder.GetProperty("channel").GetString());
        Assert.Equal("v1", reminder.GetProperty("templateVersion").GetString());
        Assert.Equal("ap@buyer.test", reminder.GetProperty("recipient").GetString());
    }

    // ---- 14. promise expiry → overdue follow-up (059 AC2) ------------------------------

    [Fact]
    public async Task Promise_Expiry_Shows_Overdue_Follow_Up()
    {
        // invoice backdated 40 days → due 10 days ago → past due → on the worklist
        var (invoiceId, _) = await SeedPostedInvoice(3m, 40m, invoiceDate: Today.AddDays(-40));

        var res = await PostAsync("/api/v1/collections/disputes", new
        {
            invoiceId,
            reason = "Waiting for customer confirmation",
            promiseDueDate = Today.AddDays(-1), // promise already broken
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var worklist = await GetAsync("/api/v1/collections/worklist", Sales);
        var item = worklist.GetProperty("data").EnumerateArray()
            .First(w => w.GetProperty("invoiceId").GetString() == invoiceId);
        Assert.True(item.GetProperty("disputed").GetBoolean());
        Assert.True(item.GetProperty("followUp").GetBoolean()); // promise expiry flagged
        Assert.True(item.GetProperty("daysPastDue").GetInt32() >= 1);
        Assert.True(item.GetProperty("openAmount").GetDecimal() > 0);
    }

    // ---- 15. period lock on the new posting path (task-5 centralized guard) ------------

    [Fact]
    public async Task Invoice_Posting_Into_Closed_Period_Returns_409_PERIOD_LOCKED()
    {
        var sku = Tag("SKU");
        await SeedStock(sku, 2m, 1m);
        var soId = await SeedShippedSo(sku, 2m, 50m);

        // dedicated future period, closed for the probe — today's shared month stays Open
        var start = MonthStart(Today).AddMonths(9);
        var res = await PostAsync("/api/v1/fiscal-periods", new
        {
            name = $"{start:yyyy-MM}-t7lock",
            startDate = start,
            endDate = start.AddMonths(1).AddDays(-1),
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var periodId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/fiscal-periods/{periodId}/close", new { reason = "t7 lock probe" }, Admin)).StatusCode);

        var draft = await DraftInvoice(soId, start.AddDays(5));
        var invoiceId = draft.GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/invoices/{invoiceId}/postings", new { }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("PERIOD_LOCKED", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // the refused posting left the invoice in Draft
        Assert.Equal("Draft", (await GetInvoice(invoiceId)).GetProperty("status").GetString());
    }

    // ---- negative paths for the remaining endpoints (CLAUDE.md: every endpoint tested) --

    [Fact]
    public async Task Lead_Without_Source_Returns_400()
    {
        var res = await PostAsync("/api/v1/leads", new { company = "No Source Co", contactName = "X" }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Lead_Transition_Map_Rejects_Skips_And_Disqualified_Needs_Reason()
    {
        var tag = Tag("LEAD");
        var res = await PostAsync("/api/v1/leads", new { company = $"Skip Co {tag}", source = "Referral" }, Sales);
        var leadId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // New → Qualified skips Working → 409
        res = await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Qualified" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("LEAD_TRANSITION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Working" }, Sales)).StatusCode);

        // Disqualified without a reason code → 400
        res = await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Disqualified" }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // legal: Working → Disqualified → Recycled → Working
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Disqualified", reasonCode = "Out of segment" }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Recycled" }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/leads/{leadId}/transitions", new { status = "Working" }, Sales)).StatusCode);
    }

    [Fact]
    public async Task Interaction_Requires_Party_And_Known_Channel()
    {
        var res = await PostAsync("/api/v1/crm/interactions", new { channel = "Email", subject = "no party", occurredAt = DateTimeOffset.UtcNow }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        res = await PostAsync("/api/v1/crm/interactions", new
        {
            partyType = "Customer", partyId = await SeedCustomer(), channel = "Fax",
            subject = "unknown channel", occurredAt = DateTimeOffset.UtcNow,
        }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Crm_Case_Requires_Category()
    {
        var res = await PostAsync("/api/v1/crm/cases", new { priority = "Low" }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Crm_Case_Transition_Rejects_Skips()
    {
        var res = await PostAsync("/api/v1/crm/cases", new { category = "Warranty", priority = "Medium" }, Sales);
        var caseId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // Open → Closed skips the map
        res = await PostAsync($"/api/v1/crm/cases/{caseId}/transitions", new { status = "Closed" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("CRM_CASE_TRANSITION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Opportunity_Requires_Currency()
    {
        var res = await PostAsync("/api/v1/opportunities", new { title = "No currency deal", ownerId = Sales }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Opportunity_Lines_Rejected_After_Close()
    {
        var customerId = await SeedCustomer();
        var oppId = await CreateOpportunity(customerId);
        var (_, soId) = await SeedConfirmedSo(Tag("SKU"), 1m, 10m, customerId);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new { status = "Won", stage = "Closed", orderId = soId }, Sales)).StatusCode);

        var res = await PostAsync($"/api/v1/opportunities/{oppId}/lines", new { itemSku = "A", description = "Alpha", qty = 1m, targetPrice = 10m }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OPPORTUNITY_STATE_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Opportunity_Transition_Rejects_Reopen_After_Terminal()
    {
        var oppId = await CreateOpportunity(null);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new { status = "Lost", stage = "Closed", lossReason = "No budget" }, Sales)).StatusCode);

        var res = await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new { status = "Open", stage = "Prospecting" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("OPPORTUNITY_TRANSITION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invoice_Draft_Requires_Shipped_Order()
    {
        var sku = Tag("SKU");
        await SeedStock(sku, 5m, 1m);
        var (_, soId) = await SeedConfirmedSo(sku, 5m, 20m); // confirmed, NOT shipped

        var res = await PostAsync($"/api/v1/sales-orders/{soId}/invoice-drafts", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("SO_NOT_BILLABLE", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Adjustment_Requires_Reason_And_Existing_Invoice()
    {
        var res = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit",
            invoiceId = "INV-NOPE",
            lines = new[] { new { itemSku = "X", qty = 1m, unitPrice = 10m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode); // reason missing → 400 first
        Assert.Equal("VALIDATION_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        res = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit",
            invoiceId = "INV-NOPE",
            reason = "Unknown invoice probe",
            lines = new[] { new { itemSku = "X", qty = 1m, unitPrice = 10m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("INVOICE_NOT_FOUND", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Adjustment_Approval_By_Creator_Returns_409()
    {
        var (invoiceId, _) = await SeedPostedInvoice(2m, 30m);
        var res = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Debit",
            invoiceId,
            reason = "Undercharged freight",
            lines = new[] { new { itemSku = "FRT", qty = 1m, unitPrice = 10m } },
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var adjustmentId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // maker-checker: the drafter cannot approve their own adjustment
        res = await PostAsync($"/api/v1/adjustments/{adjustmentId}/approvals", new { reason = (string?)null }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ADJUSTMENT_APPROVAL_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // finance approves — debit raises the open amount by 11.50 (10 + 15%)
        res = await PostAsync($"/api/v1/adjustments/{adjustmentId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(11.50m, (await Json(res)).GetProperty("data").GetProperty("total").GetDecimal());
    }

    // R-T7f: the journal-only invoice shortcut is retired once the document flow ships
    [Fact]
    public async Task Retired_Journal_Only_Invoice_Endpoint_Returns_404()
    {
        var sku = Tag("SKU");
        await SeedStock(sku, 1m, 1m);
        var soId = await SeedShippedSo(sku, 1m, 10m);

        var res = await PostAsync($"/api/v1/sales-orders/{soId}/invoices", new { }, Sales);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---- fix 72931d8 semantics on the new memo path: Zero basis publishes a 0 component -----

    [Fact]
    public async Task Adjustment_With_Zero_Rated_Code_Publishes_Zero_Tax_And_No_Vat_Control_Line()
    {
        var (invoiceId, _) = await SeedPostedInvoice(2m, 50m);

        // publish a zero-rated code (rate 0 + reason, per the 032 edge rule)
        var res = await PostAsync("/api/v1/tax-codes", new
        {
            code = $"VAT-ZERO-{Tag("Z")}",
            ratePct = 0m,
            basis = "Zero",
            recoverable = true,
            effectiveFrom = new DateOnly(2020, 1, 1),
            effectiveTo = (DateOnly?)null,
            reason = "zero-rated export",
        }, Admin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var zeroCode = (await Json(res)).GetProperty("data").GetProperty("code").GetString()!;

        res = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit",
            invoiceId,
            reason = "Zero-rated export return",
            lines = new[] { new { itemSku = "EXP-1", qty = 1m, unitPrice = 40m, taxCodeId = zeroCode } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var adjustmentId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync($"/api/v1/adjustments/{adjustmentId}/approvals", new { reason = (string?)null }, Fin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var approved = (await Json(res)).GetProperty("data");
        Assert.Equal(40.00m, approved.GetProperty("total").GetDecimal()); // net only — no tax component

        // the reversing journal carries no 2200 control line: Dr 4000 net / Cr 1200 net
        var journals = await GetAsync("/api/v1/journals", Admin);
        var journal = journals.GetProperty("data").EnumerateArray()
            .First(j => j.GetProperty("id").GetString() == approved.GetProperty("journalId").GetString());
        var accounts = journal.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("accountCode").GetString()).ToList();
        Assert.DoesNotContain("2200", accounts);
        Assert.Equal(2, accounts.Count);
    }

    // ---- helper ------------------------------------------------------------------------

    private async Task<string> CreateOpportunity(string? customerId)
    {
        var res = await PostAsync("/api/v1/opportunities", new
        {
            customerId,
            title = $"Deal {Tag("OPP")}",
            currency = "BDT",
            expectedCloseDate = Today.AddMonths(1),
            ownerId = Sales,
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }
}

// JsonElement extension for DateOnly comparisons in assertions
internal static class SalesDeepJsonExtensions
{
    public static DateOnly GetDateOnly(this JsonElement element) => DateOnly.Parse(element.GetString()!);
}
