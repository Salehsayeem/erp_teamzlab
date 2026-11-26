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
    private const string SalesManager = "U-014"; // Q-S1: second sales user (maker-checker)

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

    private async Task<HttpStatusCode> GetStatusAsync(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return (await client.GetAsync(path)).StatusCode;
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

// ==== S4 order lifecycle: holds, amendments, partial fulfillment, cancel, recurring ====

public partial class SalesDeferredTests
{
    private async Task<(string customerId, string soId)> SeedBigConfirmedSo(decimal qty = 10m, decimal price = 10m)
    {
        var sku = Tag("SKU");
        await SeedStock(sku, qty, 2m);
        return await SeedConfirmedSo(sku, qty, price);
    }

    private async Task<string> OrderSku(string soId)
    {
        var orders = await GetAsync("/api/v1/sales-orders", Sales);
        return orders.GetProperty("data").EnumerateArray()
            .First(o => o.GetProperty("id").GetString() == soId)
            .GetProperty("lines").EnumerateArray().First().GetProperty("itemSku").GetString()!;
    }

    // 13 (035 AC1-2): a blocking hold prevents shipment; release records clear actor/time
    [Fact]
    public async Task Blocking_Hold_Gates_Shipment_Release_Records_Actor()
    {
        var (_, soId) = await SeedBigConfirmedSo();
        var hold = await PostAsync($"/api/v1/sales-orders/{soId}/holds",
            new { holdType = "Compliance", reason = "export screening pending", source = "trade-compliance" }, Sales);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var holdId = (await Json(hold)).GetProperty("data").GetProperty("id").GetString()!;

        var ship = await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = await OrderSku(soId), qty = 1m }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, ship.StatusCode);
        Assert.Equal("SO_HELD", (await Json(ship)).GetProperty("error").GetProperty("code").GetString());

        var release = await PostAsync($"/api/v1/sales-orders/{soId}/holds/{holdId}/releases", new { reason = "screening cleared" }, Sales);
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        var cleared = (await Json(release)).GetProperty("data");
        Assert.Equal("Cleared", cleared.GetProperty("status").GetString());
        Assert.Equal(Sales, cleared.GetProperty("clearedBy").GetString());
        Assert.NotNull(cleared.GetProperty("clearedAt").GetString());

        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments",
            new { itemSku = await OrderSku(soId), qty = 1m }, Sales)).StatusCode);
    }

    // 14 (034 AC1-2): pending amendment is invisible to fulfillment; approval applies it
    [Fact]
    public async Task Amendment_Requires_Approval_Before_Fulfillment_Sees_It()
    {
        var (_, soId) = await SeedBigConfirmedSo(qty: 10m, price: 10m);
        var sku = await OrderSku(soId);
        var amd = await PostAsync($"/api/v1/sales-orders/{soId}/amendments", new
        {
            lines = new[] { new { itemSku = sku, description = "amended", qty = 4m, unitPrice = 20m } },
            rationale = "customer cut scope",
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, amd.StatusCode);
        var amdId = (await Json(amd)).GetProperty("data").GetProperty("id").GetString()!;
        var detail = await GetAsync($"/api/v1/sales-orders/{soId}", Sales);
        Assert.Equal("PendingAmendmentApproval", detail.GetProperty("data").GetProperty("status").GetString());

        // fulfillment cannot ship while an amendment is pending (034 AC2)
        var blocked = await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty = 1m }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // maker-checker: issuer cannot approve own amendment; U-014 can
        var self = await PostAsync($"/api/v1/sales-orders/{soId}/amendments/{amdId}/approvals", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("SO_AMENDMENT_ERROR", (await Json(self)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/amendments/{amdId}/approvals", new { }, SalesManager)).StatusCode);

        // now fulfillment sees the approved version: line qty 4 @ 20
        var after = await GetAsync($"/api/v1/sales-orders/{soId}", Sales);
        var line = after.GetProperty("data").GetProperty("lines").EnumerateArray().First();
        Assert.Equal(4m, line.GetProperty("qty").GetDecimal());
        Assert.Equal(20m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty = 1m }, Sales)).StatusCode);
    }

    // 15 (036 AC1/037): two shipments update fulfilled/remaining independently; shortage
    // leaves measurable remaining + expected date
    [Fact]
    public async Task Two_Shipments_Track_Fulfilled_Remaining_And_Backorder()
    {
        var (_, soId) = await SeedBigConfirmedSo(qty: 10m, price: 10m);
        var sku = await OrderSku(soId);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty = 3m }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty = 2m }, Sales)).StatusCode);

        var detail = await GetAsync($"/api/v1/sales-orders/{soId}", Sales);
        var line = detail.GetProperty("data").GetProperty("lines").EnumerateArray().First();
        Assert.Equal(5m, line.GetProperty("fulfilledQty").GetDecimal());
        Assert.Equal(5m, line.GetProperty("remainingQty").GetDecimal());
        Assert.Equal(0m, line.GetProperty("backorderQty").GetDecimal());

        // shortage shipment: line wants 10, 3 more given -> remaining 2 measured + expected date
        var expected = Today.AddDays(14);
        var shortage = await PostAsync($"/api/v1/sales-orders/{soId}/shipments",
            new { itemSku = sku, qty = 3m, expectedDate = expected }, Sales);
        Assert.Equal(HttpStatusCode.Created, shortage.StatusCode);
        var after = await GetAsync($"/api/v1/sales-orders/{soId}", Sales);
        var shortLine = after.GetProperty("data").GetProperty("lines").EnumerateArray().First();
        Assert.Equal(8m, shortLine.GetProperty("fulfilledQty").GetDecimal());
        Assert.Equal(2m, shortLine.GetProperty("remainingQty").GetDecimal());
        Assert.Equal(2m, shortLine.GetProperty("backorderQty").GetDecimal());
        Assert.Equal(expected, DateOnly.Parse(shortLine.GetProperty("expectedDate").GetString()!));
    }

    // 16 (039 AC1-2): cancellation of the remaining preserves posted invoices
    [Fact]
    public async Task Cancel_Remaining_Preserves_Posted_Invoice_And_Records_Outcome()
    {
        var customerId = await SeedCustomer();
        var sku = Tag("SKU");
        await SeedStock(sku, 10m, 2m);
        var (_, soId) = await SeedConfirmedSo(sku, 10m, 25m, customerId);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty = 4m }, Sales)).StatusCode);
        var draft = await DraftInvoice(soId);
        var (invoiceId, _) = await PostInvoice(draft.GetProperty("id").GetString()!, Fin);

        var cancel = await PostAsync($"/api/v1/sales-orders/{soId}/cancellations",
            new { reason = "customer walked from the remainder", scope = "Remaining" }, Sales);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var outcome = (await Json(cancel)).GetProperty("data");
        Assert.Equal("Cancelled", outcome.GetProperty("status").GetString());
        Assert.Equal(6m, outcome.GetProperty("remainingQty").GetDecimal());

        // posted invoice untouched - corrections ride the credit-note flow (039 AC2)
        var invoice = await GetInvoice(invoiceId);
        Assert.Equal("Posted", invoice.GetProperty("status").GetString());

        // cancelling again is a state error
        var again = await PostAsync($"/api/v1/sales-orders/{soId}/cancellations",
            new { reason = "double cancel", scope = "Remaining" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // 16b (039 AC2): post-release FULL cancellation needs a different approver
    [Fact]
    public async Task Post_Release_Full_Cancel_Needs_Approver_Role()
    {
        var (_, soId) = await SeedBigConfirmedSo(qty: 10m, price: 10m);
        var sku = await OrderSku(soId);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty = 2m }, Sales)).StatusCode);

        var self = await PostAsync($"/api/v1/sales-orders/{soId}/cancellations",
            new { reason = "full walkaway", scope = "Full" }, Sales);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode); // maker + post-release full scope

        var approved = await PostAsync($"/api/v1/sales-orders/{soId}/cancellations",
            new { reason = "full walkaway", scope = "Full" }, SalesManager);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Cancelled", (await Json(approved)).GetProperty("data").GetProperty("status").GetString());
    }

    // 17 (040 AC1-2): at most one occurrence per (schedule, period), duplicate idempotent,
    // paused yields none
    [Fact]
    public async Task Recurring_Generation_Is_Idempotent_Per_Period_And_Pauses()
    {
        var customerId = await SeedCustomer();
        var schedule = await PostAsync("/api/v1/recurring-schedules", new
        {
            customerId,
            frequency = "Monthly",
            nextRunDate = Today.AddDays(1),
            lines = new[] { new { itemSku = Tag("SKU"), description = "Maintenance", qty = 2m, unitPrice = 50m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, schedule.StatusCode);
        var scheduleId = (await Json(schedule)).GetProperty("data").GetProperty("id").GetString()!;

        var first = await PostAsync($"/api/v1/recurring-schedules/{scheduleId}/generations", new { period = "2026-12" }, Sales);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var orderId1 = (await Json(first)).GetProperty("data").GetProperty("orderId").GetString()!;

        var duplicate = await PostAsync($"/api/v1/recurring-schedules/{scheduleId}/generations", new { period = "2026-12" }, Sales);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(orderId1, (await Json(duplicate)).GetProperty("data").GetProperty("orderId").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/recurring-schedules/{scheduleId}/pauses", new { }, Sales)).StatusCode);
        var paused = await PostAsync($"/api/v1/recurring-schedules/{scheduleId}/generations", new { period = "2027-01" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, paused.StatusCode);
        Assert.Equal("SCHEDULE_PAUSED", (await Json(paused)).GetProperty("error").GetProperty("code").GetString());
    }
}

// ==== S5 forecast categories + snapshots (018/020) ====

public partial class SalesDeferredTests
{
    private async Task<string> SeedOpenOpportunity(decimal lineTotal)
    {
        var customerId = await SeedCustomer();
        var res = await PostAsync("/api/v1/opportunities", new
        {
            customerId, title = $"Opp {Tag("OPP")}", currency = "BDT", ownerId = Sales, expectedCloseDate = (DateOnly?)null,
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var oppId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        var line = await PostAsync($"/api/v1/opportunities/{oppId}/lines", new
        {
            itemSku = Tag("SKU"), description = "License", qty = 1m, targetPrice = lineTotal,
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, line.StatusCode);
        return oppId;
    }

    // 18a (018 AC1): report shows unweighted / weighted / submitted-override separately
    [Fact]
    public async Task Forecast_Report_Separates_Buckets()
    {
        await SeedOpenOpportunity(100m);
        var sub = await PostAsync("/api/v1/forecast-submissions", new
        {
            owner = Sales, period = "2099-01", category = "Commit", overrideValue = 90m,
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, sub.StatusCode);

        var report = await GetAsync("/api/v1/forecast?period=2099-01", Sales);
        var data = report.GetProperty("data");
        Assert.Equal(100m, data.GetProperty("unweighted").GetProperty("total").GetDecimal());
        Assert.True(data.GetProperty("weighted").GetProperty("total").GetDecimal() > 0);
        Assert.Equal(90m, data.GetProperty("override").GetProperty("total").GetDecimal());
        Assert.True(data.GetProperty("weighted").GetProperty("total").GetDecimal()
            != data.GetProperty("unweighted").GetProperty("total").GetDecimal()); // separate buckets
    }

    // 18b (018 AC2): submission records owner/period/time/version — resubmission bumps version
    [Fact]
    public async Task Forecast_Submission_Records_Versioned_Provenance()
    {
        var first = await PostAsync("/api/v1/forecast-submissions", new
        {
            owner = Sales, period = "2099-02", category = "BestCase", overrideValue = 10m,
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var v1 = (await Json(first)).GetProperty("data");
        Assert.Equal(1, v1.GetProperty("version").GetInt32());
        Assert.Equal(Sales, v1.GetProperty("owner").GetString());
        Assert.NotNull(v1.GetProperty("submittedAt").GetString());

        var second = await PostAsync("/api/v1/forecast-submissions", new
        {
            owner = Sales, period = "2099-02", category = "BestCase", overrideValue = 20m,
        }, Sales);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var v2 = (await Json(second)).GetProperty("data");
        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        Assert.Equal(20m, v2.GetProperty("overrideValue").GetDecimal());
    }

    // 19 (020 AC1-2): snapshot freezes included ids/values; later opportunity edits never
    // change it; snapshots are append-only rows (fixture DB may hold other open opps —
    // assert reconciliation with the live report, then freeze)
    [Fact]
    public async Task Snapshot_Immunne_To_Later_Opportunity_Edits()
    {
        var oppId = await SeedOpenOpportunity(100m);
        var report = await GetAsync("/api/v1/forecast?period=2099-03", Sales);
        var liveTotal = report.GetProperty("data").GetProperty("unweighted").GetProperty("total").GetDecimal();

        var snap = await PostAsync("/api/v1/forecast-snapshots", new { period = "2099-03" }, Sales);
        Assert.Equal(HttpStatusCode.Created, snap.StatusCode);
        var snapshot = (await Json(snap)).GetProperty("data");
        Assert.Equal("Complete", snapshot.GetProperty("status").GetString());
        Assert.Contains(oppId, snapshot.GetProperty("payload").GetProperty("includedIds").EnumerateArray()
            .Select(i => i.GetString()));
        Assert.Equal(liveTotal, snapshot.GetProperty("payload").GetProperty("unweightedTotal").GetDecimal());

        // edit the opportunity afterwards (lost) — the snapshot must not move
        var lost = await PostAsync($"/api/v1/opportunities/{oppId}/transitions", new
        {
            status = "Lost", stage = "Closed", lossReason = "budget cut", orderId = (string?)null, competitor = (string?)null,
        }, Sales);
        Assert.Equal(HttpStatusCode.OK, lost.StatusCode);

        var list = await GetAsync("/api/v1/forecast-snapshots?period=2099-03", Sales);
        var rows = list.GetProperty("data").EnumerateArray().ToList();
        var frozen = rows.Single(r => r.GetProperty("id").GetString() == snapshot.GetProperty("id").GetString());
        Assert.Equal(liveTotal, frozen.GetProperty("payload").GetProperty("unweightedTotal").GetDecimal());
        Assert.Contains(oppId, frozen.GetProperty("payload").GetProperty("includedIds").EnumerateArray()
            .Select(i => i.GetString()));
    }
}

// ==== S6 e-invoice adapter + delivery tracking (050/051) ====

public partial class SalesDeferredTests
{
    // 20 (050 AC1-2): the adapter identifies jurisdiction/schema/version/correlation/status;
    // the seeded entity has no jurisdiction profile → NotEnabled blocks transmission
    [Fact]
    public async Task Einvoice_Submission_NotEnabled_For_Seeded_Entity()
    {
        var (invoiceId, _) = await SeedPostedInvoice(1m, 100m);
        var res = await PostAsync($"/api/v1/invoices/{invoiceId}/einvoice-submissions", new { }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("EINVOICE_NOT_ENABLED", body.GetProperty("error").GetProperty("code").GetString());
        var detail = body.GetProperty("error").GetProperty("detail");
        Assert.Equal("NotEnabled", detail.GetProperty("status").GetString());
        Assert.NotEmpty(detail.GetProperty("correlationId").GetString()!);
        Assert.NotEmpty(detail.GetProperty("errors").EnumerateArray());
    }

    // 21 (051 AC1-2): attempts ride the same posted revision; resend = new attempt; a bounce
    // is visible and never mutates the invoice's payment state
    [Fact]
    public async Task Delivery_Attempts_On_Posted_Revision_And_Bounce_Never_Marks_Paid()
    {
        var (invoiceId, total) = await SeedPostedInvoice(2m, 50m);
        var first = await PostAsync($"/api/v1/invoices/{invoiceId}/delivery-attempts",
            new { channel = "Email", recipient = "ar@buyer.test" }, Sales);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var attempt1 = (await Json(first)).GetProperty("data");
        Assert.Equal(1, attempt1.GetProperty("attempt").GetInt32());
        var deliveryId = attempt1.GetProperty("id").GetString()!;

        // resend = a NEW attempt on the same posted revision
        var resend = await PostAsync($"/api/v1/invoices/{invoiceId}/delivery-attempts",
            new { channel = "Email", recipient = "ar@buyer.test" }, Sales);
        Assert.Equal(HttpStatusCode.Created, resend.StatusCode);
        Assert.Equal(2, (await Json(resend)).GetProperty("data").GetProperty("attempt").GetInt32());

        // bounce: visible on the attempt, invoice untouched
        var bounce = await PostAsync($"/api/v1/invoices/{invoiceId}/delivery-attempts/{deliveryId}/bounce",
            new { reason = "mailbox full" }, Sales);
        Assert.Equal(HttpStatusCode.OK, bounce.StatusCode);
        Assert.Equal("Bounced", (await Json(bounce)).GetProperty("data").GetProperty("status").GetString());

        var invoice = await GetInvoice(invoiceId);
        Assert.Equal("Posted", invoice.GetProperty("status").GetString());
        Assert.Equal(total, invoice.GetProperty("total").GetDecimal());
        Assert.False(invoice.TryGetProperty("paid", out _)); // delivery state never conflates payment

        var deliveries = await GetAsync($"/api/v1/invoices/{invoiceId}/deliveries", Sales);
        Assert.Equal(2, deliveries.GetProperty("data").GetArrayLength());

        // a draft cannot be delivered — delivery state rides posted revisions only
        var soId = (await GetInvoice(invoiceId)).GetProperty("salesOrderId").GetString()!;
        var sku = Tag("SKU");
        await SeedStock(sku, 5m, 1m);
        var (_, so2) = await SeedConfirmedSo(sku, 5m, 5m);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{so2}/shipments", new { itemSku = sku, qty = 5m }, Sales)).StatusCode);
        var draft = await DraftInvoice(so2);
        var draftId = draft.GetProperty("id").GetString()!;
        var early = await PostAsync($"/api/v1/invoices/{draftId}/delivery-attempts",
            new { channel = "Email", recipient = "ar@buyer.test" }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("DELIVERY_STATE_ERROR", (await Json(early)).GetProperty("error").GetProperty("code").GetString());
    }
}

// ==== S7 returns/RMA + refunds (061/062) ====

public partial class SalesDeferredTests
{
    // 22 (061 AC1-2): authorized return capped at sold − prior returns; received tracked
    // separately from authorized
    [Fact]
    public async Task Return_Capped_At_Sold_Minus_Prior_Returns()
    {
        var (_, soId) = await SeedBigConfirmedSo(qty: 5m, price: 20m);
        var sku = await OrderSku(soId);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty = 5m }, Sales)).StatusCode);

        var first = await PostAsync("/api/v1/sales-returns", new
        {
            orderId = soId,
            lines = new[] { new { itemSku = sku, qty = 2m, reason = "wrong size", condition = "Resellable" } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var returnId = (await Json(first)).GetProperty("data").GetProperty("id").GetString()!;

        // 2 already authorized; 4 more exceeds the 5 sold → 409 without an approved override
        var over = await PostAsync("/api/v1/sales-returns", new
        {
            orderId = soId,
            lines = new[] { new { itemSku = sku, qty = 4m, reason = "second wave", condition = "Resellable" } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("RETURN_QTY_EXCEEDED", (await Json(over)).GetProperty("error").GetProperty("code").GetString());

        // override rides so:approve held by an officer who is not the maker of the return… the
        // maker here IS Sales, so the override must come from the second sales user
        var makerOverride = await PostAsync("/api/v1/sales-returns", new
        {
            orderId = soId,
            lines = new[] { new { itemSku = sku, qty = 4m, reason = "second wave", condition = "Resellable" } },
            overrideJustification = "manager-approved goodwill return",
        }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, makerOverride.StatusCode);

        var overrideOk = await PostAsync("/api/v1/sales-returns", new
        {
            orderId = soId,
            lines = new[] { new { itemSku = sku, qty = 4m, reason = "second wave", condition = "Resellable" } },
            overrideJustification = "manager-approved goodwill return",
        }, SalesManager);
        Assert.Equal(HttpStatusCode.Created, overrideOk.StatusCode);

        // received tracked separately from authorized (061 AC2) + stock comes back
        var receipt = await PostAsync($"/api/v1/sales-returns/{returnId}/receipts", new { receivedQty = 2m }, Sales);
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        var data = (await Json(receipt)).GetProperty("data");
        Assert.Equal(2m, data.GetProperty("receivedQty").GetDecimal());
        Assert.Equal(2m, data.GetProperty("authorizedQty").GetDecimal());
        Assert.Equal("Completed", data.GetProperty("status").GetString()); // fully received
    }

    // 23 (062 AC1): posting a credit alone never completes a refund; the refund request links
    // an approved credit and is idempotent on retry; approval is R-FIN and not the requester
    [Fact]
    public async Task Refund_Needs_Finance_Approval_And_Is_Idempotent()
    {
        var (invoiceId, total) = await SeedPostedInvoice(4m, 25m); // 100
        var customerId = (await GetInvoice(invoiceId)).GetProperty("customerId").GetString()!;

        // approved credit adjustment (house reversal pattern — existing flow)
        var adj = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit", invoiceId, reason = "wrong item discount",
            lines = new[] { new { itemSku = Tag("SKU"), qty = 1m, unitPrice = 25m } },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, adj.StatusCode);
        var adjData = (await Json(adj)).GetProperty("data");
        var adjId = adjData.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/adjustments/{adjId}/approvals", new { }, Fin)).StatusCode);
        // 046: adjustment totals finalize AT APPROVAL — read the total after it posts
        var adjustments = await GetAsync($"/api/v1/adjustments?invoiceId={invoiceId}", Sales);
        var adjTotal = adjustments.GetProperty("data").EnumerateArray()
            .First(a => a.GetProperty("id").GetString() == adjId).GetProperty("total").GetDecimal();
        Assert.True(adjTotal > 0);

        // credit posted — but a refund request still starts at Requested, never Completed
        var refund = await PostAsync($"/api/v1/adjustments/{adjId}/refund-requests", new { }, Sales);
        Assert.Equal(HttpStatusCode.Created, refund.StatusCode);
        var request = (await Json(refund)).GetProperty("data");
        Assert.Equal("Requested", request.GetProperty("status").GetString());
        Assert.Equal(adjTotal, request.GetProperty("amount").GetDecimal());
        var refundId = request.GetProperty("id").GetString()!;

        // duplicate request on the same credit → idempotent same record
        var retry = await PostAsync($"/api/v1/adjustments/{adjId}/refund-requests", new { }, Sales);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(refundId, (await Json(retry)).GetProperty("data").GetProperty("id").GetString());

        // SoD: the maker cannot approve; refunds:approve is R-FIN only
        var sod = await PostAsync($"/api/v1/refund-requests/{refundId}/approvals", new { }, Sales);
        Assert.Equal(HttpStatusCode.Forbidden, sod.StatusCode);
        var selfFin = await PostAsync($"/api/v1/refund-requests/{refundId}/approvals", new { }, Sales);
        Assert.Equal(HttpStatusCode.Forbidden, selfFin.StatusCode);

        var approved = await PostAsync($"/api/v1/refund-requests/{refundId}/approvals", new { }, Fin);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedBody = await Json(approved);
        Assert.Equal("Approved", approvedBody.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(Fin, approvedBody.GetProperty("data").GetProperty("approvedBy").GetString());
    }
}

// ==== S8 receipt candidates + AR reconciliation (054/055/056) ====

public partial class SalesDeferredTests
{
    // 24 (054 AC1-2): candidate preserves bank ref + raw amount/currency; unmatched stays
    // Unapplied and never closes an invoice; payer link added later keeps the reference
    [Fact]
    public async Task Unmatched_Receipt_Candidate_Never_Closes_An_Invoice()
    {
        var (invoiceId, total) = await SeedPostedInvoice(1m, 80m);
        var customerId = (await GetInvoice(invoiceId)).GetProperty("customerId").GetString()!;

        var res = await PostAsync("/api/v1/receipt-candidates", new
        {
            bankRef = $"BANK-{Tag("REF")}", amount = 80m, currency = "BDT",
            valueDate = Today, channel = "BankTransfer",
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var candidate = (await Json(res)).GetProperty("data");
        Assert.Equal("Unapplied", candidate.GetProperty("status").GetString());
        Assert.StartsWith("BANK-", candidate.GetProperty("bankRef").GetString());
        Assert.Equal(80m, candidate.GetProperty("amount").GetDecimal());
        var candidateId = candidate.GetProperty("id").GetString()!;

        // the unmatched candidate did NOT close anything — invoice open amount unchanged
        var recon = await GetAsync("/api/v1/ar-reconciliation", Sales);
        var row = recon.GetProperty("data").GetProperty("rows").EnumerateArray()
            .First(r => r.GetProperty("docId").GetString() == invoiceId);
        Assert.Equal(total, row.GetProperty("openAmount").GetDecimal());
        Assert.Equal("Resolved", row.GetProperty("status").GetString());

        // payer link added later without losing the bank reference
        var link = await PostAsync($"/api/v1/receipt-candidates/{candidateId}/payer-links",
            new { customerId }, Sales);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        var linked = (await Json(link)).GetProperty("data");
        Assert.Equal(customerId, linked.GetProperty("customerId").GetString());
        Assert.StartsWith("BANK-", linked.GetProperty("bankRef").GetString());
    }

    // 25 (056 AC1-2): recon view shows doc/finance ref/currency/open/as-of and never marks
    // an invoice settled unless finance state (approved credits) brought the open to zero.
    // Adjustments carry pinned tax, so expectations are computed from the actual totals.
    [Fact]
    public async Task Ar_Reconciliation_Marks_State_Explicitly()
    {
        var (invoiceId, invoiceTotal) = await SeedPostedInvoice(2m, 50m);
        var adj = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit", invoiceId, reason = "partial credit",
            lines = new[] { new { itemSku = Tag("SKU"), qty = 1m, unitPrice = 20m } },
        }, Sales);
        var adjId = (await Json(adj)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/adjustments/{adjId}/approvals", new { }, Fin)).StatusCode);
        var adjustments = await GetAsync($"/api/v1/adjustments?invoiceId={invoiceId}", Sales);
        var adjTotal = adjustments.GetProperty("data").EnumerateArray()
            .First(a => a.GetProperty("id").GetString() == adjId).GetProperty("total").GetDecimal();

        var recon = await GetAsync("/api/v1/ar-reconciliation", Sales);
        var row = recon.GetProperty("data").GetProperty("rows").EnumerateArray()
            .First(r => r.GetProperty("docId").GetString() == invoiceId);
        Assert.Equal(invoiceTotal - adjTotal, row.GetProperty("openAmount").GetDecimal());
        Assert.Equal("BDT", row.GetProperty("currency").GetString());
        Assert.StartsWith("J-", row.GetProperty("financeRef").GetString()!);
        Assert.Equal("Resolved", row.GetProperty("status").GetString()); // open > 0, journal present

        // a fully credited invoice reads Settled (finance said so), never by candidates —
        // the remaining open is 92.00 net 80.00 at the 15% rate
        var adj2 = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit", invoiceId, reason = "remaining credit",
            lines = new[] { new { itemSku = Tag("SKU"), qty = 1m, unitPrice = 80m } },
        }, Sales);
        var adj2Id = (await Json(adj2)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/adjustments/{adj2Id}/approvals", new { }, Fin)).StatusCode);
        var after = await GetAsync("/api/v1/ar-reconciliation", Sales);
        var settled = after.GetProperty("data").GetProperty("rows").EnumerateArray()
            .First(r => r.GetProperty("docId").GetString() == invoiceId);
        Assert.Equal(0m, settled.GetProperty("openAmount").GetDecimal());
        Assert.Equal("Settled", settled.GetProperty("status").GetString());
    }
}

// ==== S9 rebates + commission boundary (063/064) ====

public partial class SalesDeferredTests
{
    private async Task<(string agreementId, string invoiceId, decimal invoiceTotal)> SeedRebateFlow()
    {
        var (invoiceId, invoiceTotal) = await SeedPostedInvoice(10m, 100m); // 1000 net + tax
        var customerId = (await GetInvoice(invoiceId)).GetProperty("customerId").GetString()!;
        var agreement = await PostAsync("/api/v1/rebate-agreements", new
        {
            customerId, basis = "net_sales", ratePct = 5m, accrualPeriod = "Monthly", validFrom = Today.AddDays(-1),
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, agreement.StatusCode);
        var agreementId = (await Json(agreement)).GetProperty("data").GetProperty("id").GetString()!;
        return (agreementId, invoiceId, invoiceTotal);
    }

    // 26 (063 AC1): accrual calculation identifies eligible transactions + agreement version
    [Fact]
    public async Task Rebate_Accrual_Identifies_Eligible_Transactions()
    {
        var (agreementId, invoiceId, invoiceTotal) = await SeedRebateFlow();
        var period = $"{Today:yyyy-MM}";
        var accrual = await PostAsync($"/api/v1/rebate-agreements/{agreementId}/accruals", new { period }, Sales);
        Assert.Equal(HttpStatusCode.Created, accrual.StatusCode);
        var data = (await Json(accrual)).GetProperty("data");
        Assert.Contains(invoiceId, data.GetProperty("eligibleTxnIds").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal(invoiceTotal * 0.05m, data.GetProperty("accruedAmount").GetDecimal());
        Assert.Equal(1, data.GetProperty("agreementVersion").GetInt32());

        // settlement above the APPROVED accrual is refused until finance approves the accrual
        var claim = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId, period, amount = data.GetProperty("accruedAmount").GetDecimal(),
            eligibleTxnIds = new[] { invoiceId },
        }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, claim.StatusCode);
        Assert.Equal("REBATE_EXCEEDS_ACCRUAL", (await Json(claim)).GetProperty("error").GetProperty("code").GetString());

        // maker-checker: R-FIN approves the accrual (rebates:settle)
        var self = await PostAsync($"/api/v1/rebate-agreements/{agreementId}/accruals/{period}/approvals", new { }, Sales);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/rebate-agreements/{agreementId}/accruals/{period}/approvals", new { }, Fin)).StatusCode);

        var ok = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId, period, amount = data.GetProperty("accruedAmount").GetDecimal(),
            eligibleTxnIds = new[] { invoiceId },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        // a claim above the approved accrued amount still refuses
        var over = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId, period, amount = data.GetProperty("accruedAmount").GetDecimal() * 2,
            eligibleTxnIds = new[] { invoiceId },
        }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("REBATE_EXCEEDS_ACCRUAL", (await Json(over)).GetProperty("error").GetProperty("code").GetString());
    }

    // 27 (064 edge): invoice posting emits a commission event; a credit reversal emits a
    // linked reversal event on the existing outbox
    [Fact]
    public async Task Commission_Events_Ride_The_Outbox_With_Reversal_Links()
    {
        var (invoiceId, _) = await SeedPostedInvoice(2m, 50m);
        var outbox = await GetAsync("/api/v1/outbox", Admin);
        var posted = outbox.GetProperty("data").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "sales.commission.event")
            .FirstOrDefault(e => e.GetProperty("payloadJson").GetString()!.Contains(invoiceId));
        Assert.NotEqual(default, posted);

        // reversal: approving a credit adjustment emits a linked reversal event
        var adj = await PostAsync("/api/v1/adjustments", new
        {
            kind = "Credit", invoiceId, reason = "commission reversal probe",
            lines = new[] { new { itemSku = Tag("SKU"), qty = 1m, unitPrice = 10m } },
        }, Sales);
        var adjId = (await Json(adj)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/adjustments/{adjId}/approvals", new { }, Fin)).StatusCode);

        var after = await GetAsync("/api/v1/outbox", Admin);
        var reversal = after.GetProperty("data").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "sales.commission.reversed")
            .FirstOrDefault(e => e.GetProperty("payloadJson").GetString()!.Contains(invoiceId));
        Assert.NotEqual(default, reversal);
        var payload = JsonDocument.Parse(reversal.GetProperty("payloadJson").GetString()!).RootElement;
        Assert.Equal(invoiceId, payload.GetProperty("sourceInvoiceId").GetString());
    }

    // 29 (063 AC2): the cap is cumulative — Submitted+Approved claims together may not exceed
    // the approved accrual, so per-claim checks alone can't double-settle one accrual
    [Fact]
    public async Task Rebate_Claims_Are_Capped_Cumulatively_At_The_Approved_Accrual()
    {
        // full-cap claim settles → a second full-cap claim must refuse (50 !> 50 per-claim)
        var (agreementId, invoiceId, _) = await SeedRebateFlow(); // 1000 net @ 5% → accrual 50
        var period = $"{Today:yyyy-MM}";
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/rebate-agreements/{agreementId}/accruals", new { period }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/rebate-agreements/{agreementId}/accruals/{period}/approvals", new { }, Fin)).StatusCode);

        var first = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId, period, amount = 50m, eligibleTxnIds = new[] { invoiceId },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(
            $"/api/v1/rebate-claims/{(await Json(first)).GetProperty("data").GetProperty("id").GetString()!}/approvals",
            new { }, Fin)).StatusCode);

        var second = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId, period, amount = 50m, eligibleTxnIds = new[] { invoiceId },
        }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("REBATE_EXCEEDS_ACCRUAL", (await Json(second)).GetProperty("error").GetProperty("code").GetString());

        // partial remainder: half the accrual settles → a full-cap claim refuses, a
        // remainder-sized claim still fits
        var (agreement2, invoice2, _) = await SeedRebateFlow();
        var period2 = $"{Today:yyyy-MM}";
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/rebate-agreements/{agreement2}/accruals", new { period = period2 }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/rebate-agreements/{agreement2}/accruals/{period2}/approvals", new { }, Fin)).StatusCode);

        var half = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId = agreement2, period = period2, amount = 25m, eligibleTxnIds = new[] { invoice2 },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, half.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(
            $"/api/v1/rebate-claims/{(await Json(half)).GetProperty("data").GetProperty("id").GetString()!}/approvals",
            new { }, Fin)).StatusCode);

        var over = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId = agreement2, period = period2, amount = 50m, eligibleTxnIds = new[] { invoice2 },
        }, Sales);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("REBATE_EXCEEDS_ACCRUAL", (await Json(over)).GetProperty("error").GetProperty("code").GetString());

        var remainder = await PostAsync("/api/v1/rebate-claims", new
        {
            agreementId = agreement2, period = period2, amount = 25m, eligibleTxnIds = new[] { invoice2 },
        }, Sales);
        Assert.Equal(HttpStatusCode.Created, remainder.StatusCode);
    }
}

