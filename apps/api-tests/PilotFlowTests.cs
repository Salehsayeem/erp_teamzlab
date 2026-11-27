using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Erp.Api.Tests;

// Procure-to-receive pilot flow (02_AppFlow §3.1) + envelope/validation/security checks.
// shortcut: shared WebApplicationFactory per class — in-memory store resets between test classes only

public class PilotFlowTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public PilotFlowTests(TestApiFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await client.PostAsJsonAsync(path, body);
    }

    private async Task<JsonElement> GetAsync(string path, string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return await Json(await client.GetAsync(path));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private async Task<string> CreatePendingPoAsync(string maker)
    {
        var res = await PostAsync("/api/v1/suppliers", new { name = "Acme Steel", taxId = "TRX-9911", email = "sales@acme.test" }, maker);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = "STL-ROD-12", description = "Steel rod 12mm", qty = 100m, unitPrice = 45.50m } },
        }, maker);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, maker)).StatusCode);
        return poId;
    }

    // Follow-up fu-1 (click-through): explicit decision binding — a reason on the approvals
    // endpoint is an approval NOTE, never a silent rejection.
    [Fact]
    public async Task PO_approve_with_reason_approves_and_records_note_in_audit()
    {
        var poId = await CreatePendingPoAsync("U-002");

        var res = await PostAsync($"/api/v1/purchase-orders/{poId}/approvals",
            new { reason = "Approved per budget release Q4" }, "U-001");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Approved", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        var audit = await GetAsync("/api/v1/audit-events", "U-001");
        Assert.Contains(audit.GetProperty("data").EnumerateArray(),
            e => e.GetProperty("action").GetString() == "approve"
                && e.GetProperty("entityId").GetString() == poId
                && (e.GetProperty("summary").GetString() ?? "").Contains("Approved per budget release Q4"));
    }

    [Fact]
    public async Task PO_reject_with_reason_rejects_and_persists_reason()
    {
        var poId = await CreatePendingPoAsync("U-002");

        var res = await PostAsync($"/api/v1/purchase-orders/{poId}/rejections",
            new { reason = "Wrong supplier terms" }, "U-001");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Rejected", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        var detail = await GetAsync($"/api/v1/purchase-orders/{poId}", "U-002");
        Assert.Equal("Wrong supplier terms", detail.GetProperty("data").GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task PO_reject_without_reason_is_400_envelope_error()
    {
        var poId = await CreatePendingPoAsync("U-002");

        var res = await PostAsync($"/api/v1/purchase-orders/{poId}/rejections", new { }, "U-001");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json(res);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("DECISION_REASON_REQUIRED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Procure_to_receive_posts_stock_and_balanced_journal()
    {
        var proc = "U-002"; var admin = "U-001"; var fin = "U-003";

        // supplier
        var res = await PostAsync("/api/v1/suppliers", new { name = "Acme Steel", taxId = "TRX-9911", email = "sales@acme.test" }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var supplierId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        // PO + submit
        res = await PostAsync("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = "STL-ROD-12", description = "Steel rod 12mm", qty = 100m, unitPrice = 45.50m } },
        }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var poId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/purchase-orders/{poId}/submit", new { }, proc);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // maker-checker: creator cannot approve own PO
        res = await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, proc);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("PO_APPROVAL_ERROR", (await Json(res)).GetProperty("error").GetProperty("code").GetString());

        // approve as admin → receipt → stock + balanced journal
        res = await PostAsync($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Approved", (await Json(res)).GetProperty("data").GetProperty("status").GetString());

        res = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "STL-ROD-12", qty = 100m }, proc);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var receiptData = (await Json(res)).GetProperty("data");
        Assert.Equal(4550.00m, receiptData.GetProperty("journal").GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        Assert.Equal(4550.00m, receiptData.GetProperty("journal").GetProperty("lines")[1].GetProperty("credit").GetDecimal());

        // stock reflects receipt (Db.I is a process-wide singleton — other test classes may have
        // received/shipped the same SKU, so assert the receipt's contribution, not an exact count)
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {proc}");
        var stock = await Json(await client.GetAsync("/api/v1/stock"));
        var rod = stock.GetProperty("data").EnumerateArray()
            .First(s => s.GetProperty("itemSku").GetString() == "STL-ROD-12");
        Assert.True(rod.GetProperty("qtyOnHand").GetDecimal() >= 100m);

        // finance reverses the journal — linked provenance
        var journalId = receiptData.GetProperty("journal").GetProperty("id").GetString()!;
        res = await PostAsync($"/api/v1/journals/{journalId}/reversals", new { }, fin);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.NotNull((await Json(res)).GetProperty("data").GetProperty("reversalOfId").GetString());

        // audit evidence exists for the whole chain (audit-events is admin-only per finding 6)
        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {admin}");
        var audit = await Json(await adminClient.GetAsync("/api/v1/audit-events"));
        Assert.True(audit.GetProperty("data").GetArrayLength() >= 6);
    }

    // R-T7i: SO approval is gated on so:approve (moved off so:write) — a role without it is
    // forbidden even when the maker-checker pair is legal (procurement officer here)
    [Fact]
    public async Task SO_approval_forbidden_without_so_approve()
    {
        var proc = "U-002"; var sales = "U-004";

        var res = await PostAsync("/api/v1/customers", new { name = "Gate Co", taxId = "TRX-GATE-1", email = "ap@gate.test" }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var customerId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;

        res = await PostAsync("/api/v1/sales-orders", new
        {
            customerId,
            lines = new[] { new { itemSku = "GATE-SKU", description = "Gate probe", qty = 1m, unitPrice = 5m } },
        }, sales);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var soId = (await Json(res)).GetProperty("data").GetProperty("id").GetString()!;
        await PostAsync($"/api/v1/sales-orders/{soId}/submit", new { }, sales);

        res = await PostAsync($"/api/v1/sales-orders/{soId}/approvals", new { reason = (string?)null }, proc);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("so:approve permission required.", (await Json(res)).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Validation_error_uses_envelope()
    {
        var res = await PostAsync("/api/v1/auth/token-exchange", new { email = "not-an-email" }, null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json(res);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_token_is_unauthorized()
    {
        var res = await _factory.CreateClient().GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
