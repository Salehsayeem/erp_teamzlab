using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// MF4 — MRP run → publication → conversion (ERP-MFG-033..040, Q-MF1 FULL pipeline).
// The run is an immutable record: inputs + a stock/BOM/lead-time snapshot frozen BEFORE
// processing, so the same snapshot+params replay to byte-identical recommendations (033).
// Netting excludes open quality holds and is tenant-scoped by construction (034); explosion
// carries a traversable parent path and fails the run on circular BOMs with the path in the
// diagnostic (035); missing lead time is an exception, never a zero-day date (036). Modifiers
// (min-lot / multiple / safety stock) are recorded per applied modifier with the resulting
// quantity (039). Conversion creates exactly one linked order — idempotent on retry — through
// the landed production-order path for make items and the IRequisitionSink stub for buy items
// (037). Publication gates on snapshot drift unless an override reason is supplied and is
// idempotent per run (040).
public sealed class MrpService(
    IMfgDeferredRepository mfg, IManufacturingRepository landed, IInventoryRepository inventory,
    IProductionOrderService production, IRequisitionSink requisitions,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IMrpService
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record DemandInput(string ItemSku, decimal Qty, string Unit, DateOnly? By);

    // STJ cannot round-trip value tuples — BOM children serialize as records so the frozen
    // snapshot replays cleanly (033)
    private sealed record ChildRef(string Sku, decimal QtyPer);

    private sealed record SkuSnapshot(decimal Available, decimal OpenSupply, string? BomRevisionId,
        decimal OutputQtyPer, List<ChildRef> Children, int? LeadTimeDays);

    private sealed record RunSnapshot(List<DemandInput> Demands, Dictionary<string, SkuSnapshot> Skus);

    public (object data, string? code, string? message) CreateRun(CurrentUser user, Dtos.CreateMrpRunRequest request)
    {
        if (request.Demands.Count == 0) return (null!, "VALIDATION_ERROR", "At least one demand is required.");
        foreach (var d in request.Demands)
        {
            if (Guards.Check(d.ItemSku, "ItemSku") is { } skuError) return (null!, "VALIDATION_ERROR", skuError);
            if (Guards.CheckPositive(d.Qty, "Qty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);
            if (Guards.Check(d.Unit, "Unit") is { } unitError) return (null!, "VALIDATION_ERROR", unitError);
        }
        if (request.HorizonTo < request.HorizonFrom) return (null!, "VALIDATION_ERROR", "Horizon end must not be before start.");
        if (request.SafetyStockPct is { } s && s < 0) return (null!, "VALIDATION_ERROR", "SafetyStockPct must be >= 0.");
        if (request.MinLotQty is { } m && m <= 0) return (null!, "VALIDATION_ERROR", "MinLotQty must be > 0.");
        if (request.MultipleQty is { } q && q <= 0) return (null!, "VALIDATION_ERROR", "MultipleQty must be > 0.");

        var parameters = JsonSerializer.Serialize(new
        {
            request.HorizonFrom, request.HorizonTo,
            safetyStockPct = request.SafetyStockPct, minLotQty = request.MinLotQty, multipleQty = request.MultipleQty,
        });

        // snapshot FIRST (033): availability, open supply, BOM structure and lead times are
        // frozen before a single recommendation is computed
        var run = new MrpRun(numbers.Next("mrp"), ec.TenantId, ec.EntityId, MrpRunStatus.InProgress,
            request.HorizonFrom, request.HorizonTo, parameters, "{}", null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddMrpRun(run);

        var demands = request.Demands.Select(d => new DemandInput(d.ItemSku, d.Qty, d.Unit, d.By)).ToList();
        var snapshot = Snapshot(demands);
        var snapshotJson = JsonSerializer.Serialize(snapshot, Idem.JsonOptions);
        mfg.SetMrpRunSnapshot(run, snapshotJson);

        // circular BOM → the run FAILS with the path in the diagnostic (035 AC3)
        var cycle = FindCycle(snapshot);
        if (cycle is not null)
        {
            mfg.SetMrpRunStatus(run, MrpRunStatus.Failed,
                $"Circular BOM: {cycle} — the explosion cannot traverse a loop (035).");
            mfg.Save();
            return (ProjRun(run, [], []), null, null);
        }

        var lines = Explode(run, snapshot, demands);
        var messages = ExceptionMessages(run, lines);
        mfg.SetMrpRunStatus(run, MrpRunStatus.Completed, null);
        audit.Log(user, "create", "mrp_run", run.Id,
            $"MRP run {run.Id}: {demands.Count} demand root(s), {lines.Count} recommendation line(s), {messages.Count} exception message(s)");
        mfg.Save();
        return (ProjRun(run, lines, messages), null, null);
    }

    // freeze per-sku inputs: availability (stock minus open quality holds), open production-order
    // supply, the effective approved BOM, and the routing lead-time rollup
    private RunSnapshot Snapshot(List<DemandInput> demands)
    {
        var skus = new Dictionary<string, SkuSnapshot>();
        var pending = new Queue<string>(demands.Select(d => d.ItemSku));
        while (pending.Count > 0)
        {
            var sku = pending.Dequeue();
            if (skus.ContainsKey(sku)) continue;
            var bom = landed.EffectiveRevisionsFor(sku, Today).FirstOrDefault();
            var children = bom is null ? [] : landed.LinesForRevision(bom.Id)
                .Select(l => new ChildRef(l.ComponentSku, l.QtyPer)).ToList();
            foreach (var c in children) pending.Enqueue(c.Sku);
            skus[sku] = new SkuSnapshot(
                Available: inventory.QtyOf(sku) - mfg.OpenHoldsForItem(sku).Sum(h => h.Qty),
                OpenSupply: OpenSupplyFor(sku),
                BomRevisionId: bom?.Id,
                OutputQtyPer: bom?.OutputQtyPer ?? 1m,
                Children: children,
                LeadTimeDays: LeadTimeDaysFor(sku));
        }
        return new RunSnapshot(demands, skus);
    }

    // lead-time rollup (036): Σ(operation durations) of the effective approved routing, in whole
    // days; a routing with no duration data (or none at all) is MISSING, never zero-day
    private int? LeadTimeDaysFor(string sku)
    {
        var routing = mfg.EffectiveRoutingsFor(sku, Today).FirstOrDefault();
        if (routing is null) return null;
        var ops = mfg.RoutingOperationsFor(routing.Id);
        if (ops.Count == 0) return null;
        var totalMin = ops.Sum(o => o.IsZeroDuration ? 0 : o.SetupMin + o.PerUnitMin);
        return totalMin <= 0 ? null : (int)Math.Ceiling(totalMin / 1440m);
    }

    // scheduled supply (034): open (not completed/closed/cancelled) production orders' remaining qty
    private decimal OpenSupplyFor(string sku) =>
        landed.AllOrders(null, sku)
            .Where(o => o.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Released
                or ProductionOrderStatus.InProcess)
            .Sum(o => Math.Max(o.PlannedQty - o.CompletedQty, 0));

    private string? FindCycle(RunSnapshot snapshot)
    {
        foreach (var root in snapshot.Demands.Select(d => d.ItemSku).Distinct())
        {
            var path = new List<string>();
            if (Walks(root, root, snapshot.Skus, path, new HashSet<string>()))
                return string.Join(" → ", path);
        }
        return null;

        static bool Walks(string root, string current, Dictionary<string, SkuSnapshot> skus,
            List<string> path, HashSet<string> visited)
        {
            path.Add(current);
            if (!skus.TryGetValue(current, out var snap)) return false;
            if (!visited.Add(current)) return path.Contains(root);
            foreach (var child in snap.Children)
                if (Walks(root, child.Sku, skus, path, visited)) return true;
            path.RemoveAt(path.Count - 1);
            return false;
        }
    }

    private List<MrpLine> Explode(MrpRun run, RunSnapshot snapshot, List<DemandInput> demands)
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>(run.ParametersJson, Idem.JsonOptions);
        decimal? safety = parameters.TryGetProperty("safetyStockPct", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetDecimal() : null;
        decimal? minLot = parameters.TryGetProperty("minLotQty", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDecimal() : null;
        decimal? multiple = parameters.TryGetProperty("multipleQty", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetDecimal() : null;

        var lines = new List<MrpLine>();
        var lineNo = 0;
        // (sku, gross, by, level, parentPath) — ordered processing keeps output deterministic
        var queue = new Queue<(string Sku, decimal Gross, DateOnly? By, int Level, string Path)>(
            demands.Select(d => (d.ItemSku, d.Qty, d.By, 0, d.ItemSku)));
        while (queue.Count > 0)
        {
            var (sku, gross, by, level, path) = queue.Dequeue();
            var snap = snapshot.Skus.TryGetValue(sku, out var s) ? s : new SkuSnapshot(0, 0, null, 1m, [], null);

            // netting (034): supply = availability + scheduled supply, counted once
            var supply = Math.Min(gross, Math.Max(snap.Available, 0) + Math.Max(snap.OpenSupply, 0));
            var net = Math.Max(gross - supply, 0);

            // modifiers (039): each applied modifier recorded with its input + resulting qty
            var modifiers = new List<object>();
            var modifierInput = net;
            if (net > 0)
            {
                if (safety is { } pct && pct > 0)
                {
                    var raised = decimal.Round(net * (1 + pct / 100m), 4, MidpointRounding.AwayFromZero);
                    if (raised != net) { modifiers.Add(new { code = "SAFETY_STOCK", input = net, result = raised }); net = raised; }
                }
                if (minLot is { } lot && net < lot)
                {
                    modifiers.Add(new { code = "MIN_LOT", input = net, result = lot });
                    net = lot;
                }
                if (multiple is { } mult && net % mult != 0)
                {
                    var rounded = Math.Ceiling(net / mult) * mult;
                    modifiers.Add(new { code = "MULTIPLE", input = net, result = rounded });
                    net = rounded;
                }
            }

            // lead time (036): missing data is an exception — never a zero-day date
            var leadDays = snap.LeadTimeDays;
            DateOnly? requiredBy = null;
            string? exception = null;
            if (net > 0)
            {
                if (leadDays is { } days)
                    requiredBy = (by ?? parameters.GetProperty("horizonFrom").Deserialize<DateOnly>()).AddDays(-days);
                else
                    exception = "MISSING_LEAD_TIME";
            }

            lineNo++;
            var line = new MrpLine(numbers.Next("mrp"), ec.TenantId, ec.EntityId, run.Id, lineNo,
                sku, level, path, gross, supply, net,
                JsonSerializer.Serialize(new { input = modifierInput, applied = modifiers }, Idem.JsonOptions),
                requiredBy, leadDays, exception,
                net > 0 ? "Pending" : "NotNeeded", null, null, null, run.CreatedBy, run.CreatedAt);
            lines.Add(line);
            mfg.AddMrpLine(line);

            foreach (var child in snap.Children)
            {
                var childGross = decimal.Round(child.QtyPer / Math.Max(snap.OutputQtyPer, 0.000001m) * net, 6, MidpointRounding.AwayFromZero);
                if (childGross <= 0) continue;
                var parentStart = requiredBy ?? by; // children need the parts when the parent STARTS
                queue.Enqueue((child.Sku, childGross, parentStart, level + 1, $"{path}>{child.Sku}"));
            }
        }
        return lines;
    }

    private List<ActionMessage> ExceptionMessages(MrpRun run, List<MrpLine> lines)
    {
        var messages = new List<ActionMessage>();
        foreach (var line in lines.Where(l => l.ExceptionCode is not null))
        {
            var message = new ActionMessage(numbers.Next("mrp"), ec.TenantId, ec.EntityId, run.Id,
                line.Id, line.ExceptionCode!, ExceptionText(line.ExceptionCode!, line), ActionMessageState.Open,
                null, null, null, run.CreatedBy, run.CreatedAt);
            messages.Add(message);
            mfg.AddActionMessage(message);
        }
        return messages;
    }

    private static string ExceptionText(string code, MrpLine line) => code switch
    {
        "MISSING_LEAD_TIME" => $"No lead-time data (routing) for {line.ItemSku} — the requirement is not dated (036 AC3).",
        _ => $"{code} on {line.ItemSku}.",
    };

    public object Recommendations(string runId) =>
        mfg.MrpLinesFor(runId).Select(ProjLine).ToList();

    // read-gap indexes: runs without their line/message payloads (per-row joins would N+1),
    // lines optionally scoped to one run
    public object Runs() => mfg.MrpRuns().Select(r => new
    {
        r.Id, status = r.Status.ToString(), r.HorizonFrom, r.HorizonTo,
        r.FailureDetail, r.CreatedBy, r.CreatedAt,
    }).ToList();

    public object Lines(string? runId) =>
        mfg.MrpLines(runId).Select(ProjLine).ToList();

    public object ActionMessages(string runId) =>
        mfg.ActionMessagesFor(runId).Select(m => new
        {
            m.Id, m.RunId, m.MrpLineId, m.Code, m.Message, state = m.State.ToString(),
            m.DismissReason, m.AcknowledgedBy, m.AcknowledgedAt, m.CreatedAt,
        }).ToList();

    // 037: one linked order per recommendation, idempotent on retry; rejected/deferred lines stay
    // visible with their reason
    public (object data, string? code, string? message) Convert(CurrentUser user, string lineId, Dtos.MrpConversionRequest request)
    {
        var line = mfg.FindMrpLine(lineId);
        if (line is null) return (null!, "MRP_LINE_NOT_FOUND", "MRP recommendation line not found.");
        if (line.ConversionStatus == "Converted")
            return (ProjConversion(line), "MRP_LINE_REPLAY", null); // idempotent replay — exactly one linked order
        if (line.ConversionStatus is "Rejected" or "Deferred")
            return (null!, "MRP_LINE_DECIDED", $"The recommendation was already {line.ConversionStatus.ToLowerInvariant()}: {line.DecisionReason}");
        if (line.ConversionStatus == "NotNeeded")
            return (null!, "MRP_LINE_NOT_NEEDED", "The line netted to zero — nothing to convert.");
        var run = mfg.FindMrpRun(line.RunId);
        if (run is null) return (null!, "MRP_RUN_NOT_FOUND", "MRP run not found.");
        if (run.Status != MrpRunStatus.Completed) return (null!, "MRP_RUN_STATE_ERROR", "Only completed runs convert.");
        if (run.CreatedBy == user.Id)
            return (null!, "MRP_CONVERSION_ERROR", "Maker-checker: the run maker cannot convert their own recommendation (037).");

        var note = $"MRP {run.Id} line {line.LineNo} ({line.ItemSku})" + (request.Note is null ? "" : $" — {request.Note}");
        if (line.ExceptionCode == "MISSING_LEAD_TIME")
            return (null!, "MRP_LINE_NOT_DATED", "The recommendation has no lead-time data — resolve the exception before converting (036).");

        string? orderId = null;
        string? requisitionRef = null;
        var snapshot = JsonSerializer.Deserialize<RunSnapshot>(run.SnapshotJson, Idem.JsonOptions);
        var isMake = snapshot?.Skus.TryGetValue(line.ItemSku, out var skuSnap) == true && skuSnap!.BomRevisionId is not null;
        if (isMake)
        {
            // make path: the linked order goes through the LANDED production-order service so the
            // BOM resolution + guards stay single-sourced
            var demandUnit = snapshot!.Demands.FirstOrDefault(d => d.ItemSku == line.ItemSku)?.Unit ?? "pcs";
            var (order, code, message) = production.Create(user, new Dtos.CreateProductionOrderRequest(
                line.ItemSku, line.NetQty, demandUnit, 0m));
            if (code is not null) return (null!, code, message);
            orderId = JsonSerializer.SerializeToElement(order!, Idem.JsonOptions).GetProperty("id").GetString();
        }
        else
        {
            // buy path: the IRequisitionSink stub until PROC binds (Q-MF1)
            var (reference, error) = requisitions.CreateForMrpLine(user, line, note);
            if (error is not null) return (null!, "REQUISITION_SINK_ERROR", error);
            requisitionRef = reference;
        }

        mfg.SetMrpLineConversion(line, "Converted", orderId, requisitionRef, request.Note);
        audit.Log(user, "convert", "mrp_line", line.Id, $"Recommendation converted: {line.NetQty} x {line.ItemSku} → {orderId ?? requisitionRef}");
        mfg.Save();
        return (ProjConversion(line), null, null);
    }

    public (object data, string? code, string? message) DecideLine(CurrentUser user, string lineId, Dtos.MrpLineDecisionRequest request)
    {
        var line = mfg.FindMrpLine(lineId);
        if (line is null) return (null!, "MRP_LINE_NOT_FOUND", "MRP recommendation line not found.");
        if (request.Action is not ("reject" or "defer"))
            return (null!, "VALIDATION_ERROR", "Action must be reject or defer.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (null!, "VALIDATION_ERROR", "A decision reason is required (037 AC3).");
        if (line.ConversionStatus == "Converted")
            return (null!, "MRP_LINE_DECIDED", "The recommendation was already converted.");
        if (line.ConversionStatus is "Rejected" or "Deferred")
            return (null!, "MRP_LINE_DECIDED", $"The recommendation was already {line.ConversionStatus.ToLowerInvariant()}.");

        mfg.SetMrpLineConversion(line, request.Action == "reject" ? "Rejected" : "Deferred", null, null, request.Reason);
        audit.Log(user, "decide", "mrp_line", line.Id, $"Recommendation {request.Action}: {request.Reason}");
        mfg.Save();
        return (ProjLine(line), null, null);
    }

    // 040: reviewer ≠ run maker; stale critical sources (snapshot vs live availability) block
    // unless an override reason is recorded; publication idempotent per run
    public (object data, string? code, string? message) Publish(CurrentUser reviewer, string runId, Dtos.MrpPublicationRequest request)
    {
        var run = mfg.FindMrpRun(runId);
        if (run is null) return (null!, "MRP_RUN_NOT_FOUND", "MRP run not found.");
        if (mfg.FindMrpPublication(runId) is { } existing) return (ProjPublication(existing), null, null);
        if (run.Status != MrpRunStatus.Completed)
            return (null!, "MRP_RUN_STATE_ERROR", "Only completed runs publish.");
        if (run.CreatedBy == reviewer.Id)
            return (null!, "MRP_PUBLISH_ERROR", "Maker-checker: the run maker cannot publish their own run (040).");

        var staleReasons = StaleSources(run);
        var stale = staleReasons.Count > 0;
        if (stale && string.IsNullOrWhiteSpace(request.OverrideReason))
            return (null!, "MRP_SOURCES_STALE",
                "Critical sources drifted since the snapshot (" + string.Join("; ", staleReasons) +
                ") — publish requires an override reason (040 AC3).");

        var publication = new MrpPublication(numbers.Next("mrp"), ec.TenantId, ec.EntityId, runId,
            reviewer.Id, stale, request.OverrideReason, request.ExceptionDisposition ?? "none", DateTimeOffset.UtcNow);
        if (!mfg.AddMrpPublication(publication))
            return (ProjPublication(mfg.FindMrpPublication(runId)!), null, null);
        audit.Log(reviewer, "publish", "mrp_run", runId,
            $"MRP run published{(stale ? " with stale-source override" : "")} by {reviewer.Email}");
        audit.Emit(reviewer, "mfg.mrp.published", new { runId, reviewer = reviewer.Id, overrodeStaleSources = stale });
        mfg.Save();
        return (ProjPublication(publication), null, null);
    }

    private List<string> StaleSources(MrpRun run)
    {
        var reasons = new List<string>();
        var snapshot = JsonSerializer.Deserialize<RunSnapshot>(run.SnapshotJson, Idem.JsonOptions);
        if (snapshot is null) return reasons;
        foreach (var sku in snapshot.Skus.Keys)
        {
            var now = inventory.QtyOf(sku) - mfg.OpenHoldsForItem(sku).Sum(h => h.Qty);
            if (now != snapshot.Skus[sku].Available)
                reasons.Add($"{sku}: availability {snapshot.Skus[sku].Available} → {now}");
        }
        return reasons;
    }

    private static object ProjRun(MrpRun run, List<MrpLine> lines, List<ActionMessage> messages) => new
    {
        run.Id, status = run.Status.ToString(), run.HorizonFrom, run.HorizonTo,
        run.ParametersJson, run.SnapshotJson, run.FailureDetail, run.CreatedBy, run.CreatedAt,
        lines = lines.Select(ProjLine).ToList(),
        messages = messages.Select(m => new { m.Id, m.Code, m.Message, state = m.State.ToString() }).ToList(),
    };

    private static object ProjLine(MrpLine l) => new
    {
        l.Id, l.RunId, l.LineNo, l.ItemSku, l.ExplosionLevel, l.ParentPath, l.GrossQty, l.SupplyQty,
        l.NetQty, l.ModifiersJson, l.RequiredBy, l.LeadTimeDays, l.ExceptionCode,
        conversionStatus = l.ConversionStatus, l.ConvertedOrderId, l.ConvertedRequisitionRef, l.DecisionReason,
    };

    private static object ProjConversion(MrpLine l) => new
    {
        l.Id, l.RunId, l.ItemSku, l.NetQty, conversionStatus = l.ConversionStatus,
        l.ConvertedOrderId, l.ConvertedRequisitionRef,
    };

    private static object ProjPublication(MrpPublication p) => new
    {
        p.Id, p.RunId, p.ReviewerId, p.OverrodeStaleSources, p.OverrideReason, p.ExceptionDisposition, p.At,
    };
}