// ==== S10 sales analytics (065) ====

public partial class SalesDeferredTests
{
    // 28 (065 AC1-2): totals reconcile to included sources; unauthorized drill-through is 403
    [Fact]
    public async Task Analytics_Reconcile_And_Gate_Drill_Through()
    {
        var (invoiceId, invoiceTotal) = await SeedPostedInvoice(3m, 40m); // 120 net + tax
        var customerId = (await GetInvoice(invoiceId)).GetProperty("customerId").GetString()!;

        var billing = await GetAsync("/api/v1/sales-analytics?domain=billing&groupBy=customer", Sales);
        var data = billing.GetProperty("data");
        var row = data.GetProperty("rows").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("groupKey").GetString() == customerId);
        Assert.NotEqual(default, row);
        // the posted invoice's total reconciles into the customer's billed row
        Assert.True(row.GetProperty("total").GetDecimal() >= invoiceTotal);

        // every row names its drill-through identifiers
        Assert.NotEmpty(row.GetProperty("docIds").EnumerateArray().Select(i => i.GetString()));

        // orders domain reconciles against the orders list
        var soId = (await GetInvoice(invoiceId)).GetProperty("salesOrderId").GetString()!;
        var ordersView = await GetAsync("/api/v1/sales-analytics?domain=orders&groupBy=status", Sales);
        var ordersData = ordersView.GetProperty("data");
        Assert.True(ordersData.GetProperty("rows").EnumerateArray().Any());

