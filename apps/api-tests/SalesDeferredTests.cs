using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api.Tests;
using Xunit;

// Deferred SALES screens wave (ERP-SALES-021..026/030 quotes+pricing first slice; later
// scenarios append per slice). Brief: .superpowers/sdd/06_Plan/briefs/deferred-screens-sales.md
// — scenario numbering mirrors brief §7 (1..28) + authz negative sweep (28/Gate 2).
// Test data uses unique SKUs per test — the class fixture shares one throwaway database.

public partial class SalesDeferredTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public SalesDeferredTests(TestApiFactory factory) => _factory = factory;

    private const string Admin = "U-001";
    private const string Fin = "U-003";
    private const string Sales = "U-004";
    private const string SalesManager = "U-009"; // Q-S1: second sales user (maker-checker)

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? token, string? idemKey = null)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        if (idemKey is not null) client.DefaultRequestHeaders.Add("Idempotency-Key", idemKey);
        return await client.PostAsJsonAsync(path, body);
    }

    private async Task<HttpResponseMessage> RawAsync(HttpMethod method, string path, string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.SendAsync(new HttpRequestMessage(method, path));
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

    private async Task<string> SeedCustomer(string? creditLimit = null)
    {
        var tag = Tag("CUST");
        var payload = (object)new { name = $"Buyer {tag}", taxId = $"TRX-{tag}", email = $"ap.{tag.ToLowerInvariant()}@buyer.test" };
        if (creditLimit is not null) payload = new { name = $"Buyer {tag}", taxId = $"TRX-{tag}", email = $"ap.{tag.ToLowerInvariant()}@buyer.test", creditLimit };
        var res = await PostAsync("/api/v1/customers", payload, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<string> SeedBook(string sku, decimal price, int precedence = 1, decimal minQty = 0)
    {
        var res = await PostAsync("/api/v1/price-books", new { name = $"Book {Tag("PB")}", currency = "BDT", precedence }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var bookId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        var entry = await PostAsync($"/api/v1/price-books/{bookId}/entries", new
        {
            itemSku = sku, minQty, unitPrice = price, effectiveFrom = Today.AddDays(-1),
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, entry.StatusCode);
        return bookId;
    }

    // ---- shared flow seeds (same shape as SalesDeepTests — private per class) ----------

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

    // ---- S2 price books (024/025/026) ---------------------------------------------------

    // 7a (024 AC2): overlapping active entries for the same sku are rejected — never silently
    // precedence-resolved inside one book
    [Fact]
    public async Task Price_Book_Overlapping_Entries_Rejected()
    {
        var sku = Tag("SKU");
        var bookId = await SeedBook(sku, 100m);
        var res = await PostAsync($"/api/v1/price-books/{bookId}/entries", new
        {
            itemSku = sku, minQty = 0m, unitPrice = 90m, effectiveFrom = Today, effectiveTo = Today.AddDays(30),
        }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("PRICE_OVERLAP", (await Json(res)).GetProperty("error").GetProperty("code").GetString());
    }

    // 7b (024 edge): no matching price → controlled PRICE_NOT_FOUND, never a silent zero
    [Fact]
    public async Task Quote_Line_Without_Price_Resolution_Is_Rejected_Not_Zero()
    {
        var customerId = await SeedCustomer();
        var res = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT", lines = new[] { new { itemSku = Tag("SKU"), qty = 2m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("PRICE_NOT_FOUND", body.GetProperty("error").GetProperty("code").GetString());
        Assert.NotEqual(0m, body.GetProperty("error").GetProperty("message").GetString()!.Length);
    }

    // 8 (026 AC1–2): contract price wins over books by precedence; expired contracts excluded
    [Fact]
    public async Task Contract_Price_Wins_And_Expired_Contract_Excluded()
    {
        var customerId = await SeedCustomer();
        var sku = Tag("SKU");
        await SeedBook(sku, 100m);
        // active contract (cheaper) — must win
        var ok = await PostAsync("/api/v1/customer-contract-prices", new
        {
            customerId, itemSku = sku, minQty = 0m, unitPrice = 80m, effectiveFrom = Today.AddDays(-1),
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var q1 = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT", lines = new[] { new { itemSku = sku, qty = 5m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, q1.StatusCode);
        var quote1 = (await Json(q1)).GetProperty("data");
        Assert.Equal(400m, quote1.GetProperty("total").GetDecimal());
        var evidence = quote1.GetProperty("priceEvidence").EnumerateArray().First();
        Assert.Equal("contract", evidence.GetProperty("rule").GetString());

        // expired contract for another customer+sku — must NOT resolve
        var other = await SeedCustomer();
        var sku2 = Tag("SKU");
        var expired = await PostAsync("/api/v1/customer-contract-prices", new
        {
            customerId = other, itemSku = sku2, minQty = 0m, unitPrice = 10m,
            effectiveFrom = Today.AddDays(-10), effectiveTo = Today.AddDays(-1),
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, expired.StatusCode);
        var q2 = await PostAsync("/api/v1/quotes", new
        {
            customerId = other, currency = "BDT", lines = new[] { new { itemSku = sku2, qty = 1m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.NotFound, q2.StatusCode);
        Assert.Equal("PRICE_NOT_FOUND", (await Json(q2)).GetProperty("error").GetProperty("code").GetString());
    }

    // 8b (024 AC1): book precedence decides between two active books; evidence carries book id
    [Fact]
    public async Task Book_Precedence_Resolves_And_Evidence_Carries_Source()
    {
        var customerId = await SeedCustomer();
        var sku = Tag("SKU");
        await SeedBook(sku, 100m, precedence: 2);
        await SeedBook(sku, 70m, precedence: 1); // lower number wins
        var q = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT", lines = new[] { new { itemSku = sku, qty = 2m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, q.StatusCode);
        var quote = (await Json(q)).GetProperty("data");
        Assert.Equal(140m, quote.GetProperty("total").GetDecimal());
        var evidence = quote.GetProperty("priceEvidence").EnumerateArray().First();
        Assert.StartsWith("PBL-", evidence.GetProperty("priceBookId").GetString());
        Assert.True(DateOnly.TryParse(evidence.GetProperty("effectiveFrom").GetString(), out var from) && from <= Today);
    }

    // 9 (025 AC2/edge): a discount over the cap forces the approval path (acceptance blocked
    // until a non-issuer approves); negative net rejected outright
    [Fact]
    public async Task Discount_Over_Cap_Needs_Approval_And_Negative_Net_Rejected()
    {
        var customerId = await SeedCustomer();
        var sku = Tag("SKU");
        await SeedBook(sku, 100m);
        // 50% > cap (15%) → revision lands PendingApproval, acceptance blocked (total is tiny)
        var res = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT",
            lines = new[] { new { itemSku = sku, qty = 1m, unitPrice = 100m, discountPct = 50m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var quote = (await Json(res)).GetProperty("data");
        Assert.Equal("PendingApproval", quote.GetProperty("revisionStatus").GetString());
        var quoteId = quote.GetProperty("id").GetString()!;

        var early = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/1/acceptances",
            new { method = "Email" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("QUOTE_STATE_ERROR", (await Json(early)).GetProperty("error").GetProperty("code").GetString());

        // maker-checker: issuer cannot clear the cap breach; a different approver can
        var self = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/1/approvals", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("QUOTE_APPROVAL_ERROR", (await Json(self)).GetProperty("error").GetProperty("code").GetString());
        var ok = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/1/approvals", new { }, SalesManager);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var accept = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/1/acceptances",
            new { method = "Email" }, Sales);
        Assert.Equal(HttpStatusCode.Created, accept.StatusCode);
        var detail = await GetAsync($"/api/v1/quotes/{quoteId}", Sales);
        Assert.Equal("Accepted", detail.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(50m, detail.GetProperty("data").GetProperty("revisions").EnumerateArray()
            .First().GetProperty("total").GetDecimal());

        // discountPct >= 100 → negative/zero net → VALIDATION_ERROR
        var negative = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT",
            lines = new[] { new { itemSku = sku, qty = 1m, unitPrice = 100m, discountPct = 100m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await Json(negative)).GetProperty("error").GetProperty("code").GetString());
    }

    // ---- S1 quotes (021/022/023/030) ----------------------------------------------------

    // 1 (021 AC2): rendered quote reconciles exactly to stored lines/discounts/taxes/total
    [Fact]
    public async Task Quote_Total_Reconciles_To_Lines_Discounts_Tax()
    {
        var customerId = await SeedCustomer();
        var skuA = Tag("SKU");
        var skuB = Tag("SKU");
        await SeedBook(skuA, 100m);
        await SeedBook(skuB, 40m);
        var res = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT",
            lines = new object[]
            {
                new { itemSku = skuA, qty = 3m },                                      // 300, resolved
                new { itemSku = skuB, qty = 2m, unitPrice = 40m, discountPct = 10m },  // 80 − 8 = 72
            },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var quote = (await Json(res)).GetProperty("data");
        Assert.Equal(380m, quote.GetProperty("subTotal").GetDecimal());  // 300 + 80
        Assert.Equal(8m, quote.GetProperty("discountTotal").GetDecimal());
        Assert.Equal(0m, quote.GetProperty("taxTotal").GetDecimal());    // quotes are pre-tax
        Assert.Equal(372m, quote.GetProperty("total").GetDecimal());
        Assert.Equal(1, quote.GetProperty("revision").GetInt32());
        Assert.Equal("Draft", quote.GetProperty("status").GetString());
    }

    private async Task<(string quoteId, int revision, string customerId, string sku)> SeedApprovedQuote(
        decimal unitPrice = 10m, decimal qty = 1m, DateOnly? expiresAt = null)
    {
        var customerId = await SeedCustomer();
        var sku = Tag("SKU");
        await SeedBook(sku, unitPrice);
        var res = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT", expiresAt,
            lines = new[] { new { itemSku = sku, qty } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var quote = (await Json(res)).GetProperty("data");
        var quoteId = quote.GetProperty("id").GetString()!;
        var revision = quote.GetProperty("revision").GetInt32();
        if (quote.GetProperty("revisionStatus").GetString() == "PendingApproval")
        {
            // above-threshold revision — maker-checker approval by the second sales user
            var ap = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/{revision}/approvals", new { }, SalesManager);
            Assert.Equal(HttpStatusCode.OK, ap.StatusCode);
        }
        return (quoteId, revision, customerId, sku);
    }

    // 2 (021 AC1): expired quote cannot be accepted without authorized revalidation
    [Fact]
    public async Task Expired_Quote_Acceptance_Blocked_Until_Revalidated()
    {
        var (quoteId, revision, _, _) = await SeedApprovedQuote(expiresAt: Today.AddDays(-1));
        var res = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/{revision}/acceptances",
            new { method = "Email", externalEventId = $"EVT-{Tag("A")}" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("QUOTE_EXPIRED", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // revalidation is an authorized act (quotes:approve, approver ≠ issuer)
        var sod = await PostAsync($"/api/v1/quotes/{quoteId}/revalidations",
            new { expiresAt = Today.AddDays(30), reason = "customer asked to extend" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, sod.StatusCode);
        Assert.Equal("QUOTE_APPROVAL_ERROR", (await Json(sod)).GetProperty("error").GetProperty("code").GetString());

        var reval = await PostAsync($"/api/v1/quotes/{quoteId}/revalidations",
            new { expiresAt = Today.AddDays(30), reason = "customer asked to extend" }, SalesManager);
        Assert.Equal(HttpStatusCode.OK, reval.StatusCode);

        var accept = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/{revision}/acceptances",
            new { method = "Email", externalEventId = $"EVT-{Tag("A")}" }, Sales);
        Assert.Equal(HttpStatusCode.Created, accept.StatusCode);
    }

    // 3 (022 AC1): material change post-creation → new revision; prior revision intact
    [Fact]
    public async Task Material_Change_Creates_New_Revision_Prior_Retained()
    {
        var (quoteId, revision, customerId, sku) = await SeedApprovedQuote(unitPrice: 10m, qty: 2m);
        var res = await PostAsync($"/api/v1/quotes/{quoteId}/revisions", new
        {
            lines = new[] { new { itemSku = sku, qty = 5m } },
            rationale = "customer asked for more units",
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var rev2 = (await Json(res)).GetProperty("data");
        Assert.Equal(revision + 1, rev2.GetProperty("revision").GetInt32());

        // prior revision + its totals still readable, new revision visible on the quote
        var detail = await GetAsync($"/api/v1/quotes/{quoteId}", Sales);
        var revisions = detail.GetProperty("data").GetProperty("revisions");
        Assert.Equal(2, revisions.GetArrayLength());
        var prior = revisions.EnumerateArray().First(r => r.GetProperty("revision").GetInt32() == revision);
        Assert.Equal(20m, prior.GetProperty("total").GetDecimal());
        var latest = detail.GetProperty("data").GetProperty("revision").GetInt32();
        Assert.Equal(revision + 1, latest);
    }

    // 4 (022 AC2): acceptance pinned to revision N survives creation of N+1
    [Fact]
    public async Task Acceptance_Pinned_To_Revision_Survives_New_Revision()
    {
        var (quoteId, revision, customerId, sku) = await SeedApprovedQuote();
        var evt = $"EVT-{Tag("ACC")}";
        var acc = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/{revision}/acceptances",
            new { method = "Portal", externalEventId = evt }, Sales);
        Assert.Equal(HttpStatusCode.Created, acc.StatusCode);
        var acceptance = (await Json(acc)).GetProperty("data");
        Assert.Equal(revision, acceptance.GetProperty("revision").GetInt32());

        // new revision does not migrate the acceptance
        var rev = await PostAsync($"/api/v1/quotes/{quoteId}/revisions", new
        {
            lines = new[] { new { itemSku = sku, qty = 3m } }, rationale = "scope change",
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, rev.StatusCode);
        var detail = await GetAsync($"/api/v1/quotes/{quoteId}", Sales);
        var data = detail.GetProperty("data");
        Assert.Equal(revision, data.GetProperty("acceptedRevision").GetInt32());
        var acceptanceRow = data.GetProperty("acceptances").EnumerateArray().Single();
        Assert.Equal(evt, acceptanceRow.GetProperty("externalEventId").GetString());
        Assert.Equal(revision, acceptanceRow.GetProperty("revision").GetInt32());
    }

    // 5 (023 AC1–2): threshold quote blocked until approval; maker-checker on the approval
    [Fact]
    public async Task Threshold_Quote_Needs_Approval_By_Non_Issuer()
    {
        // 5000 x 10 = 50,000 > threshold (10000) → PendingApproval
        var (quoteId, revision, _, _) = await SeedApprovedQuote(unitPrice: 10m, qty: 5000m);
        var detail = await GetAsync($"/api/v1/quotes/{quoteId}", Sales);
        var rev = detail.GetProperty("data").GetProperty("revisions").EnumerateArray()
            .First(r => r.GetProperty("revision").GetInt32() == revision);
        Assert.Equal("Approved", rev.GetProperty("status").GetString());
        Assert.Equal(SalesManager, rev.GetProperty("approvals").EnumerateArray().Single().GetProperty("approverId").GetString());

        // issuer cannot self-approve (fresh above-threshold revision)
        var customerId = await SeedCustomer();
        var sku = Tag("SKU");
        await SeedBook(sku, 10m);
        var res = await PostAsync("/api/v1/quotes", new
        {
            customerId, currency = "BDT", lines = new[] { new { itemSku = sku, qty = 5000m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var qid = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        var self = await PostAsync($"/api/v1/quotes/{qid}/revisions/1/approvals", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("QUOTE_APPROVAL_ERROR", (await Json(self)).GetProperty("error").GetProperty("code").GetString());
    }

    // 6 (030 AC2): duplicate external acceptance event → idempotent same acceptance
    [Fact]
    public async Task Duplicate_Acceptance_External_Event_Id_Is_Idempotent()
    {
        var (quoteId, revision, _, _) = await SeedApprovedQuote();
        var evt = $"EVT-{Tag("DUP")}";
        var first = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/{revision}/acceptances",
            new { method = "Email", externalEventId = evt }, Sales);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id1 = (await Json(first)).GetProperty("data").GetProperty("id").GetString();

        var second = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/{revision}/acceptances",
            new { method = "Email", externalEventId = evt }, Sales);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var id2 = (await Json(second)).GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(id1, id2);
    }

    // 30 (030 AC1): accepted revision id rides onto the generated order (031 conversion)
    [Fact]
    public async Task Order_Conversion_Stamps_Accepted_Quote_Revision()
    {
        var (quoteId, revision, customerId, sku) = await SeedApprovedQuote();
        var acc = await PostAsync($"/api/v1/quotes/{quoteId}/revisions/{revision}/acceptances",
            new { method = "Portal" }, Sales);
        Assert.Equal(HttpStatusCode.Created, acc.StatusCode);

        var res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = sku, description = "From quote", qty = 1m, unitPrice = 10m } },
            sourceQuoteRevisionId = $"{quoteId}:R{revision}",
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var order = (await Json(res)).GetProperty("data");
        Assert.Equal($"{quoteId}:R{revision}", order.GetProperty("sourceQuoteRevisionId").GetString());

        // bogus revision reference → controlled error
        var bad = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = sku, description = "From quote", qty = 1m, unitPrice = 10m } },
            sourceQuoteRevisionId = "QTE-NOPE:R1",
        }, Sales);
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
        Assert.Equal("QUOTE_REVISION_NOT_FOUND", (await Json(bad)).GetProperty("error").GetProperty("code").GetString());
    }
}

// ==== S3 customer 360: hierarchy, onboarding controls, party merge (012/013/014) ====

public partial class SalesDeferredTests
{
    // 10 (012 AC1): self-parent and indirect cycles rejected
    [Fact]
    public async Task Customer_Hierarchy_Cycle_And_Self_Parent_Rejected()
    {
        var a = await SeedCustomer();
        var b = await SeedCustomer();
        var ok = await PostAsync($"/api/v1/customers/{b}/hierarchy", new { parentId = a }, Sales);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var self = await PostAsync($"/api/v1/customers/{a}/hierarchy", new { parentId = a }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("CUSTOMER_HIERARCHY_CYCLE", (await Json(self)).GetProperty("error").GetProperty("code").GetString());

        // a → b is set; making b's parent a's ancestor (a) closes a 2-cycle — reject
        var cycle = await PostAsync($"/api/v1/customers/{a}/hierarchy", new { parentId = b }, Sales);
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        Assert.Equal("CUSTOMER_HIERARCHY_CYCLE", (await Json(cycle)).GetProperty("error").GetProperty("code").GetString());
    }

    // 10b (012 AC2): detail rollup identifies included entities
    [Fact]
    public async Task Customer_Detail_Shows_Hierarchy_Rollup()
    {
        var parent = await SeedCustomer();
        var child = await SeedCustomer();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/customers/{child}/hierarchy", new { parentId = parent }, Sales)).StatusCode);

        var childDetail = await GetAsync($"/api/v1/customers/{child}", Sales);
        Assert.Equal(parent, childDetail.GetProperty("data").GetProperty("hierarchy").GetProperty("parentId").GetString());

        var parentDetail = await GetAsync($"/api/v1/customers/{parent}", Sales);
        var hierarchy = parentDetail.GetProperty("data").GetProperty("hierarchy");
        Assert.Contains(child, hierarchy.GetProperty("childrenIds").EnumerateArray().Select(c => c.GetString()));
        var included = hierarchy.GetProperty("includedEntityIds").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Contains(parent, included);
        Assert.Contains(child, included);
    }

    // 11 (013 AC2): order submit for account with unmet mandatory check returns control IDs
    [Fact]
    public async Task Order_Submit_Returns_Unmet_Onboarding_Control_Ids()
    {
        var customerId = await SeedCustomer();
        var created = await PostAsync($"/api/v1/customers/{customerId}/onboarding-checks",
            new { checkCode = "KYC-VERIFY", mandatory = true }, Sales);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var sku = Tag("SKU");
        await SeedStock(sku, 5m, 2m);
        var res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = sku, description = "Gate probe", qty = 1m, unitPrice = 10m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var soId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        var submit = await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        var body = await Json(submit);
        Assert.Equal("ONBOARDING_CONTROLS_UNMET", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("KYC-VERIFY", body.GetProperty("error").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString()));

        // mark the check met → submit passes
        var met = await PostAsync($"/api/v1/customers/{customerId}/onboarding-checks/KYC-VERIFY/met", new { }, Sales);
        Assert.Equal(HttpStatusCode.OK, met.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, Sales)).StatusCode);
    }

    // 12 (014 AC1–2/edge): merge preview lists reassignments; old id resolves after merge;
    // posted balances block
    [Fact]
    public async Task Customer_Merge_Preview_Executes_And_Posted_Balance_Blocks()
    {
        var survivor = await SeedCustomer();
        var merged = await SeedCustomer();
        // open order on the merged customer → should be reassigned to the survivor
        var sku = Tag("SKU");
        await SeedStock(sku, 5m, 2m);
        var res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId = merged,
            lines = new[] { new { itemSku = sku, description = "Move me", qty = 1m, unitPrice = 10m } },
        }, Sales);
        var soId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        var preview = await PostAsync("/api/v1/customer-merges/preview",
            new { survivorId = survivor, mergedId = merged, reason = "duplicate account" }, Admin);
        Assert.Equal(HttpStatusCode.Created, preview.StatusCode);
        var pv = (await Json(preview)).GetProperty("data");
        Assert.Equal(1, pv.GetProperty("reassignments").GetProperty("openOrders").GetInt32());

        // merge act is elevated: sales cannot execute (Q-S3: customers:merge on R-ADMIN)
        var sod = await PostAsync($"/api/v1/customer-merges/{pv.GetProperty("id").GetString()}/executions",
            new { }, Sales);
        Assert.Equal(HttpStatusCode.Forbidden, sod.StatusCode);

        var exec = await PostAsync($"/api/v1/customer-merges/{pv.GetProperty("id").GetString()}/executions",
            new { }, Admin);
        Assert.Equal(HttpStatusCode.OK, exec.StatusCode);

        // old identifier resolves to the survivor; the open order moved
        var old = await GetAsync($"/api/v1/customers/{merged}", Sales);
        Assert.Equal(survivor, old.GetProperty("data").GetProperty("mergedIntoId").GetString());
        var orders = await GetAsync("/api/v1/sales-orders", Sales);
        var moved = orders.GetProperty("data").EnumerateArray().First(o => o.GetProperty("id").GetString() == soId);
        Assert.Equal(survivor, moved.GetProperty("customerId").GetString());

        // posted balance blocks: invoice the other order first, then preview must refuse
        var second = await SeedPostedInvoice(2m, 50m);
        var withPosted = (await GetInvoice(second.invoiceId)).GetProperty("customerId").GetString()!;
        var blocked = await PostAsync("/api/v1/customer-merges/preview",
            new { survivorId = survivor, mergedId = withPosted, reason = "try to merge a billed account" }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("MERGE_BLOCKED", (await Json(blocked)).GetProperty("error").GetProperty("code").GetString());
    }
}
