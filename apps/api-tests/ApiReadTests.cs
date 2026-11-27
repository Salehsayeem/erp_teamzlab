using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

// READ-ONLY composition/detail endpoints for the UI screens (page-reload reads):
// GET /api/v1/sourcing-events/{id} (sealed-aware composite), GET /api/v1/awards?eventId=,
// GET /api/v1/contracts/{id}, GET /api/v1/suppliers/{id}/gates,
// GET /api/v1/adjustments?invoiceId=, GET /api/v1/invoices/{id}/reminders|disputes.
// Conventions follow ProcurementDeepTests/SalesDeepTests: throwaway per-class DB, seeded
// through the API; the tenant-scoping proof shifts a row's tenant/entity via raw SQL (the only
// write a test makes outside the API). Makers/checkers mirror the deep-slice roles:
// U-002 procurement (R-PROC), U-003 finance (sourcing:approve/contracts:approve), U-004 sales
// (invoices:read + collections:read), U-006 evaluator, U-001 admin (*).

namespace Erp.Api.Tests;

public class ApiReadTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public ApiReadTests(TestApiFactory factory) => _factory = factory;

    private const string Proc = "U-002";   // R-PROC — sourcing/contracts/suppliers read + write
    private const string Fin = "U-003";    // R-FIN — sourcing:approve / contracts:approve / invoices:approve
    private const string Sales = "U-004";  // R-SALES — invoices:read/write, collections:read/write
    private const string Admin = "U-001";  // R-ADMIN — *

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

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

    private async Task<JsonElement> PostForData(string path, object body, string token, string? idemKey = null)
    {
        var res = await PostAsync(path, body, token, idemKey);
        Assert.True(res.IsSuccessStatusCode, $"{path} → {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await Json(res)).GetProperty("data");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static string ErrorCode(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

    private static bool HasProp(JsonElement row, string name) => row.TryGetProperty(name, out _);

    // ==================== seeds (API-only) ====================

    private async Task<string> SeedSupplier(string tag)
    {
        var data = await PostForData("/api/v1/suppliers",
            new { name = $"Supplier {tag}", taxId = $"TRX-{tag}", email = $"{tag.ToLowerInvariant()}@sup.test" }, Proc);
        return data.GetProperty("id").GetString()!;
    }

    private async Task<string> AddGate(string supplierId, string gateCode, string severity, DateOnly? exceptionUntil = null)
    {
        var data = await PostForData($"/api/v1/suppliers/{supplierId}/gates", new
        {
            gateCode,
            severity,
            effectiveFrom = Today,
            effectiveTo = (DateOnly?)null,
            evidenceRef = (string?)null,
            exceptionUntil,
        }, Proc);
        return data.GetProperty("id").GetString()!;
    }

    // published event with one lot, criteria summing to 100 and an invitation per supplier
    private async Task<(string eventId, string lotId, Dictionary<string, string> criteria)> SeedPublishedEvent(
        string tag, string[]? suppliers = null)
    {
        var data = await PostForData("/api/v1/sourcing-events", new
        {
            title = $"Sourcing {tag}",
            eventType = "Rfq",
            opensAt = DateTimeOffset.UtcNow.AddHours(-1),
            closesAt = DateTimeOffset.UtcNow.AddHours(4),
            timeZoneName = "Asia/Dhaka",
            currency = "BDT",
        }, Proc);
        var eventId = data.GetProperty("id").GetString()!;
        var lotId = (await PostForData($"/api/v1/sourcing-events/{eventId}/lots",
            new { lotNo = "L1", description = $"Lot {tag}", itemSku = $"SKU-{tag}", targetQty = 10m, ceilingValue = 100000m }, Proc))
            .GetProperty("id").GetString()!;
        var ids = new Dictionary<string, string>();
        foreach (var (code, kind, weight, scale) in new[]
                 { ("PRICE", "Scored", 60m, 10), ("DELIVERY", "Scored", 30m, 5), ("ISO", "PassFail", 10m, 0) })
            ids[code] = (await PostForData($"/api/v1/sourcing-events/{eventId}/criteria",
                new { code, name = $"{code} {tag}", kind, weightPct = weight, scaleMax = scale }, Proc)).GetProperty("id").GetString()!;
        foreach (var supplierId in suppliers ?? [])
            await PostForData($"/api/v1/sourcing-events/{eventId}/invitations",
                new { supplierIds = new[] { supplierId } }, Proc);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sourcing-events/{eventId}/publications", new { }, Proc)).StatusCode);
        return (eventId, lotId, ids);
    }

    private async Task<string> SubmitBid(string eventId, string supplierId, string lotId)
    {
        var data = await PostForData($"/api/v1/sourcing-events/{eventId}/bids",
            new { supplierId, lines = new[] { new { lotId, qty = 5m, unitPrice = 100m, discountPct = 10m } } }, Proc);
        return data.GetProperty("bidId").GetString()!;
    }

    // the bid-opening gate compares wall-clock instants — shift closes_at into the past on the
    // throwaway fixture database (test-only write outside the API, ProcurementDeepTests style)
    private async Task CloseEvent(string eventId)
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sourcing_events SET closes_at = $1 WHERE id = $2", conn);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddMinutes(-5));
        cmd.Parameters.AddWithValue(eventId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task<string> SeedRecommendedAward(string tag, string supplierId)
    {
        var (eventId, lotId, _) = await SeedPublishedEvent(tag, [supplierId]);
        await SubmitBid(eventId, supplierId, lotId);
        await CloseEvent(eventId);
        var data = await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId, qty = 5m, unitPrice = 100m, recommendation = $"Best value {tag}" }, Proc);
        return data.GetProperty("id").GetString()!;
    }

    // supplier → approved award → contract → price-schedule amendment → activation
    private async Task<string> SeedActiveContract(string tag, string supplierId)
    {
        var awardId = await SeedRecommendedAward($"{tag}-CTR", supplierId);
        await PostForData($"/api/v1/awards/{awardId}/approvals", new { reason = (string?)null }, Fin);
        var data = await PostForData("/api/v1/contracts", new
        {
            supplierId,
            awardId,
            currency = "BDT",
            effectiveFrom = Today,
            effectiveTo = Today.AddDays(15),
            renewalNoticeDays = (int?)30,
            optionPeriodsTotal = (int?)1,
        }, Proc);
        var contractId = data.GetProperty("id").GetString()!;
        await PostForData($"/api/v1/contracts/{contractId}/amendments", new
        {
            effectiveDate = Today,
            summary = $"Initial price schedule {tag}",
            priceLines = new[] { new { itemSku = $"SKU-{tag}-CTR", unitPrice = 100m, effectiveFrom = Today, effectiveTo = (DateOnly?)null } },
        }, Proc);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/contracts/{contractId}/activations", new { }, Fin)).StatusCode);
        return contractId;
    }

    // stock → confirmed SO → shipment → posted invoice (SalesDeepTests chain, trimmed)
    private async Task<string> SeedPostedInvoice(string tag, decimal qty, decimal price)
    {
        var sku = $"SKU-{tag}";
        var supplierId = await SeedSupplier($"{tag}-SUP");
        var poId = (await PostForData("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = 10m } },
        }, Proc)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, Proc)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = sku, qty }, Proc)).StatusCode);

        var customerId = (await PostForData("/api/v1/customers",
            new { name = $"Buyer {tag}", taxId = $"TRX-{tag}", email = $"ap.{tag.ToLowerInvariant()}@buyer.test" }, Sales))
            .GetProperty("id").GetString()!;
        var soId = (await PostForData("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = sku, description = $"Item {sku}", qty, unitPrice = price } },
        }, Sales)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/v1/sales-orders/{soId}/shipments", new { itemSku = sku, qty }, Sales)).StatusCode);
        var draftId = (await PostForData($"/api/v1/sales-orders/{soId}/invoice-drafts", new { }, Sales)).GetProperty("id").GetString()!;
        var posted = await PostForData($"/api/v1/invoices/{draftId}/postings", new { }, Fin);
        return posted.GetProperty("invoice").GetProperty("id").GetString()!;
    }

    // ==================== 1. GET /api/v1/sourcing-events/{id} ====================

    // composition read: event + lots + criteria + publication + invitations (+ gate decorations)
    // + Q&A + bids + awards; FIX 1 mirrored — while sealed, a caller WITHOUT sourcing:approve
    // sees neither bid prices NOR contentDigest (and no award unitPrice); the approver does.
    [Fact]
    public async Task Sourcing_Event_Detail_Composes_Event_And_Hides_Sealed_Digest_And_Prices()
    {
        var supplierId = await SeedSupplier("R1");
        await AddGate(supplierId, "FIN-DISCLOSE", "Warning"); // gate decoration on the invitation
        var (eventId, lotId, criteria) = await SeedPublishedEvent("R1", [supplierId]);
        await PostForData($"/api/v1/sourcing-events/{eventId}/questions",
            new { question = "Sample needed?", answer = "Yes, by Dec 5", scope = "EventWide", publish = true, supplierId = supplierId }, Proc);
        var bidId = await SubmitBid(eventId, supplierId, lotId);

        var detail = (await GetAsync($"/api/v1/sourcing-events/{eventId}", Proc)).GetProperty("data");
        Assert.Equal(eventId, detail.GetProperty("event").GetProperty("id").GetString());
        Assert.Equal("Published", detail.GetProperty("event").GetProperty("status").GetString());
        Assert.Equal(2, detail.GetProperty("event").GetProperty("version").GetInt32()); // bumped by the published Q&A
        Assert.True(detail.GetProperty("publication").GetProperty("published").GetBoolean());
        Assert.Equal(1, detail.GetProperty("lots").GetArrayLength());
        Assert.Equal(lotId, detail.GetProperty("lots")[0].GetProperty("id").GetString());
        Assert.Equal(3, detail.GetProperty("criteria").GetArrayLength());
        Assert.Equal("Sample needed?", detail.GetProperty("qa")[0].GetProperty("question").GetString());
        var invitation = detail.GetProperty("invitations")[0];
        Assert.Equal(supplierId, invitation.GetProperty("supplierId").GetString());
        Assert.Contains("FIN-DISCLOSE", invitation.GetProperty("warningGates").GetString());
        Assert.Equal("FIN-DISCLOSE", invitation.GetProperty("gates")[0].GetProperty("gateCode").GetString());

        // sealed: the buyer (no sourcing:approve) sees neither digest nor prices, on bids or awards
        var raw = await (await GetRawAsync($"/api/v1/sourcing-events/{eventId}", Proc)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("contentDigest", raw);
        Assert.DoesNotContain("unitPrice", raw);
        Assert.False(HasProp(detail.GetProperty("bids")[0], "contentDigest"));
        Assert.Equal(bidId, detail.GetProperty("bids")[0].GetProperty("bidId").GetString());
        Assert.False(HasProp(detail.GetProperty("bids")[0].GetProperty("lines")[0], "unitPrice"));

        // approver view keeps the sealed payload visible (R-T6b)
        var approverDetail = (await GetAsync($"/api/v1/sourcing-events/{eventId}", Fin)).GetProperty("data");
        Assert.Equal(64, approverDetail.GetProperty("bids")[0].GetProperty("contentDigest").GetString()!.Length);
        Assert.True(HasProp(approverDetail.GetProperty("bids")[0].GetProperty("lines")[0], "unitPrice"));

        await CloseEvent(eventId);
        var recommended = await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId, qty = 5m, unitPrice = 100m, recommendation = "best value" }, Proc);
        var sealedDetail = (await GetAsync($"/api/v1/sourcing-events/{eventId}", Proc)).GetProperty("data");
        var awardRow = sealedDetail.GetProperty("awards")[0];
        Assert.Equal(recommended.GetProperty("id").GetString(), awardRow.GetProperty("id").GetString());
        Assert.Equal("Recommended", awardRow.GetProperty("status").GetString());
        Assert.Equal(supplierId, awardRow.GetProperty("supplierId").GetString());
        Assert.False(HasProp(awardRow, "unitPrice")); // bids unopened → award price stays sealed

        // approved opening unseals prices for everyone (who/when recorded)
        await PostForData($"/api/v1/sourcing-events/{eventId}/bid-openings", new { }, Fin);
        var openedDetail = (await GetAsync($"/api/v1/sourcing-events/{eventId}", Proc)).GetProperty("data");
        Assert.Contains("contentDigest", await (await GetRawAsync($"/api/v1/sourcing-events/{eventId}", Proc)).Content.ReadAsStringAsync());
        Assert.True(HasProp(openedDetail.GetProperty("bids")[0].GetProperty("lines")[0], "unitPrice"));
        Assert.True(HasProp(openedDetail.GetProperty("awards")[0], "unitPrice"));

        // missing id → 404
        var res = await GetRawAsync("/api/v1/sourcing-events/EV-9999", Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("EVENT_NOT_FOUND", ErrorCode(await Json(res)));
    }

    // tenant+entity scoping: a row moved to another tenant/entity is invisible → 404
    [Fact]
    public async Task Sourcing_Event_Detail_Is_Tenant_Scoped()
    {
        var supplierId = await SeedSupplier("R2");
        var (eventId, _, _) = await SeedPublishedEvent("R2", [supplierId]);
        Assert.Equal(HttpStatusCode.OK, (await GetRawAsync($"/api/v1/sourcing-events/{eventId}", Proc)).StatusCode);

        await using (var conn = new NpgsqlConnection(_factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE sourcing_events SET tenant_id = 'T-009', legal_entity_id = 'LE-009' WHERE id = $1", conn);
            cmd.Parameters.AddWithValue(eventId);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        var res = await GetRawAsync($"/api/v1/sourcing-events/{eventId}", Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("EVENT_NOT_FOUND", ErrorCode(await Json(res)));
    }

    // ==================== 2. GET /api/v1/awards?eventId= ====================

    [Fact]
    public async Task Awards_For_Event_List_Summary_With_Sealed_Price_Rule()
    {
        var supplierId = await SeedSupplier("R3");
        var (eventId, lotId, _) = await SeedPublishedEvent("R3", [supplierId]);
        await SubmitBid(eventId, supplierId, lotId);
        await CloseEvent(eventId);
        var award = await PostForData("/api/v1/awards",
            new { eventId, lotId, supplierId, qty = 4m, unitPrice = 100m, recommendation = "best value" }, Proc);

        // sealed (bids unopened): the buyer gets the summary without the price
        var list = (await GetAsync($"/api/v1/awards?eventId={eventId}", Proc)).GetProperty("data");
        Assert.Equal(1, list.GetArrayLength());
        var row = list[0];
        Assert.Equal(award.GetProperty("id").GetString(), row.GetProperty("id").GetString());
        Assert.Equal(supplierId, row.GetProperty("supplierId").GetString());
        Assert.Equal(lotId, row.GetProperty("lotId").GetString());
        Assert.Equal(4m, row.GetProperty("qty").GetDecimal());
        Assert.Equal("Recommended", row.GetProperty("status").GetString());
        Assert.False(HasProp(row, "unitPrice"));

        // the approver sees the price while sealed
        var approverRow = (await GetAsync($"/api/v1/awards?eventId={eventId}", Fin)).GetProperty("data")[0];
        Assert.Equal(100m, approverRow.GetProperty("unitPrice").GetDecimal());

        // after the approved opening the price unseals for everyone
        await PostForData($"/api/v1/sourcing-events/{eventId}/bid-openings", new { }, Fin);
        var openedRow = (await GetAsync($"/api/v1/awards?eventId={eventId}", Proc)).GetProperty("data")[0];
        Assert.Equal(100m, openedRow.GetProperty("unitPrice").GetDecimal());

        // negative paths: unknown event → 404; missing query param → 400
        var res = await GetRawAsync("/api/v1/awards?eventId=EV-9999", Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("EVENT_NOT_FOUND", ErrorCode(await Json(res)));
        res = await GetRawAsync("/api/v1/awards", Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));
    }

    // ==================== 3. GET /api/v1/contracts/{id} ====================

    [Fact]
    public async Task Contract_Detail_Returns_Amendments_PriceLines_Obligations_Renewal()
    {
        var supplierId = await SeedSupplier("R4");
        var contractId = await SeedActiveContract("R4", supplierId);
        var obligation = await PostForData($"/api/v1/contracts/{contractId}/obligations",
            new { obligationType = "Delivery plan", ownerUserId = Proc, dueDate = Today.AddDays(-1), sourceClause = (string?)"Cl. 4.1" }, Proc);
        await PostForData($"/api/v1/contracts/{contractId}/obligations/{obligation.GetProperty("id").GetString()!}/evidence",
            new { evidenceRef = "doc://grn-7788" }, Proc);
        // renewal exercised by the checker (approver ≠ creator)
        await PostForData($"/api/v1/contracts/{contractId}/renewals", new { optionNumber = 1 }, Fin);

        var detail = (await GetAsync($"/api/v1/contracts/{contractId}", Proc)).GetProperty("data");
        Assert.Equal(contractId, detail.GetProperty("contract").GetProperty("id").GetString());
        Assert.Equal("Active", detail.GetProperty("contract").GetProperty("status").GetString());
        Assert.Equal(1, detail.GetProperty("amendments").GetArrayLength());
        Assert.Equal(2, detail.GetProperty("amendments")[0].GetProperty("version").GetInt32()); // ordered by version
        Assert.Equal($"SKU-R4-CTR", detail.GetProperty("priceLines")[0].GetProperty("itemSku").GetString());
        Assert.Equal(100m, detail.GetProperty("priceLines")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal("EvidenceSubmitted", detail.GetProperty("obligations")[0].GetProperty("status").GetString());
        Assert.True(detail.GetProperty("obligations")[0].GetProperty("overdue").GetBoolean());

        var renewal = detail.GetProperty("renewal");
        Assert.Equal(1, renewal.GetProperty("optionsExercised").GetInt32());
        Assert.Equal(1, renewal.GetProperty("optionPeriodsTotal").GetInt32());
        Assert.Equal(0, renewal.GetProperty("optionsRemaining").GetInt32());
        Assert.True(renewal.GetProperty("noticeDue").GetBoolean()); // 30-day notice inside the short remaining term
        Assert.Equal(Today.AddDays(30), DateOnly.Parse(renewal.GetProperty("effectiveTo").GetString()!)); // 15d term extended by 15d

        var res = await GetRawAsync("/api/v1/contracts/CTR-9999", Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("CONTRACT_NOT_FOUND", ErrorCode(await Json(res)));

        // permission family: contracts:read — sales carries neither it nor contracts:write
        res = await GetRawAsync($"/api/v1/contracts/{contractId}", Sales);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(res)));
    }

    // ==================== 4. GET /api/v1/suppliers/{id}/gates ====================

    [Fact]
    public async Task Supplier_Gates_List_Status_And_Exception_Windows()
    {
        var supplierId = await SeedSupplier("R5");
        await AddGate(supplierId, "TAX-CLEAR", "Hard", exceptionUntil: Today.AddDays(30));
        await AddGate(supplierId, "FIN-DISCLOSE", "Warning");

        var gates = (await GetAsync($"/api/v1/suppliers/{supplierId}/gates", Proc)).GetProperty("data");
        Assert.Equal(2, gates.GetArrayLength());
        var hard = gates.EnumerateArray().First(g => g.GetProperty("gateCode").GetString() == "TAX-CLEAR");
        Assert.Equal("Hard", hard.GetProperty("severity").GetString());
        Assert.Equal(Today, DateOnly.Parse(hard.GetProperty("effectiveFrom").GetString()!));
        Assert.Equal(Proc, hard.GetProperty("exceptionBy").GetString());
        Assert.NotNull(hard.GetProperty("exceptionUntil").GetString());
        var warning = gates.EnumerateArray().First(g => g.GetProperty("gateCode").GetString() == "FIN-DISCLOSE");
        Assert.Equal("Warning", warning.GetProperty("severity").GetString());
        Assert.Equal(supplierId, warning.GetProperty("supplierId").GetString());

        var res = await GetRawAsync("/api/v1/suppliers/SUP-9999/gates", Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("SUPPLIER_NOT_FOUND", ErrorCode(await Json(res)));
    }

    // ==================== 5. GET /api/v1/adjustments?invoiceId= ====================

    [Fact]
    public async Task Adjustments_For_Invoice_List_Kind_Status_Totals()
    {
        var invoiceId = await SeedPostedInvoice("R6", 5m, 100m);
        var credit = await PostForData("/api/v1/adjustments", new
        {
            kind = "Credit",
            invoiceId,
            reason = "Returned 1 unit",
            lines = new[] { new { itemSku = "SKU-R6", qty = 1m, unitPrice = 100m, taxCodeId = "VAT-STD-BD" } },
        }, Sales);
        var debit = await PostForData("/api/v1/adjustments", new
        {
            kind = "Debit",
            invoiceId,
            reason = "Under-billed freight",
            lines = new[] { new { itemSku = "FREIGHT", qty = 1m, unitPrice = 20m, taxCodeId = "VAT-STD-BD" } },
        }, Sales);

        var list = (await GetAsync($"/api/v1/adjustments?invoiceId={invoiceId}", Sales)).GetProperty("data");
        Assert.Equal(2, list.GetArrayLength());
        var creditRow = list.EnumerateArray().First(a => a.GetProperty("kind").GetString() == "Credit");
        Assert.Equal(credit.GetProperty("id").GetString(), creditRow.GetProperty("id").GetString());
        Assert.Equal("Draft", creditRow.GetProperty("status").GetString());
        Assert.Equal(100m, creditRow.GetProperty("subTotal").GetDecimal());
        Assert.Equal(0m, creditRow.GetProperty("total").GetDecimal()); // totals finalize at approval
        Assert.Equal("SKU-R6", creditRow.GetProperty("lines")[0].GetProperty("itemSku").GetString());
        Assert.Equal(invoiceId, creditRow.GetProperty("invoiceId").GetString());
        var debitRow = list.EnumerateArray().First(a => a.GetProperty("kind").GetString() == "Debit");
        Assert.Equal(debit.GetProperty("id").GetString(), debitRow.GetProperty("id").GetString());

        // approved adjustment shows the finalized totals
        await PostForData($"/api/v1/adjustments/{creditRow.GetProperty("id").GetString()!}/approvals", new { reason = (string?)null }, Fin);
        var approvedRow = (await GetAsync($"/api/v1/adjustments?invoiceId={invoiceId}", Sales)).GetProperty("data")
            .EnumerateArray().First(a => a.GetProperty("kind").GetString() == "Credit");
        Assert.Equal("Approved", approvedRow.GetProperty("status").GetString());
        Assert.Equal(115m, approvedRow.GetProperty("total").GetDecimal()); // 100 + 15% VAT
        Assert.NotNull(approvedRow.GetProperty("journalId").GetString());

        var res = await GetRawAsync("/api/v1/adjustments?invoiceId=INV-9999", Sales);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("INVOICE_NOT_FOUND", ErrorCode(await Json(res)));
        res = await GetRawAsync("/api/v1/adjustments", Sales);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(res)));
    }

    // ==================== 6. GET /api/v1/invoices/{id}/reminders + /disputes ====================

    [Fact]
    public async Task Invoice_Reminders_And_Disputes_List_The_Invoice_Records()
    {
        var invoiceId = await SeedPostedInvoice("R7", 2m, 50m);
        await PostForData("/api/v1/collections/reminders", new { invoiceId, channel = "Email", templateVersion = "v1", recipient = "ap@buyer.test" }, Sales);
        await PostForData("/api/v1/collections/reminders", new { invoiceId, channel = "Phone", recipient = "+8801700000000" }, Sales);
        var dispute = await PostForData("/api/v1/collections/disputes",
            new { invoiceId, reason = "Pricing dispute on line 1", promiseDueDate = Today.AddDays(7) }, Sales);
        await PostForData($"/api/v1/collections/disputes/{dispute.GetProperty("id").GetString()!}/resolutions",
            new { outcome = "Discount agreed" }, Sales);

        var reminders = (await GetAsync($"/api/v1/invoices/{invoiceId}/reminders", Sales)).GetProperty("data");
        Assert.Equal(2, reminders.GetArrayLength());
        Assert.Equal("Email", reminders[0].GetProperty("channel").GetString());
        Assert.Equal("v1", reminders[0].GetProperty("templateVersion").GetString());
        Assert.Equal("ap@buyer.test", reminders[0].GetProperty("recipient").GetString());
        Assert.Equal(invoiceId, reminders[0].GetProperty("invoiceId").GetString());

        var disputes = (await GetAsync($"/api/v1/invoices/{invoiceId}/disputes", Sales)).GetProperty("data");
        Assert.Equal(1, disputes.GetArrayLength());
        Assert.Equal(dispute.GetProperty("id").GetString(), disputes[0].GetProperty("id").GetString());
        Assert.Equal("Resolved", disputes[0].GetProperty("status").GetString());
        Assert.Equal("Discount agreed", disputes[0].GetProperty("outcome").GetString());
        Assert.Equal(Today.AddDays(7), DateOnly.Parse(disputes[0].GetProperty("promiseDueDate").GetString()!));

        var res = await GetRawAsync("/api/v1/invoices/INV-9999/reminders", Sales);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("INVOICE_NOT_FOUND", ErrorCode(await Json(res)));
        res = await GetRawAsync("/api/v1/invoices/INV-9999/disputes", Sales);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("INVOICE_NOT_FOUND", ErrorCode(await Json(res)));
    }

    // ==================== 7. GET /api/v1/purchase-orders/{id}/acknowledgements ====================
    // Click-through fu-5: supplier acks were write-only — the read-back is part of the same family.

    private async Task<(string PoId, string SupplierId)> SeedPoWithAck(string tag)
    {
        var supplierId = await SeedSupplier($"{tag}-SUP");
        var poId = (await PostForData("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = $"SKU-{tag}", description = $"Item {tag}", qty = 10m, unitPrice = 5m } },
        }, Proc)).GetProperty("id").GetString()!;
        await PostForData($"/api/v1/purchase-orders/{poId}/acknowledgements", new
        {
            supplierId,
            orderVersion = 1,
            lines = new[] { new { itemSku = $"SKU-{tag}", status = "Accepted" } },
        }, Proc);
        return (poId, supplierId);
    }

    [Fact]
    public async Task Po_Acks_Read_Back_All_Written_Fields()
    {
        var (poId, supplierId) = await SeedPoWithAck("R9");

        var acks = (await GetAsync($"/api/v1/purchase-orders/{poId}/acknowledgements", Proc)).GetProperty("data");
        Assert.Equal(1, acks.GetArrayLength());
        var ack = acks[0];
        Assert.Equal(poId, ack.GetProperty("poId").GetString());
        Assert.Equal(1, ack.GetProperty("orderVersion").GetInt32());
        Assert.Equal("Current", ack.GetProperty("status").GetString());
        Assert.False(ack.GetProperty("late").GetBoolean());
        Assert.Equal(supplierId, ack.GetProperty("supplierId").GetString());
        Assert.Equal(Proc, ack.GetProperty("receivedBy").GetString());
        Assert.NotNull(ack.GetProperty("receivedAt").GetString());
        Assert.False(ack.GetProperty("hasDiscrepancy").GetBoolean());
        Assert.Equal(1, ack.GetProperty("lines").GetArrayLength());
        Assert.Equal($"SKU-R9", ack.GetProperty("lines")[0].GetProperty("itemSku").GetString());
        Assert.Equal("Accepted", ack.GetProperty("lines")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Po_Acks_Unknown_Order_Is_404()
    {
        var res = await GetRawAsync("/api/v1/purchase-orders/PO-9999/acknowledgements", Proc);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("PO_NOT_FOUND", ErrorCode(await Json(res)));
    }

    [Fact]
    public async Task Po_Acks_Ungated_User_Is_403()
    {
        var (poId, _) = await SeedPoWithAck("R10");

        // sales carries neither po:read nor po:write
        var res = await GetRawAsync($"/api/v1/purchase-orders/{poId}/acknowledgements", Sales);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(res)));
    }
}