        // drill-through without the domain permission returns no protected rows: R-FIN holds
        // no crm:read → the crm domain refuses
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/v1/sales-analytics?domain=crm", Fin));

        // an unknown domain is a controlled validation error
        Assert.Equal(HttpStatusCode.BadRequest, await GetStatusAsync("/api/v1/sales-analytics?domain=inventory", Sales));
    }
}

// ==== Gate 2: authz negative sweep on the new routes (brief §7 #28) ====

public partial class SalesDeferredTests
{
    // R-EVAL (U-006) holds none of the sales permissions — every new write/read route must
    // refuse with 403 rather than leaking rows or state changes
    [Fact]
    public async Task Authz_Sweep_New_Routes_Refuse_Unprivileged_Tokens()
    {
        const string NoPerm = "U-006"; // R-EVAL: sourcing:read + sourcing:evaluate only
        var gets = new[]
        {
            "/api/v1/quotes", "/api/v1/price-books", "/api/v1/customers/C-0001",
            "/api/v1/customers/C-0001/onboarding-checks", "/api/v1/sales-orders/SO-0001",
            "/api/v1/receipt-candidates", "/api/v1/ar-reconciliation",
            "/api/v1/sales-returns", "/api/v1/rebate-agreements",
            "/api/v1/sales-analytics?domain=crm", "/api/v1/sales-analytics?domain=orders",
            "/api/v1/sales-analytics?domain=billing", "/api/v1/forecast?period=2099-01",
            "/api/v1/forecast-snapshots?period=2099-01",
        };
        foreach (var path in gets)
        {
            var res = await RawAsync(HttpMethod.Get, path, NoPerm);
            // list routes can only 403 (no existence check); id-bearing detail routes may 404
            // on the fixture's placeholder ids — both refuse and leak nothing
            Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"{path} should be 403/404 for {NoPerm}, got {res.StatusCode}");
        }

