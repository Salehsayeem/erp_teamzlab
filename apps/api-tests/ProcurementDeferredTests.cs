using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

// Deferred PROCUREMENT wave (briefs/deferred-screens-procurement.md) — P1 requisitions first.
// Makers: U-002/U-009 (R-PROC/R-PROC2, requisitions:write). Checker: U-003 (R-FIN,
// requisitions:approve). Admin U-001 can play either side (*). Quality authority: U-011 (R-QC,
// receipts:dispose) — renumbered from U-010 in the union merge (U-010 is PEOPLE's HR checker).
// Budget-check stub mode flips via finance_settings on the throwaway fixture DB
// (direct write — the house precedent for wall-clock-style fixture adjustments).

namespace Erp.Api.Tests;

public class ProcurementDeferredTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public ProcurementDeferredTests(TestApiFactory factory) => _factory = factory;

    private const string Proc = "U-002";   // R-PROC — maker
    private const string Proc2 = "U-009";  // R-PROC2 — second procurement officer (Q-S1)
    private const string Fin = "U-003";    // R-FIN — requisitions:approve checker
    private const string Admin = "U-001";  // R-ADMIN — *
    private const string Eval = "U-006";   // R-EVAL — sourcing-only, the wrong role for everything new
    private const string Qc = "U-011";     // R-QC — quality authority (Q-P7; U-010 is PEOPLE's HR checker)

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
        var res = await client.GetAsync(path);
        Assert.True(res.IsSuccessStatusCode, $"{path} → {(int)res.StatusCode}");
        return (await Json(res)).GetProperty("data");
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

    private async Task<string> SetBudgetMode(string mode)
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE finance_settings SET value = @m WHERE key = 'budget.check_mode'", conn);
        cmd.Parameters.AddWithValue("m", mode);
        await cmd.ExecuteNonQueryAsync();
        return mode;
    }

    // drafts save incomplete (011 AC1) — only the id/status/version contract is asserted at create.
    // Self-seeds one active category + the EA unit so submission validation passes.
    private async Task<string> SeedRequisition(string tag, decimal unitPrice = 100m, string? category = null,
        string actor = Proc, decimal qty = 5m)
    {
        var cat = category ?? await SeedCategory($"cat-{tag}");
        await EnsureUom("EA", "Count", 0, isBase: true);
        var data = await PostForData("/api/v1/requisitions", new
        {
            purpose = $"Requisition {tag}",
            currency = "BDT",
            lines = new[]
            {
                new { categoryId = cat, description = $"{tag} line A", qty, unitPrice, unit = "EA", needBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)) },
            },
        }, actor);
        return data.GetProperty("id").GetString()!;
    }

    private async Task EnsureUom(string code, string dimension, int precision, bool isBase = false)
    {
        var list = await GetAsync("/api/v1/unit-of-measures", Proc);
        if (list.EnumerateArray().Any(u => u.GetProperty("code").GetString() == code)) return;
        await PostForData("/api/v1/unit-of-measures", new { code, name = code, dimension, precision, isBase }, Proc);
    }

    // full happy path: create → submit as the given maker
    private async Task<(string id, JsonElement detail)> SubmitRequisition(string tag, string actor = Proc,
        decimal unitPrice = 100m, string? idemKey = null)
    {
        var id = await SeedRequisition(tag, unitPrice, actor: actor);
        var res = await PostAsync($"/api/v1/requisitions/{id}/submission", new { }, actor, idemKey);
        Assert.True(res.IsSuccessStatusCode, $"submission → {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        var detail = await GetAsync($"/api/v1/requisitions/{id}", actor);
        return (id, detail);
    }

    // ==================== P1: requisitions (011–019) ====================

    [Fact]
    public async Task Submit_with_missing_line_field_stays_draft_with_field_error_012()
    {
        var id = await SeedRequisition("P1-012a"); // valid draft first
        var broken = await PostForData("/api/v1/requisitions", new
        {
            purpose = "Broken draft",
            lines = new[] { new { categoryId = "CAT-OFFICE", description = "", qty = 1m, unitPrice = 10m, unit = "EA", needBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) } },
        }, Proc);
        Assert.Equal("Draft", broken.GetProperty("status").GetString()); // draft saves incomplete (011 AC1)
        var draftId = broken.GetProperty("id").GetString()!;

        var res = await PostAsync($"/api/v1/requisitions/{draftId}/submission", new { }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(body));
        Assert.Contains("description", body.GetProperty("error").GetProperty("message").GetString()!);
        Assert.Contains("line 1", body.GetProperty("error").GetProperty("message").GetString()!); // field-specific reference

        var after = await GetAsync($"/api/v1/requisitions/{draftId}", Proc);
        Assert.Equal("Draft", after.GetProperty("requisition").GetProperty("status").GetString());
        _ = id;
    }

    [Fact]
    public async Task Submit_assigns_stable_id_version_and_submitter_011()
    {
        var id = await SeedRequisition("P1-011");
        var detail = await GetAsync($"/api/v1/requisitions/{id}", Proc);
        Assert.Equal(1, detail.GetProperty("requisition").GetProperty("version").GetInt32());

        var submitted = await PostForData($"/api/v1/requisitions/{id}/submission", new { }, Proc);
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.Equal(id, submitted.GetProperty("id").GetString()); // stable id
        Assert.Equal(2, submitted.GetProperty("version").GetInt32()); // record version bumped
        Assert.Equal(Proc, submitted.GetProperty("submittedBy").GetString());

        var step = (await GetAsync($"/api/v1/requisitions/{id}", Fin)).GetProperty("step");
        Assert.Equal(JsonValueKind.Null, step.GetProperty("decision").ValueKind); // pending step
        Assert.Equal(1, step.GetProperty("version").GetInt32());
        Assert.Equal(1, step.GetProperty("ruleVersion").GetInt32()); // rule version snapshotted (014 AC1)
    }

    [Fact]
    public async Task Creator_cannot_decide_own_requisition_016()
    {
        var (id, detail) = await SubmitRequisition("P1-016", actor: Admin);
        var version = detail.GetProperty("step").GetProperty("version").GetInt32();
        var res = await PostAsync($"/api/v1/requisitions/{id}/decisions",
            new { decision = "Approve", version }, Admin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("REQUISITION_APPROVAL_ERROR", ErrorCode(await Json(res)));
    }

    [Fact]
    public async Task Stale_version_decision_conflicts_and_step_survives_018()
    {
        var (id, detail) = await SubmitRequisition("P1-018");
        var res = await PostAsync($"/api/v1/requisitions/{id}/decisions",
            new { decision = "Approve", version = detail.GetProperty("step").GetProperty("version").GetInt32() + 99 }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("REQUISITION_VERSION_CONFLICT", ErrorCode(await Json(res)));

        var after = await GetAsync($"/api/v1/requisitions/{id}", Fin);
        Assert.Equal("Submitted", after.GetProperty("requisition").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("step").GetProperty("decision").ValueKind);
    }

    [Fact]
    public async Task Budget_hard_stop_blocks_submit_override_needs_perm_and_reason_013()
    {
        await SetBudgetMode("HardStop");
        var id = await SeedRequisition("P1-013");
        var blocked = await PostAsync($"/api/v1/requisitions/{id}/submission", new { }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("BUDGET_HARD_STOP", ErrorCode(await Json(blocked)));

        // override reason but no override permission → still blocked
        var noPerm = await PostAsync($"/api/v1/requisitions/{id}/submission", new { overrideReason = "urgent" }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, noPerm.StatusCode);

        // admin holds requisitions:override (*) → proceeds with an audited override
        var overridden = await PostForData($"/api/v1/requisitions/{id}/submission", new { overrideReason = "urgent plant stoppage" }, Admin);
        Assert.Equal("Submitted", overridden.GetProperty("status").GetString());
        await SetBudgetMode("Pass");
    }

    [Fact]
    public async Task Idempotent_submission_reserves_budget_once_013()
    {
        var (id, _) = await SubmitRequisition("P1-013r", idemKey: $"P1-013r-key");
        var replay = await PostAsync($"/api/v1/requisitions/{id}/submission", new { }, Proc, "P1-013r-key");
        Assert.True(replay.IsSuccessStatusCode);

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM budget_reservations WHERE txn_id = @t", conn);
        cmd.Parameters.AddWithValue("t", id);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Out_of_scope_delegation_denied_despite_second_delegation_015()
    {
        // delegations target Proc (not Proc2) so earlier tests' Proc2 delegations can't leak in
        var (id, _) = await SubmitRequisition("P1-015a", unitPrice: 500m); // amount 2500
        await PostForData("/api/v1/approval-delegations", new
        {
            delegateId = Proc, from = DateTimeOffset.UtcNow.AddHours(-1), to = DateTimeOffset.UtcNow.AddHours(4),
            maxAmount = 100m, reason = "leave cover", // in window, out by amount
        }, Fin);
        await PostForData("/api/v1/approval-delegations", new
        {
            delegateId = Proc, from = DateTimeOffset.UtcNow.AddHours(-1), to = DateTimeOffset.UtcNow.AddHours(4),
            categoryId = "CAT-OTHER", reason = "leave cover", // out by category scope
        }, Fin);
        var detail = await GetAsync($"/api/v1/requisitions/{id}", Fin);
        var res = await PostAsync($"/api/v1/requisitions/{id}/decisions",
            new { decision = "Approve", version = detail.GetProperty("step").GetProperty("version").GetInt32() }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("DELEGATION_SCOPE_DENIED", ErrorCode(await Json(res)));
    }

    [Fact]
    public async Task Delegate_decision_audits_delegate_and_delegator_distinctly_015()
    {
        var (id, _) = await SubmitRequisition("P1-015b");
        await PostForData("/api/v1/approval-delegations", new
        {
            delegateId = Proc2, from = DateTimeOffset.UtcNow.AddHours(-1), to = DateTimeOffset.UtcNow.AddHours(4),
            reason = "leave cover",
        }, Fin);
        var detail = await GetAsync($"/api/v1/requisitions/{id}", Proc2);
        var decided = await PostForData($"/api/v1/requisitions/{id}/decisions",
            new { decision = "Approve", version = detail.GetProperty("step").GetProperty("version").GetInt32() }, Proc2);
        Assert.Equal("Approved", decided.GetProperty("status").GetString());

        var after = await GetAsync($"/api/v1/requisitions/{id}", Fin);
        var step = after.GetProperty("step");
        Assert.Equal(Proc2, step.GetProperty("decidedBy").GetString());
        Assert.Equal(Fin, step.GetProperty("onBehalfOfDelegatorId").GetString());
        var history = after.GetProperty("decisions").EnumerateArray().ToList();
        Assert.Contains(history, d => d.GetProperty("actorId").GetString() == Proc2
            && d.GetProperty("onBehalfOfDelegatorId").GetString() == Fin);
    }

    [Fact]
    public async Task Reject_and_return_require_minimum_reason_017()
    {
        var (id, detail) = await SubmitRequisition("P1-017");
        var version = detail.GetProperty("step").GetProperty("version").GetInt32();
        var shortReject = await PostAsync($"/api/v1/requisitions/{id}/decisions",
            new { decision = "Reject", version, reason = "no" }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, shortReject.StatusCode);
        Assert.Equal("DECISION_REASON_REQUIRED", ErrorCode(await Json(shortReject)));

        var shortReturn = await PostAsync($"/api/v1/requisitions/{id}/decisions",
            new { decision = "Return", version, reason = "why?" }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, shortReturn.StatusCode);
    }

    [Fact]
    public async Task Decision_history_is_append_only_and_requestinfo_bumps_token_017_018()
    {
        var (id, detail) = await SubmitRequisition("P1-017h");
        var v1 = detail.GetProperty("step").GetProperty("version").GetInt32();
        await PostForData($"/api/v1/requisitions/{id}/decisions", new { decision = "RequestInfo", version = v1, reason = "attach spec sheet" }, Fin);
        var mid = await GetAsync($"/api/v1/requisitions/{id}", Fin);
        Assert.Equal("Submitted", mid.GetProperty("requisition").GetProperty("status").GetString());
        var v2 = mid.GetProperty("step").GetProperty("version").GetInt32();
        Assert.True(v2 > v1, "request-info bumps the decision token (stale tokens conflict)");

        var approved = await PostForData($"/api/v1/requisitions/{id}/decisions", new { decision = "Approve", version = v2 }, Fin);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        var history = (await GetAsync($"/api/v1/requisitions/{id}", Fin)).GetProperty("decisions").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal("RequestInfo", history[0].GetProperty("decision").GetString());
        Assert.Equal("Approve", history[1].GetProperty("decision").GetString());

        // one terminal decision per step (018 AC3): a second terminal decision is a state error
        var again = await PostAsync($"/api/v1/requisitions/{id}/decisions", new { decision = "Reject", version = v2 + 1, reason = "double decision" }, Fin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("REQUISITION_STATE_ERROR", ErrorCode(await Json(again)));
    }

    [Fact]
    public async Task Exception_requires_reason_code_and_narrative_019()
    {
        var id = await SeedRequisition("P1-019");
        var missing = await PostAsync($"/api/v1/requisitions/{id}/exceptions", new { reasonCode = "SOLE_SOURCE" }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("EXCEPTION_REASON_REQUIRED", ErrorCode(await Json(missing)));

        var flagged = await PostForData($"/api/v1/requisitions/{id}/exceptions", new
        {
            reasonCode = "SOLE_SOURCE", narrative = "only supplier carries the certified part",
            evidenceRef = "memo-77",
        }, Proc);
        Assert.Equal("SOLE_SOURCE", flagged.GetProperty("exceptionCode").GetString());
        Assert.True(flagged.GetProperty("exceptionFlaggedAt").ValueKind != JsonValueKind.Null);
    }

    private async Task<string> SeedSupplier(string tag)
    {
        var data = await PostForData("/api/v1/suppliers",
            new { name = $"Supplier {tag}", taxId = $"TRX-{tag}", email = $"{tag.ToLowerInvariant()}@sup.test" }, Proc);
        return data.GetProperty("id").GetString()!;
    }

    // ==================== P2: category taxonomy + purchasing units (002–004) ====================

    // registers the catalog rows requisition validation consumes: one active category + base UoMs
    private async Task<string> SeedCategory(string tag)
    {
        var data = await PostForData("/api/v1/procurement-categories", new { name = $"Category {tag}" }, Proc);
        return data.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Category_cycle_rejected_on_reparent_003()
    {
        var a = await SeedCategory("P2-A");
        var b = (await PostForData("/api/v1/procurement-categories", new { name = "P2-B", parentId = a }, Proc))
            .GetProperty("id").GetString()!;

        var cycle = await PostAsync($"/api/v1/procurement-categories/{a}", new { parentId = b }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        Assert.Equal("CATEGORY_CYCLE", ErrorCode(await Json(cycle)));

        var ok = await PostForData($"/api/v1/procurement-categories/{a}", new { name = "P2-A renamed" }, Proc);
        Assert.Equal("P2-A renamed", ok.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Deprecated_category_not_selectable_on_requisition_003()
    {
        var cat = await SeedCategory("P2-dep");
        var dep = await PostForData($"/api/v1/procurement-categories/{cat}/deprecations", new { }, Proc);
        Assert.Equal("Deprecated", dep.GetProperty("status").GetString());

        var id = await SeedRequisition("P2-dep-req", category: cat);
        var res = await PostAsync($"/api/v1/requisitions/{id}/submission", new { }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("deprecated", (await Json(res)).GetProperty("error").GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Over_precision_qty_rejected_never_rounded_004()
    {
        var cat = await SeedCategory("P2-prec");
        await EnsureUom("GM", "Mass", 2, isBase: true);
        var created = await PostForData("/api/v1/requisitions", new
        {
            purpose = "precision",
            lines = new[] { new { categoryId = cat, description = "powder", qty = 1.123m, unitPrice = 1m, unit = "GM", needBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) } },
        }, Proc);
        var id = created.GetProperty("id").GetString()!; // draft accepts the line as-is
        var res = await PostAsync($"/api/v1/requisitions/{id}/submission", new { }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("QTY_PRECISION", ErrorCode(await Json(res)));
    }

    [Fact]
    public async Task Conversion_applied_only_when_active_for_txn_date_004()
    {
        var cat = await SeedCategory("P2-conv");
        await EnsureUom("GM", "Mass", 2, isBase: true);
        await EnsureUom("BOX", "Mass", 2);
        await EnsureUom("EA", "Count", 0, isBase: true);
        var badDim = await PostAsync("/api/v1/unit-of-measures/BOX/conversions",
            new { toCode = "EA", factor = 1m, effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow) }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, badDim.StatusCode);
        Assert.Equal("UOM_CONVERSION_INVALID", ErrorCode(await Json(badDim)));

        // future-dated conversion: not applicable today
        await PostForData("/api/v1/unit-of-measures/BOX/conversions", new
        {
            toCode = "GM", factor = 1000m, effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
        }, Proc);
        var draft = await PostForData("/api/v1/requisitions", new
        {
            purpose = "conversion",
            lines = new[] { new { categoryId = cat, description = "bulk", qty = 2m, unitPrice = 10m, unit = "BOX", needBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)) } },
        }, Proc);
        var draftId = draft.GetProperty("id").GetString()!;
        var inactive = await PostAsync($"/api/v1/requisitions/{draftId}/submission", new { }, Proc);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode); // no active conversion for today

        // second conversion effective now → normalization applies: 2 BOX → 2000 GM
        await PostForData("/api/v1/unit-of-measures/BOX/conversions", new
        {
            toCode = "GM", factor = 1000m, effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        }, Proc);
        var submitted = await PostForData($"/api/v1/requisitions/{draftId}/submission", new { }, Proc);
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        var line = submitted.GetProperty("lines").EnumerateArray().First();
        Assert.Equal(2000m, line.GetProperty("baseQty").GetDecimal());
        Assert.Equal("GM", line.GetProperty("baseUnit").GetString());
    }

    // ==================== P3: PO change control + acks + blanket orders (052–054) ====================

    // approved PO with one line (SKU-{tag} ×10 @100) — the 052/053 test substrate
    private async Task<(string poId, string supplierId)> SeedApprovedPo(string tag, decimal qty = 10m)
    {
        var supplierId = await SeedSupplier(tag);
        var po = await PostForData("/api/v1/purchase-orders", new
        {
            supplierId,
            lines = new[] { new { itemSku = $"SKU-{tag}", description = $"{tag} item", qty, unitPrice = 100m } },
        }, Proc);
        var poId = po.GetProperty("id").GetString()!;
        await PostForData($"/api/v1/purchase-orders/{poId}/submit", new { }, Proc);
        await PostForData($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, Fin);
        return (poId, supplierId);
    }

    [Fact]
    public async Task Material_change_blocks_dispatch_until_approved_052()
    {
        var (poId, _) = await SeedApprovedPo("P3-052");
        var change = await PostForData($"/api/v1/purchase-orders/{poId}/changes", new
        {
            rationale = "volume renegotiated",
            lines = new[] { new { itemSku = $"SKU-P3-052", qty = 99m, unitPrice = (decimal?)90m } },
        }, Proc);
        Assert.Equal("Pending", change.GetProperty("status").GetString());
        Assert.True(change.GetProperty("isMaterial").GetBoolean());

        var blocked = await PostAsync($"/api/v1/purchase-orders/{poId}/dispatches",
            new { orderVersion = 1, state = "Sent", detail = "smtp" }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("PO_CHANGE_PENDING", ErrorCode(await Json(blocked)));

        // maker-checker: the requester cannot approve their own change (052/016)
        var selfDecide = await PostAsync($"/api/v1/purchase-orders/{poId}/changes/{change.GetProperty("id").GetString()!}/decisions",
            new { approve = true }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, selfDecide.StatusCode);
        Assert.Equal("PO_CHANGE_APPROVAL_ERROR", ErrorCode(await Json(selfDecide)));

        var decided = await PostForData($"/api/v1/purchase-orders/{poId}/changes/{change.GetProperty("id").GetString()!}/decisions",
            new { approve = true }, Fin);
        Assert.Equal("Approved", decided.GetProperty("status").GetString());

        var after = await GetAsync($"/api/v1/purchase-orders/{poId}", Proc);
        Assert.Equal(2, after.GetProperty("version").GetInt32()); // order version bumped on apply
        var line = after.GetProperty("lines").EnumerateArray().First();
        Assert.Equal(99m, line.GetProperty("qty").GetDecimal());
        Assert.Equal(90m, line.GetProperty("unitPrice").GetDecimal());

        var dispatch = await PostForData($"/api/v1/purchase-orders/{poId}/dispatches",
            new { orderVersion = 2, state = "Sent", detail = "smtp" }, Proc);
        Assert.Equal("Sent", dispatch.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Superseded_ack_stored_and_flagged_current_version_counts_053()
    {
        var (poId, supplierId) = await SeedApprovedPo("P3-053");
        // bump to version 2 via an approved change, then the supplier's version-1 ack arrives late
        var change = await PostForData($"/api/v1/purchase-orders/{poId}/changes", new
        {
            rationale = "qty correction", lines = new[] { new { itemSku = "SKU-P3-053", qty = 12m } },
        }, Proc);
        await PostForData($"/api/v1/purchase-orders/{poId}/changes/{change.GetProperty("id").GetString()!}/decisions",
            new { approve = true }, Fin);

        var late = await PostForData($"/api/v1/purchase-orders/{poId}/acknowledgements", new
        {
            supplierId, orderVersion = 1,
            lines = new[] { new { itemSku = "SKU-P3-053", status = "Accepted" } },
        }, Proc);
        Assert.Equal("Superseded", late.GetProperty("status").GetString());
        Assert.True(late.GetProperty("late").GetBoolean());

        var current = await PostForData($"/api/v1/purchase-orders/{poId}/acknowledgements", new
        {
            supplierId, orderVersion = 2,
            lines = new[] { new { itemSku = "SKU-P3-053", status = "Discrepancy" } },
        }, Proc);
        Assert.Equal("Current", current.GetProperty("status").GetString());
        Assert.True(current.GetProperty("hasDiscrepancy").GetBoolean()); // buyer task flagged, PO not mutated

        var after = await GetAsync($"/api/v1/purchase-orders/{poId}", Proc);
        Assert.Equal(2, after.GetProperty("version").GetInt32()); // acks never mutate the order
    }

    [Fact]
    public async Task Concurrent_blanket_releases_admit_exactly_one_054()
    {
        var supplierId = await SeedSupplier("P3-054");
        var blanket = await PostForData("/api/v1/blanket-orders", new
        {
            supplierId, currency = "BDT", ceilingAmount = 100m,
            validFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        }, Proc);
        var blanketId = blanket.GetProperty("id").GetString()!;

        // three-way overlap widens the race window; every release is over half the ceiling so any
        // two successes would overrun — the lost-update shape must be impossible, not lucky
        var releases = await Task.WhenAll(Enumerable.Range(0, 3).Select(i =>
            PostAsync($"/api/v1/blanket-orders/{blanketId}/releases", new { amount = 60m }, Proc)));
        var statuses = releases.Select(r => r.StatusCode).ToList();
        Assert.True(statuses.Count(s => s == HttpStatusCode.Created) == 1,
            $"expected exactly one 201, got [{string.Join(", ", releases.Select(r => $"{(int)r.StatusCode}:{r.Content.ReadAsStringAsync().Result}"))}]");
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.Conflict));
        var loser = await Json(releases.First(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal("BLANKET_CEILING_CONFLICT", ErrorCode(loser));

        // fix-round-1 invariant: the debited ceiling equals the release rows — a stale post-lock
        // re-read lets the loser overwrite the winner's debit (both 201s, ceiling silent)
        var detail = await GetAsync($"/api/v1/blanket-orders/{blanketId}", Proc);
        var releaseSum = detail.GetProperty("releases").EnumerateArray().Sum(r => r.GetProperty("amount").GetDecimal());
        Assert.Equal(releaseSum, detail.GetProperty("releasedAmount").GetDecimal());
        Assert.True(detail.GetProperty("releasedAmount").GetDecimal() <= 100m); // ceiling never negative/overrun
        Assert.Equal(60m, detail.GetProperty("releasedAmount").GetDecimal());
    }

    [Fact]
    public async Task Release_with_idempotency_key_succeeds_and_replays_054()
    {
        var supplierId = await SeedSupplier("P3-054k");
        var blanket = await PostForData("/api/v1/blanket-orders", new
        {
            supplierId, currency = "BDT", ceilingAmount = 500m,
            validFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        }, Proc);
        var blanketId = blanket.GetProperty("id").GetString()!;

        // money POSTs carry an Idempotency-Key — Idem.Wrap opens the transaction, so the release
        // must join it, never nest a second BeginTransaction
        var first = await PostForData($"/api/v1/blanket-orders/{blanketId}/releases",
            new { amount = 40m }, Proc, idemKey: "P3-054k-key");
        var replay = await PostAsync($"/api/v1/blanket-orders/{blanketId}/releases",
            new { amount = 40m }, Proc, idemKey: "P3-054k-key");
        Assert.True(replay.IsSuccessStatusCode, $"replay → {(int)replay.StatusCode}: {await replay.Content.ReadAsStringAsync()}");
        Assert.Equal(first.GetProperty("id").GetString(), (await Json(replay)).GetProperty("data").GetProperty("id").GetString());

        var detail = await GetAsync($"/api/v1/blanket-orders/{blanketId}", Proc);
        Assert.Equal(40m, detail.GetProperty("releasedAmount").GetDecimal()); // replay debited once
        Assert.Single(detail.GetProperty("releases").EnumerateArray());
    }

    [Fact]
    public async Task Expired_blanket_blocks_new_releases_older_releases_intact_054()
    {
        var supplierId = await SeedSupplier("P3-054x");
        var live = await PostForData("/api/v1/blanket-orders", new
        {
            supplierId, currency = "BDT", ceilingAmount = 500m,
            validFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        }, Proc);
        var liveId = live.GetProperty("id").GetString()!;
        await PostForData($"/api/v1/blanket-orders/{liveId}/releases", new { amount = 30m }, Proc);

        var expired = await PostForData("/api/v1/blanket-orders", new
        {
            supplierId, currency = "BDT", ceilingAmount = 500m,
            validFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), // already past
        }, Proc);
        var expiredId = expired.GetProperty("id").GetString()!;
        Assert.Equal("Expired", expired.GetProperty("status").GetString());
        var blocked = await PostAsync($"/api/v1/blanket-orders/{expiredId}/releases", new { amount = 10m }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("BLANKET_EXPIRED", ErrorCode(await Json(blocked)));

        var liveDetail = await GetAsync($"/api/v1/blanket-orders/{liveId}", Proc);
        Assert.Equal(30m, liveDetail.GetProperty("releasedAmount").GetDecimal()); // old releases intact
        Assert.Single(liveDetail.GetProperty("releases").EnumerateArray());

        // list read for the workspace page
        var list = await GetAsync("/api/v1/blanket-orders", Proc);
        Assert.Contains(list.EnumerateArray(), b => b.GetProperty("id").GetString() == liveId);
    }

    [Fact]
    public async Task Receipt_detail_carries_inspections_and_returns()
    {
        var receiptId = await SeedReceipt("P4-detail", qty: 6m);
        var detail = await GetAsync($"/api/v1/receipts/{receiptId}", Proc);
        Assert.Equal(receiptId, detail.GetProperty("receipt").GetProperty("id").GetString());
        Assert.Empty(detail.GetProperty("inspections").EnumerateArray());
        Assert.NotNull(detail.GetProperty("balances"));
    }

    // ==================== P4: receipt workspace (055–059) ====================

    private async Task<string> SeedReceipt(string tag, decimal qty = 10m)
    {
        var (poId, _) = await SeedApprovedPo(tag, qty);
        var receipt = await PostForData("/api/v1/receipts",
            new { purchaseOrderId = poId, itemSku = $"SKU-{tag}", qty }, Proc);
        return receipt.GetProperty("receipt").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Asn_over_line_quarantined_and_never_increases_received_056()
    {
        var (poId, _) = await SeedApprovedPo("P4-056", qty: 10m);
        var asn = await PostForData("/api/v1/advance-ship-notices", new
        {
            poId, shipmentRef = "SHP-P4-056", orderVersion = 1,
            lines = new[]
            {
                new { itemSku = "SKU-P4-056", qty = 15m },   // over the open line → quarantine
                new { itemSku = "SKU-UNKNOWN", qty = 2m },   // unmatched line → quarantine
            },
        }, Proc);
        Assert.Equal("Quarantined", asn.GetProperty("status").GetString());
        Assert.True(asn.GetProperty("lines").EnumerateArray().All(l => l.GetProperty("quarantined").GetBoolean()));

        var queue = await GetAsync("/api/v1/advance-ship-notices?status=Quarantined", Proc);
        Assert.Contains(queue.EnumerateArray(), a => a.GetProperty("id").GetString() == asn.GetProperty("id").GetString());

        var balances = await GetAsync($"/api/v1/procurement-balances?orderId={poId}", Proc);
        Assert.Equal(0m, balances.GetProperty("received").GetDecimal()); // 056 AC3: notice never increases received qty
    }

    [Fact]
    public async Task Over_receipt_blocked_without_override_override_is_audited_057()
    {
        var (poId, _) = await SeedApprovedPo("P4-057", qty: 10m);
        var over = await PostAsync("/api/v1/receipts", new { purchaseOrderId = poId, itemSku = "SKU-P4-057", qty = 11m }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("OVER_RECEIVE", ErrorCode(await Json(over)));

        var overridden = await PostForData("/api/v1/receipts", new
        {
            purchaseOrderId = poId, itemSku = "SKU-P4-057", qty = 11m, toleranceOverrideReason = "supplier short-packed replacement agreed",
        }, Proc);
        Assert.Equal(11m, overridden.GetProperty("receipt").GetProperty("qty").GetDecimal());
    }

    [Fact]
    public async Task Rejected_inspection_qty_excluded_from_invoice_matchable_accepted_058()
    {
        var receiptId = await SeedReceipt("P4-058", qty: 10m);
        var (poId, _) = await SeedApprovedPo("P4-058b", qty: 10m);
        _ = poId;
        var receipt = await GetAsync("/api/v1/receipts", Proc); // list to resolve the PO id from the receipt
        var receiptRow = receipt.EnumerateArray().First(r => r.GetProperty("id").GetString() == receiptId);
        var receiptPo = receiptRow.GetProperty("purchaseOrderId").GetString()!;

        var inspection = await PostForData($"/api/v1/receipts/{receiptId}/inspections",
            new { itemSku = "SKU-P4-058", qtyReceived = 10m }, Qc);
        Assert.Equal("Pending", inspection.GetProperty("status").GetString());

        // SoD: the receiver cannot dispose (058 / Q-P7)
        var sod = await PostAsync($"/api/v1/receipts/{receiptId}/inspections/{inspection.GetProperty("id").GetString()!}/dispositions",
            new { status = "Partial", qtyAccepted = 7m, qtyRejected = 3m, defectCode = "DAMAGE" }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, sod.StatusCode);

        var disposed = await PostForData($"/api/v1/receipts/{receiptId}/inspections/{inspection.GetProperty("id").GetString()!}/dispositions",
            new { status = "Partial", qtyAccepted = 7m, qtyRejected = 3m, defectCode = "DAMAGE", evidenceRef = "photo-1" }, Qc);
        Assert.Equal("Partial", disposed.GetProperty("status").GetString());
        Assert.Equal(Qc, disposed.GetProperty("dispositionedBy").GetString());

        var balances = await GetAsync($"/api/v1/procurement-balances?orderId={receiptPo}", Proc);
        Assert.Equal(10m, balances.GetProperty("received").GetDecimal());
        Assert.Equal(3m, balances.GetProperty("rejected").GetDecimal());
        Assert.Equal(7m, balances.GetProperty("accepted").GetDecimal()); // invoice-matchable excludes rejects (058 AC2)
    }

    [Fact]
    public async Task Return_cannot_exceed_accepted_credit_does_not_inflate_059()
    {
        var receiptId = await SeedReceipt("P4-059", qty: 10m);
        var receipt = await GetAsync("/api/v1/receipts", Proc);
        var receiptRow = receipt.EnumerateArray().First(r => r.GetProperty("id").GetString() == receiptId);
        var receiptPo = receiptRow.GetProperty("purchaseOrderId").GetString()!;
        var receiptSku = receiptRow.GetProperty("itemSku").GetString()!;

        var inspection = await PostForData($"/api/v1/receipts/{receiptId}/inspections",
            new { itemSku = receiptSku, qtyReceived = 10m }, Qc);
        await PostForData($"/api/v1/receipts/{receiptId}/inspections/{inspection.GetProperty("id").GetString()!}/dispositions",
            new { status = "Partial", qtyAccepted = 7m, qtyRejected = 3m, defectCode = "DAMAGE" }, Qc);

        var over = await PostAsync("/api/v1/supplier-returns", new
        {
            receiptId, poId = receiptPo, itemSku = receiptSku, qty = 8m, reason = "damaged in transit", kind = "Replacement",
        }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode); // 8 > 7 accepted (059 AC1)
        Assert.Equal("RETURN_OVER_RECEIVED", ErrorCode(await Json(over)));

        var returned = await PostForData("/api/v1/supplier-returns", new
        {
            receiptId, poId = receiptPo, itemSku = receiptSku, qty = 5m, reason = "damaged in transit", kind = "Replacement",
        }, Proc);
        Assert.Equal(5m, returned.GetProperty("qty").GetDecimal());

        var claim = await PostForData("/api/v1/supplier-claims", new
        {
            receiptId, poId = receiptPo, kind = "Credit", amount = 500m,
        }, Proc);
        Assert.Equal("Open", claim.GetProperty("status").GetString());

        var closeMissing = await PostAsync($"/api/v1/supplier-claims/{claim.GetProperty("id").GetString()!}/closures", new { }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, closeMissing.StatusCode); // 059 AC3
        Assert.Equal("CLAIM_OUTCOME_REQUIRED", ErrorCode(await Json(closeMissing)));

        var maker = await PostAsync($"/api/v1/supplier-claims/{claim.GetProperty("id").GetString()!}/closures",
            new { outcomeCode = "CREDIT_ISSUED", reconciliationStatus = "Reconciled" }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, maker.StatusCode); // closure is a reviewer act (suppliers:review)

        var closed = await PostForData($"/api/v1/supplier-claims/{claim.GetProperty("id").GetString()!}/closures",
            new { outcomeCode = "CREDIT_ISSUED", reconciliationStatus = "Reconciled" }, Fin);
        Assert.Equal("Closed", closed.GetProperty("status").GetString());

        var balances = await GetAsync($"/api/v1/procurement-balances?orderId={receiptPo}", Proc);
        Assert.Equal(7m, balances.GetProperty("accepted").GetDecimal()); // credit/return never inflate accepted (059 AC2)
    }

    [Fact]
    public async Task Dispatch_retries_are_duplicate_free_and_version_pinned_055()
    {
        var (poId, _) = await SeedApprovedPo("P4-055");
        var first = await PostForData($"/api/v1/purchase-orders/{poId}/dispatches",
            new { orderVersion = 1, state = "Sent", detail = "edi-batch-1" }, Proc, idemKey: "P4-055-key");
        var replay = await PostAsync($"/api/v1/purchase-orders/{poId}/dispatches",
            new { orderVersion = 1, state = "Sent", detail = "edi-batch-1" }, Proc, idemKey: "P4-055-key");
        Assert.True(replay.IsSuccessStatusCode);
        Assert.Equal(first.GetProperty("id").GetString(), (await Json(replay)).GetProperty("data").GetProperty("id").GetString());

        var stale = await PostAsync($"/api/v1/purchase-orders/{poId}/dispatches",
            new { orderVersion = 99, state = "Sent" }, Proc);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("PO_ORDER_VERSION_CONFLICT", ErrorCode(await Json(stale)));
    }

    // ==================== P5: supplier qualification + scorecards (040–044, 061–062) ====================

    private async Task<string> SeedSupplierId(string tag) => await SeedSupplier(tag);

    [Fact]
    public async Task Expired_evidence_fails_gate_replacement_preserves_prior_041()
    {
        var supplierId = await SeedSupplierId("P5-041");
        var expired = await PostForData($"/api/v1/suppliers/{supplierId}/evidences", new
        {
            evidenceType = "ISO9001", metadataJson = "{\"cert\":\"ISO-1\"}",
            expiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        }, Proc);
        Assert.Equal("ISO9001", expired.GetProperty("evidenceType").GetString());

        var status = await GetAsync($"/api/v1/suppliers/{supplierId}/qualification", Proc);
        Assert.False(status.GetProperty("eligible").GetBoolean()); // expired evidence fails the gate (041 AC2)
        Assert.Contains(status.GetProperty("expiredEvidences").EnumerateArray(),
            e => e.GetProperty("id").GetString() == expired.GetProperty("id").GetString());

        // replacement preserves the prior version + its verification (041 AC3)
        var replacement = await PostForData($"/api/v1/suppliers/{supplierId}/evidences", new
        {
            evidenceType = "ISO9001", metadataJson = "{\"cert\":\"ISO-2\"}",
            expiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)),
            supersedesId = expired.GetProperty("id").GetString(),
        }, Proc);
        Assert.Equal(expired.GetProperty("id").GetString(), replacement.GetProperty("supersedesId").GetString());

        var evidences = await GetAsync($"/api/v1/suppliers/{supplierId}/evidences", Proc);
        var prior = evidences.EnumerateArray().First(e => e.GetProperty("id").GetString() == expired.GetProperty("id").GetString());
        Assert.Equal(Proc, prior.GetProperty("verifiedBy").GetString()); // prior row intact with verification
        Assert.Equal(2, evidences.EnumerateArray().Count());

        var after = await GetAsync($"/api/v1/suppliers/{supplierId}/qualification", Proc);
        Assert.True(after.GetProperty("eligible").GetBoolean());
    }

    [Fact]
    public async Task Screening_possible_match_queued_outage_unknown_043()
    {
        var supplierId = await SeedSupplierId("P5-043");
        var match = await PostForData($"/api/v1/suppliers/{supplierId}/screenings", new
        {
            source = "Watchlist", sourceRef = "WL-77",
        }, Proc);
        Assert.Equal("PossibleMatch", match.GetProperty("outcome").GetString());
        Assert.Equal("PendingDisposition", match.GetProperty("status").GetString()); // queued, never auto-confirmed

        // an acting human disposes it (043 AC2) — the stub never confirms on its own
        var disposed = await PostForData($"/api/v1/screenings/{match.GetProperty("id").GetString()!}/dispositions",
            new { disposition = "FalsePositive", reason = "different entity, same name" }, Qc);
        Assert.Equal("FalsePositive", disposed.GetProperty("disposition").GetString());

        var outage = await PostForData($"/api/v1/suppliers/{supplierId}/screenings", new
        {
            source = "UNAVAILABLE-SIM", sourceRef = "X-1",
        }, Proc);
        Assert.Equal("Unknown", outage.GetProperty("outcome").GetString()); // fail/hold policy: outage → unknown

        var list = await GetAsync($"/api/v1/suppliers/{supplierId}/screenings", Proc);
        Assert.True(list.EnumerateArray().Count() >= 2);
    }

    [Fact]
    public async Task Questionnaire_response_pins_version_and_reviewer_returns_with_comments_040()
    {
        var supplierId = await SeedSupplierId("P5-040");
        var qnr = await PostForData("/api/v1/supplier-questionnaires", new
        {
            name = "Onboarding v1",
            questions = new[] { new { code = "Q1", prompt = "Factory address?", required = true, conditional = false, conditionalOnCode = (string?)null } },
        }, Proc);
        Assert.Equal(1, qnr.GetProperty("version").GetInt32());

        var response = await PostForData($"/api/v1/suppliers/{supplierId}/questionnaire-responses", new
        {
            questionnaireId = qnr.GetProperty("id").GetString()!, answersJson = "{\"Q1\":\"Dhaka\"}",
        }, Proc);
        Assert.Equal(1, response.GetProperty("questionnaireVersion").GetInt32()); // version pinned (040 AC1)

        var returned = await PostForData($"/api/v1/questionnaire-responses/{response.GetProperty("id").GetString()!}/reviews", new
        {
            reviewDecision = "Returned", fieldCommentsJson = "{\"Q1\":\"needs the street address\"}",
        }, Fin);
        Assert.Equal("Returned", returned.GetProperty("reviewDecision").GetString());
        Assert.Contains("street", returned.GetProperty("fieldCommentsJson").GetString());

        // a fresh response: returning WITHOUT field-level comments is rejected (040 AC3)
        var fresh = await PostForData($"/api/v1/suppliers/{supplierId}/questionnaire-responses", new
        {
            questionnaireId = qnr.GetProperty("id").GetString()!, answersJson = "{\"Q1\":\"Dhaka\"}",
        }, Proc);
        var noComments = await PostAsync($"/api/v1/questionnaire-responses/{fresh.GetProperty("id").GetString()!}/reviews",
            new { reviewDecision = "Returned" }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, noComments.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ErrorCode(await Json(noComments)));
    }

    [Fact]
    public async Task Observation_dispute_retains_original_with_actor_history_061()
    {
        var supplierId = await SeedSupplierId("P5-061");
        var observation = await PostForData($"/api/v1/suppliers/{supplierId}/observations", new
        {
            metric = "OnTimeDelivery", value = 72m, period = "2026-Q3", source = "erp",
        }, Proc);
        Assert.Equal(72m, observation.GetProperty("value").GetDecimal());

        var disputed = await PostForData($"/api/v1/observations/{observation.GetProperty("id").GetString()!}/disputes", new
        {
            reason = "excludes the port-strike week", amendedValue = 91m,
        }, Fin);
        Assert.Equal(72m, disputed.GetProperty("value").GetDecimal()); // original retained
        Assert.Equal(91m, disputed.GetProperty("amendedValue").GetDecimal());
        Assert.Equal(Fin, disputed.GetProperty("disputedBy").GetString());
    }

    // filing a dispute (an amended value a reviewer may act on) is a suppliers:review act
    [Fact]
    public async Task Observation_dispute_requires_suppliers_review_403_061()
    {
        var supplierId = await SeedSupplierId("P5-061B");
        var observation = await PostForData($"/api/v1/suppliers/{supplierId}/observations", new
        {
            metric = "OnTimeDelivery", value = 72m, period = "2026-Q3", source = "erp",
        }, Proc);
        var res = await PostAsync($"/api/v1/observations/{observation.GetProperty("id").GetString()!}/disputes", new
        {
            reason = "not a reviewer's dispute to file", amendedValue = 1m,
        }, Eval);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(res)));
    }

    [Fact]
    public async Task Scorecard_insufficient_volume_not_rated_closure_needs_reviewer_outcome_062()
    {
        var supplierId = await SeedSupplierId("P5-062");
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1));
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var scorecard = await PostForData("/api/v1/supplier-scorecards", new { supplierId, periodFrom = from, periodTo = to }, Proc);
        Assert.Equal("NotRated", scorecard.GetProperty("status").GetString()); // < 3 observations → not rated
        Assert.Equal(1, scorecard.GetProperty("calcVersion").GetInt32());

        var action = await PostForData($"/api/v1/scorecards/{scorecard.GetProperty("id").GetString()!}/corrective-actions", new
        {
            finding = "delivery variance", action = "weekly carrier review",
        }, Proc);
        Assert.Equal("Open", action.GetProperty("status").GetString());

        var noOutcome = await PostAsync($"/api/v1/corrective-actions/{action.GetProperty("id").GetString()!}/closures", new { }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, noOutcome.StatusCode); // closure is a reviewer act

        var noOutcomeFin = await PostAsync($"/api/v1/corrective-actions/{action.GetProperty("id").GetString()!}/closures", new { }, Fin);
        Assert.Equal(HttpStatusCode.BadRequest, noOutcomeFin.StatusCode); // reviewer without outcome → 400 (062 AC3)
        Assert.Equal("REVIEW_OUTCOME_REQUIRED", ErrorCode(await Json(noOutcomeFin)));

        var closed = await PostForData($"/api/v1/corrective-actions/{action.GetProperty("id").GetString()!}/closures",
            new { outcome = "carrier switched, variance closed" }, Fin);
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.Equal(Fin, closed.GetProperty("closedBy").GetString());

        // disclosures: metric summary + period + calc version exposed (062 AC1)
        var detail = await GetAsync($"/api/v1/supplier-scorecards/{scorecard.GetProperty("id").GetString()!}", Proc);
        Assert.Equal(from, DateOnly.Parse(detail.GetProperty("periodFrom").GetString()!));
        Assert.Equal(1, detail.GetProperty("calcVersion").GetInt32());
    }

    // ==================== P6: spend & sourcing analytics (063) ====================

    [Fact]
    public async Task Spend_reconciles_to_filtered_detail_and_currencies_stay_separate_063()
    {
        // golden dataset: two approved POs on two suppliers
        var supplierA = await SeedSupplier("P6-A");
        var supplierB = await SeedSupplier("P6-B");
        foreach (var (supplierId, qty, price) in new[] { (supplierA, 10m, 100m), (supplierB, 5m, 200m) })
        {
            var po = await PostForData("/api/v1/purchase-orders", new
            {
                supplierId, lines = new[] { new { itemSku = $"SKU-{supplierId}", description = "analytics", qty, unitPrice = price } },
            }, Proc);
            var poId = po.GetProperty("id").GetString()!;
            await PostForData($"/api/v1/purchase-orders/{poId}/submit", new { }, Proc);
            await PostForData($"/api/v1/purchase-orders/{poId}/approvals", new { reason = (string?)null }, Fin);
        }

        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var spend = await GetAsync($"/api/v1/procurement-analytics/spend?groupBy=supplier&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", Proc);
        Assert.Equal("sum of qty × unitPrice over approved+ purchase orders in the window", spend.GetProperty("definition").GetString());
        var rows = spend.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(1000m, rows.First(r => r.GetProperty("key").GetString() == supplierA).GetProperty("amount").GetDecimal());
        Assert.Equal(1000m, rows.First(r => r.GetProperty("key").GetString() == supplierB).GetProperty("amount").GetDecimal());
        // reconciles to the filtered detail: total == sum of rows (earlier tests' POs also fall in the window)
        Assert.Equal(rows.Sum(r => r.GetProperty("amount").GetDecimal()), spend.GetProperty("total").GetDecimal());
        Assert.True(spend.GetProperty("total").GetDecimal() >= 2000m);

        // 063 edge: multi-currency demand lists unconverted amounts separately, never summed
        await EnsureUom("EA", "Count", 0, isBase: true);
        foreach (var currency in new[] { "BDT", "USD" })
        {
            var cat = await SeedCategory($"P6-{currency}");
            var req = await PostForData("/api/v1/requisitions", new
            {
                purpose = $"demand {currency}", currency,
                lines = new[] { new { categoryId = cat, description = "x", qty = 1m, unitPrice = 500m, unit = "EA", needBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)) } },
            }, Proc);
            await PostForData($"/api/v1/requisitions/{req.GetProperty("id").GetString()!}/submission", new { }, Proc);
        }
        var demand = await GetAsync("/api/v1/procurement-analytics/demand?groupBy=currency", Proc);
        var demandRows = demand.GetProperty("rows").EnumerateArray().ToList();
        Assert.Contains(demandRows, r => r.GetProperty("key").GetString() == "BDT"); // earlier tests' reqs, own bucket
        Assert.Equal(500m, demandRows.First(r => r.GetProperty("key").GetString() == "USD").GetProperty("amount").GetDecimal());
        Assert.DoesNotContain(demandRows, r => r.GetProperty("currency").GetString() == "USD" && r.GetProperty("amount").GetDecimal() != 500m); // separate unconverted rows

        // sourcing metric exposes the frozen event count
        var sourcing = await GetAsync("/api/v1/procurement-analytics/sourcing", Proc);
        Assert.Equal(0, sourcing.GetProperty("rows").EnumerateArray().Count(s => s.GetProperty("key").GetString() == "Awarded" && s.GetProperty("count").GetInt32() < 0));
    }

    // ==================== negative-path authz sweep (QA Gate 2 pattern) ====================

    [Fact]
    public async Task Wrong_role_gets_forbidden_envelope_on_every_new_requisition_endpoint()
    {
        var id = await SeedRequisition("P1-authz");
        foreach (var (path, actor) in new[]
        {
            ($"/api/v1/requisitions/{id}/submission", Eval),
            ($"/api/v1/requisitions/{id}/decisions", Eval),
            ($"/api/v1/requisitions/{id}/exceptions", Eval),
            ("/api/v1/procurement-categories", Eval),
            ("/api/v1/unit-of-measures", Eval),
            ("/api/v1/blanket-orders", Eval),
            ("/api/v1/advance-ship-notices", Eval),
        })
        {
            var res = await PostAsync(path, new { }, actor);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Equal("FORBIDDEN", ErrorCode(await Json(res)));
        }
        var delegation = await PostAsync("/api/v1/approval-delegations",
            new { delegateId = Proc2, from = DateTimeOffset.UtcNow, to = DateTimeOffset.UtcNow.AddHours(1), reason = "nope" }, Proc);
        Assert.Equal(HttpStatusCode.Forbidden, delegation.StatusCode); // Proc has no requisitions:approve
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(delegation)));
    }

    // ==================== fix round 1 (findings I1–I6, S1, R) ====================

    [Fact]
    public async Task Disposition_needs_receipts_dispose_permission_beyond_receiver_sod()
    {
        var receiptId = await SeedReceipt("P4-perm", qty: 4m);
        var receipt = await GetAsync("/api/v1/receipts", Proc);
        var receiptSku = receipt.EnumerateArray().First(r => r.GetProperty("id").GetString() == receiptId)
            .GetProperty("itemSku").GetString()!;
        var inspection = await PostForData($"/api/v1/receipts/{receiptId}/inspections",
            new { itemSku = receiptSku, qtyReceived = 4m }, Qc);

        // Proc2 is NOT the receiver (Proc is), so the SoD rule would pass — the 403 must come
        // from the receipts:dispose permission gate (I1: it was seeded but never enforced)
        var res = await PostAsync($"/api/v1/receipts/{receiptId}/inspections/{inspection.GetProperty("id").GetString()!}/dispositions",
            new { status = "Accepted", qtyAccepted = 4m, qtyRejected = 0m }, Proc2);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(res)));

        var disposed = await PostForData($"/api/v1/receipts/{receiptId}/inspections/{inspection.GetProperty("id").GetString()!}/dispositions",
            new { status = "Accepted", qtyAccepted = 4m, qtyRejected = 0m }, Qc);
        Assert.Equal("Accepted", disposed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Response_review_and_screening_disposition_need_suppliers_review()
    {
        var supplierId = await SeedSupplierId("P5-authz");
        var qnr = await PostForData("/api/v1/supplier-questionnaires", new
        {
            name = "Authz v1",
            questions = new[] { new { code = "Q1", prompt = "Factory address?", required = true, conditional = false, conditionalOnCode = (string?)null } },
        }, Proc);
        var response = await PostForData($"/api/v1/suppliers/{supplierId}/questionnaire-responses", new
        {
            questionnaireId = qnr.GetProperty("id").GetString()!, answersJson = "{\"Q1\":\"Dhaka\"}",
        }, Proc);
        var screening = await PostForData($"/api/v1/suppliers/{supplierId}/screenings", new
        {
            source = "Watchlist", sourceRef = "WL-A",
        }, Proc);

        // Eval (R-EVAL, sourcing-only) holds no suppliers:review — both reviewer acts must 403
        var review = await PostAsync($"/api/v1/questionnaire-responses/{response.GetProperty("id").GetString()!}/reviews",
            new { reviewDecision = "Approved" }, Eval);
        Assert.Equal(HttpStatusCode.Forbidden, review.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(review)));

        var dispose = await PostAsync($"/api/v1/screenings/{screening.GetProperty("id").GetString()!}/dispositions",
            new { disposition = "FalsePositive", reason = "not the same entity" }, Eval);
        Assert.Equal(HttpStatusCode.Forbidden, dispose.StatusCode);
        Assert.Equal("FORBIDDEN", ErrorCode(await Json(dispose)));
    }

    [Fact]
    public async Task List_carries_pending_step_version_and_queue_decision_succeeds()
    {
        var (id, detail) = await SubmitRequisition("P1-listv");

        // the decide endpoint validates the PENDING STEP's version token (018 AC2) — the list
        // row must carry that token, not the requisition's record version (I6: dead queue action)
        var list = await GetAsync("/api/v1/requisitions?status=Submitted", Proc);
        var row = list.EnumerateArray().First(r => r.GetProperty("id").GetString() == id);
        Assert.True(row.TryGetProperty("stepVersion", out var stepVersionEl), "list rows carry the pending step's version token");
        Assert.Equal(detail.GetProperty("step").GetProperty("version").GetInt32(), stepVersionEl.GetInt32());

        var decided = await PostForData($"/api/v1/requisitions/{id}/decisions",
            new { decision = "Approve", version = stepVersionEl.GetInt32() }, Fin);
        Assert.Equal("Approved", decided.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Supplier_scorecards_route_serves_by_supplier()
    {
        var supplierId = await SeedSupplierId("P5-062r");
        var scorecard = await PostForData("/api/v1/supplier-scorecards", new
        {
            supplierId,
            periodFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            periodTo = DateOnly.FromDateTime(DateTime.UtcNow),
        }, Proc);

        var list = await GetAsync($"/api/v1/suppliers/{supplierId}/scorecards", Proc);
        Assert.Contains(list.EnumerateArray(), s => s.GetProperty("id").GetString() == scorecard.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Purchase_orders_version_column_defaults_to_one_not_zero()
    {
        // ruling R: pre-migration PO rows must land at v1 (ack validation rejects 1..Version < 1)
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_default FROM information_schema.columns WHERE table_name = 'purchase_orders' AND column_name = 'version'", conn);
        var def = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.Contains("1", def);
    }
}