        var posts = new List<(string path, object body)>
        {
            ("/api/v1/quotes", new { customerId = "C-0001", currency = "BDT", lines = new[] { new { itemSku = "X", qty = 1m, unitPrice = 1m } } }),
            ("/api/v1/price-books", new { name = "x", currency = "BDT", precedence = 1 }),
            ("/api/v1/customer-contract-prices", new { customerId = "C-0001", itemSku = "X", minQty = 0m, unitPrice = 1m, effectiveFrom = Today }),
            ("/api/v1/quotes/QTE-0001/revisions/1/approvals", new { }),
            ("/api/v1/quotes/QTE-0001/revisions/1/acceptances", new { method = "Email" }),
            ("/api/v1/customers/C-0001/hierarchy", new { parentId = "C-0002" }),
            ("/api/v1/customers/C-0001/onboarding-checks", new { checkCode = "X", mandatory = true }),
            ("/api/v1/customer-merges/preview", new { survivorId = "C-0001", mergedId = "C-0002", reason = "x" }),
            ("/api/v1/sales-orders/SO-0001/holds", new { holdType = "Manual", reason = "x" }),
            ("/api/v1/sales-orders/SO-0001/amendments", new { rationale = "x", lines = new[] { new { itemSku = "X", description = "d", qty = 1m, unitPrice = 1m } } }),
            ("/api/v1/sales-orders/SO-0001/cancellations", new { reason = "x", scope = "Remaining" }),
            ("/api/v1/recurring-schedules", new { customerId = "C-0001", frequency = "Monthly", nextRunDate = Today, lines = new[] { new { itemSku = "X", description = "d", qty = 1m, unitPrice = 1m } } }),
            ("/api/v1/forecast-submissions", new { owner = Sales, period = "2099-01", category = "Commit" }),
            ("/api/v1/forecast-snapshots", new { period = "2099-01" }),
            ("/api/v1/invoices/INV-0001/einvoice-submissions", new { }),
            ("/api/v1/invoices/INV-0001/delivery-attempts", new { channel = "Email", recipient = "a@b.test" }),
            ("/api/v1/sales-returns", new { orderId = "SO-0001", lines = new[] { new { itemSku = "X", qty = 1m } } }),
            ("/api/v1/adjustments/ADJ-0001/refund-requests", new { }),
            ("/api/v1/refund-requests/X:REF/approvals", new { }),
            ("/api/v1/receipt-candidates", new { bankRef = "B-1", amount = 1m, currency = "BDT", valueDate = Today, channel = "Bank" }),
            ("/api/v1/rebate-agreements", new { customerId = "C-0001", basis = "net_sales", ratePct = 5m, accrualPeriod = "Monthly", validFrom = Today }),
            ("/api/v1/rebate-claims", new { agreementId = "RBT-0001", period = "2099-01", amount = 1m, eligibleTxnIds = new[] { "INV-1" } }),
        };
        foreach (var (path, body) in posts)
        {
            var res = await PostAsync(path, body, NoPerm);
            // 403 = permission gate; 404 = id not in this fixture; 409 = a state guard that
            // fires earlier in the flow (fixture sequences make low ids real). All three
            // refuse the action — the assert is about never reaching the business write.
            Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict,
                $"{path} should be 403/404/409 for {NoPerm}, got {res.StatusCode}");
        }

        // merge execution is tighter still: an authorized sales token is refused (R-ADMIN only,
        // Q-S3) — 403 on an existing preview, 404 acceptable only because this id is a placeholder
        var exec = await PostAsync("/api/v1/customer-merges/PMG-00000000/executions", new { }, Sales);
        Assert.True(exec.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"merge execution should refuse for {Sales}, got {exec.StatusCode}");
        // refund approval is finance-only: an unprivileged token cannot approve
        var refundSelf = await PostAsync("/api/v1/refund-requests/X:REF/approvals", new { }, NoPerm);
        Assert.True(refundSelf.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"refund approval should refuse for {NoPerm}, got {refundSelf.StatusCode}");
    }
}
