using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// ERP-ASSETS-048 AC2 period control: RunDepreciation uses the shared FinanceGuards lock (T5) —
// today's books must be Open, and a target label naming a real fiscal period must be Open too.

// Fixed-asset register: create (draft) → capitalization proposal + checker approval (CIP → Active,
// journal Dr 1600 / Cr 1610) → monthly straight-line runs (Dr 6400 / Cr 1500), transfers with
// history, impairment (Dr 6600 / Cr 1500 per R-T4k) and disposal with gain/loss handoff (055/056).
public sealed class AssetService(
    IAssetsRepository assets, IJournalRepository journals, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec, ErpDbContext db) : IAssetService
{
    public const int DepreciationMonths = 60; // R8e: 5-year straight line, zero residual — pilot policy

    public object List(AssetStatus? status) => assets.AllAssets(status).Select(Proj).ToList();

    public (object? data, string? code, string? message) Create(CurrentUser user, CreateAssetRequest request)
    {
        if (assets.FindLocationInEntity(request.LocationId) is null)
            return (null, "LOCATION_NOT_FOUND", "Location not found.");
        if (assets.AssetTagOrSerialInActiveUse(request.Tag, request.Serial))
            return (null, "ASSET_DUPLICATE_TAG", $"An active asset already uses tag '{request.Tag}' or serial '{request.Serial}'.");

        var asset = new FixedAsset(numbers.Next("FA"), ec.TenantId, ec.EntityId, request.Tag, request.Serial,
            request.Name, request.Category, request.Cost, 0m, request.LocationId, AssetStatus.Draft,
            SourceProjectId: null, InServiceDate: null, user.Id, DateTimeOffset.UtcNow);
        assets.AddAsset(asset);
        audit.Log(user, "create", "fixed_asset", asset.Id, $"Asset '{request.Name}' created at cost {request.Cost} (draft)");
        assets.Save();
        return (Proj(asset), null, null);
    }

    // CIP → active proposal (044): the amount must settle the asset's CIP cost exactly; approval
    // posts Dr 1600 / Cr 1610 and puts the asset in service
    public (object? data, string? code, string? message) ProposeCapitalization(CurrentUser user, string assetId, string? sourceProjectId, decimal amount)
    {
        var asset = assets.FindAsset(assetId);
        if (asset is null) return (null, "ASSET_NOT_FOUND", "Fixed asset not found.");
        if (asset.Status is not (AssetStatus.Draft or AssetStatus.Cip))
            return (null, "ASSET_STATE_ERROR", $"Only draft/CIP assets can be capitalized (current: {asset.Status}).");
        if (assets.FindPendingEvent(assetId, "capitalize") is not null)
            return (null, "ASSET_STATE_ERROR", "A capitalization proposal is already pending for this asset.");
        if (amount != asset.Cost)
            return (null, "VALIDATION_ERROR", $"Capitalization amount must equal the CIP cost of {asset.Cost:0.00} (044 AC1: allocations sum exactly to the approved CIP amount).");
        if (!string.IsNullOrWhiteSpace(sourceProjectId) && assets.FindProject(sourceProjectId) is null)
            return (null, "PROJECT_NOT_FOUND", "Source project not found.");

        var evt = NewEvent(user, assetId, "capitalize", amount);
        if (!string.IsNullOrWhiteSpace(sourceProjectId)) assets.SetAssetSourceProject(asset, sourceProjectId);
        assets.AddEvent(evt);
        audit.Log(user, "propose", "asset_capitalization", evt.Id,
            $"Capitalization of {amount:0.00} proposed for {assetId}{(sourceProjectId is null ? "" : $" from project {sourceProjectId}")}");
        assets.Save();
        return (new { @event = ProjEvent(evt), asset = Proj(asset) }, null, null);
    }

    public (object? data, string? code, string? message) ApproveCapitalization(CurrentUser approver, string assetId, string eventId)
    {
        var (asset, evt, code, message) = LoadPending(approver, assetId, eventId, "capitalize", "assets:approve");
        if (code is not null) return (null, code, message);
        if (asset!.Status is not (AssetStatus.Draft or AssetStatus.Cip))
            return (null, "ASSET_STATE_ERROR", $"Only draft/CIP assets can be capitalized (current: {asset.Status}).");

        var journal = PostJournal(approver, $"CIP settlement {assetId} ({eventId})",
            [new JournalLine("1600", "Fixed Asset - Cost", evt!.Amount, 0),
             new JournalLine("1610", "Construction in Progress - Cost", 0, evt.Amount)]);
        assets.SetAssetStatus(asset, AssetStatus.Active);
        assets.SetAssetInServiceDate(asset, DateTimeOffset.UtcNow);
        assets.SetEventJournal(evt, journal.Id);
        audit.Log(approver, "approve", "asset_capitalization", eventId, $"Capitalization approved by {approver.Email}; asset {assetId} in service; journal {journal.Id}");
        audit.Emit(approver, "assets.asset.capitalized", new { assetId, eventId, amount = evt.Amount, journalId = journal.Id });
        assets.Save();
        return (new { @event = ProjEvent(FindEvent(eventId)), asset = Proj(asset) }, null, null);
    }

    // one balanced journal per run; the Idempotency-Key IS the run id (048 AC1)
    public (object? data, string? code, string? message) RunDepreciation(CurrentUser user, string? idempotencyKey, string periodId)
    {
        // 048 AC2 on the real periods table (T5): the run posts into today's books — they must be
        // Open (fail-closed when unbooked) — and a target label naming a real fiscal period must
        // be Open too. Labels with no fiscal period keep working (arbitrary run tags).
        var (periodCode, periodMessage) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
        if (periodCode is not null) return (null, periodCode, periodMessage);
        var target = db.FiscalPeriods.FirstOrDefault(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId && p.Name == periodId);
        if (target is { Status: PeriodStatus.SoftClosed or PeriodStatus.Closed })
            return (null, "PERIOD_LOCKED", $"Period {periodId} is {target.Status} for depreciation posting (048 AC2).");
        if (assets.DepreciationPeriodAlreadyPosted(periodId))
            return (null, "DEPRECIATION_PERIOD_ALREADY_POSTED", $"Period {periodId} already has a posted depreciation run.");
        var eligible = assets.DepreciableAssets();
        if (eligible.Count == 0)
            return (null, "DEPRECIATION_NOTHING_TO_POST", "No active assets have depreciable basis remaining.");

        var runId = string.IsNullOrWhiteSpace(idempotencyKey) ? numbers.Next("DEP") : idempotencyKey;
        var lines = new List<JournalLine>();
        var posted = new List<(FixedAsset Asset, decimal Amount)>();
        foreach (var asset in eligible)
        {
            var monthly = decimal.Round(asset.Cost / DepreciationMonths, 2, MidpointRounding.AwayFromZero);
            var amount = Math.Min(monthly, asset.Cost - asset.AccumulatedDepreciation); // last period lands on cost
            lines.Add(new JournalLine("6400", "Depreciation Expense", amount, 0));
            lines.Add(new JournalLine("1500", "Accumulated Depreciation", 0, amount));
            posted.Add((asset, amount));
        }
        var journal = PostJournal(user, $"Depreciation run {runId} period {periodId}", [.. lines]);

        foreach (var (asset, amount) in posted)
        {
            assets.SetAssetAccumulated(asset, asset.AccumulatedDepreciation + amount);
            assets.AddEvent(new AssetEvent(numbers.Next("FAE"), ec.TenantId, ec.EntityId, asset.Id, "depreciate",
                amount, journal.Id, user.Id, DateTimeOffset.UtcNow));
        }
        var total = posted.Sum(p => p.Amount);
        assets.AddRun(new DepreciationRun(runId, ec.TenantId, ec.EntityId, periodId, total, journal.Id, user.Id, DateTimeOffset.UtcNow));
        audit.Log(user, "post", "depreciation", journal.Id,
            $"Depreciation run {runId} for period {periodId}: {total:0.00} across {posted.Count} asset(s); journal {journal.Id}");
        audit.Emit(user, "assets.depreciation.posted", new { runId, periodId, totalAmount = total, journalId = journal.Id, assetCount = posted.Count });
        assets.Save();
        return (new
        {
            id = runId,
            periodId,
            periodState = target?.Status.ToString() ?? "Open", // only Open-labeled runs reach here
            totalAmount = total,
            journalId = journal.Id,
            lines = posted.Select(p => new { assetId = p.Asset.Id, amount = p.Amount }).ToList(),
        }, null, null);
    }

    public (object? data, string? code, string? message) ProposeTransfer(CurrentUser user, string assetId, string toLocationId, DateTimeOffset effectiveAt)
    {
        var asset = assets.FindAsset(assetId);
        if (asset is null) return (null, "ASSET_NOT_FOUND", "Fixed asset not found.");
        if (asset.Status == AssetStatus.Disposed)
            return (null, "ASSET_STATE_ERROR", "A disposed asset cannot be transferred.");
        if (toLocationId == asset.LocationId)
            return (null, "VALIDATION_ERROR", $"Asset is already located at {toLocationId}.");
        var location = assets.FindLocationInEntity(toLocationId);
        if (location is null)
            return assets.FindLocationInTenant(toLocationId) is not null
                ? (null, "TRANSFER_CROSS_ENTITY_UNSUPPORTED", "Cross-entity transfers are not supported in the pilot (R8b — intercompany handoff deferred).")
                : (null, "LOCATION_NOT_FOUND", "Location not found.");

        var from = asset.LocationId;
        var evt = new AssetEvent(numbers.Next("FAE"), ec.TenantId, ec.EntityId, assetId, "transfer",
            0m, "", user.Id, effectiveAt, from, toLocationId);
        assets.AddEvent(evt);
        assets.SetAssetLocation(asset, toLocationId);
        audit.Log(user, "transfer", "fixed_asset", assetId,
            $"Asset {assetId} transferred {from} → {toLocationId} effective {effectiveAt:u}");
        assets.Save();
        return (new { @event = ProjEvent(evt), asset = Proj(asset) }, null, null);
    }

    public (object? data, string? code, string? message) ProposeImpairment(CurrentUser user, string assetId, decimal amount, string reason)
    {
        var asset = assets.FindAsset(assetId);
        if (asset is null) return (null, "ASSET_NOT_FOUND", "Fixed asset not found.");
        if (asset.Status != AssetStatus.Active)
            return (null, "ASSET_STATE_ERROR", $"Only active assets can be impaired (current: {asset.Status}).");
        if (assets.FindPendingEvent(assetId, "impair") is not null)
            return (null, "ASSET_STATE_ERROR", "An impairment proposal is already pending for this asset.");
        var carrying = asset.Cost - asset.AccumulatedDepreciation;
        if (amount > carrying)
            return (null, "VALIDATION_ERROR", $"Impairment amount must not exceed the carrying value of {carrying:0.00}.");

        var evt = NewEvent(user, assetId, "impair", amount);
        assets.AddEvent(evt);
        audit.Log(user, "propose", "asset_impairment", evt.Id, $"Impairment of {amount:0.00} proposed for {assetId}: {reason}");
        assets.Save();
        return (new
        {
            @event = ProjEvent(evt),
            preview = new { priorCarryingAmount = carrying, adjustment = amount, resultingAmount = carrying - amount },
        }, null, null);
    }

    public (object? data, string? code, string? message) ApproveImpairment(CurrentUser approver, string assetId, string eventId)
    {
        var (asset, evt, code, message) = LoadPending(approver, assetId, eventId, "impair", "assets:approve");
        if (code is not null) return (null, code, message);
        if (asset!.Status != AssetStatus.Active) // finding 2: re-enforce the propose-time guard —
            // a stale impairment must not post against a disposed asset
            return (null, "ASSET_STATE_ERROR", $"Only active assets can be impaired (current: {asset.Status}).");

        // R-T4k: impairment credits Accumulated Depreciation, NOT 1600 — crediting the cost
        // account would strand a permanent credit in 1600 once disposal writes off Accum
        var journal = PostJournal(approver, $"Impairment {assetId} ({eventId})",
            [new JournalLine("6600", "Impairment Loss", evt!.Amount, 0),
             new JournalLine("1500", "Accumulated Depreciation", 0, evt.Amount)]);
        assets.SetAssetAccumulated(asset, asset.AccumulatedDepreciation + evt.Amount);
        assets.SetEventJournal(evt, journal.Id);
        audit.Log(approver, "approve", "asset_impairment", eventId, $"Impairment of {evt.Amount:0.00} approved by {approver.Email}; journal {journal.Id}");
        audit.Emit(approver, "assets.asset.impaired", new { assetId, eventId, amount = evt.Amount, journalId = journal.Id });
        assets.Save();
        return (new { @event = ProjEvent(FindEvent(eventId)), asset = Proj(asset) }, null, null);
    }

    public (object? data, string? code, string? message) ProposeDisposal(CurrentUser user, string assetId, decimal proceeds, string reason)
    {
        var asset = assets.FindAsset(assetId);
        if (asset is null) return (null, "ASSET_NOT_FOUND", "Fixed asset not found.");
        if (asset.Status != AssetStatus.Active)
            return (null, "ASSET_STATE_ERROR", $"Only active assets can be disposed (current: {asset.Status}).");
        if (assets.FindPendingEvent(assetId, "dispose") is not null)
            return (null, "ASSET_STATE_ERROR", "A disposal proposal is already pending for this asset.");

        var carrying = asset.Cost - asset.AccumulatedDepreciation;
        var evt = NewEvent(user, assetId, "dispose", proceeds);
        assets.AddEvent(evt);
        audit.Log(user, "propose", "asset_disposal", evt.Id, $"Disposal proposed for {assetId} at proceeds {proceeds:0.00}: {reason}");
        assets.Save();
        return (new
        {
            @event = ProjEvent(evt),
            preview = new
            {
                carryingValue = carrying,
                proceeds,
                gainLoss = proceeds - carrying,
                outcome = proceeds > carrying ? "Gain" : proceeds < carrying ? "Loss" : "BreakEven",
            },
        }, null, null);
    }

    public (object? data, string? code, string? message) ApproveDisposal(CurrentUser approver, string assetId, string eventId)
    {
        var (asset, evt, code, message) = LoadPending(approver, assetId, eventId, "dispose", "assets:approve");
        if (code is not null) return (null, code, message);
        if (asset!.Status != AssetStatus.Active) // finding 2: re-enforce the propose-time guard
            return (null, "ASSET_STATE_ERROR", $"Only active assets can be disposed (current: {asset.Status}).");

        // 055/056 handoff — one balanced journal; zero-amount lines are skipped and exactly one
        // of 6500/6100 (never both non-zero)
        var carrying = asset!.Cost - asset.AccumulatedDepreciation;
        var proceeds = evt!.Amount;
        var loss = carrying > proceeds ? carrying - proceeds : 0m;
        var gain = proceeds > carrying ? proceeds - carrying : 0m;
        var lines = new List<JournalLine>();
        if (asset.AccumulatedDepreciation > 0m)
            lines.Add(new JournalLine("1500", "Accumulated Depreciation", asset.AccumulatedDepreciation, 0));
        if (proceeds > 0m) lines.Add(new JournalLine("1200", "Accounts Receivable", proceeds, 0));
        if (loss > 0m) lines.Add(new JournalLine("6500", "Loss on Asset Disposal", loss, 0));
        lines.Add(new JournalLine("1600", "Fixed Asset - Cost", 0, asset.Cost));
        if (gain > 0m) lines.Add(new JournalLine("6100", "Gain on Asset Disposal", 0, gain));

        var journal = PostJournal(approver, $"Disposal {assetId} ({eventId})", [.. lines]);
        assets.SetAssetStatus(asset, AssetStatus.Disposed); // terminal — depreciation stops (Disposed never re-enters DepreciableAssets)
        assets.SetEventJournal(evt, journal.Id);
        audit.Log(approver, "approve", "asset_disposal", eventId,
            $"Disposal approved by {approver.Email}; carrying {carrying:0.00}, proceeds {proceeds:0.00}, {(loss > 0 ? $"loss {loss:0.00}" : gain > 0 ? $"gain {gain:0.00}" : "break-even")}; journal {journal.Id}");
        audit.Emit(approver, "assets.asset.disposed", new { assetId, eventId, carryingValue = carrying, proceeds, loss, gain, journalId = journal.Id });
        assets.Save();
        return (new { @event = ProjEvent(FindEvent(eventId)), asset = Proj(asset) }, null, null);
    }

    // shared approval skeleton: record → pending event → maker-checker → permission (PO ordering)
    private (FixedAsset? asset, AssetEvent? evt, string? code, string? message) LoadPending(
        CurrentUser approver, string assetId, string eventId, string eventType, string permission)
    {
        var asset = assets.FindAsset(assetId);
        if (asset is null) return (null, null, "ASSET_NOT_FOUND", "Fixed asset not found.");
        var evt = assets.FindEvent(eventId);
        if (evt is null || evt.AssetId != assetId || evt.EventType != eventType)
            return (null, null, "ASSET_EVENT_NOT_FOUND", "Asset event not found.");
        if (evt.JournalId != "")
            return (null, null, "ASSET_STATE_ERROR", $"Event {eventId} was already approved.");
        if (evt.ActorId == approver.Id)
            return (null, null, "APPROVAL_MAKER_CHECKER", "Maker-checker: the proposer cannot approve their own asset event.");
        if (!approver.Has(permission))
            return (null, null, "FORBIDDEN", $"{permission} permission required.");
        return (asset, evt, null, null);
    }

    private Journal PostJournal(CurrentUser actor, string memo, JournalLine[] lines)
    {
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted, memo,
            lines, DateTimeOffset.UtcNow, actor.Id);
        journals.Add(journal);
        return journal;
    }

    private AssetEvent NewEvent(CurrentUser user, string assetId, string eventType, decimal amount) =>
        new(numbers.Next("FAE"), ec.TenantId, ec.EntityId, assetId, eventType, amount, "", user.Id, DateTimeOffset.UtcNow);

    private AssetEvent? FindEvent(string eventId) => assets.FindEvent(eventId);

    private static object Proj(FixedAsset a) => new
    {
        a.Id, a.Tag, a.Serial, a.Name, a.Category, a.Cost, a.AccumulatedDepreciation,
        bookValue = a.Cost - a.AccumulatedDepreciation, a.LocationId, a.Status,
        a.SourceProjectId, a.InServiceDate, a.CreatedBy, a.CreatedAt,
    };

    private static object ProjEvent(AssetEvent? e) => e is null ? null! : new
    {
        e.Id, e.AssetId, eventType = e.EventType, e.Amount, e.JournalId, state = EventState(e),
        e.FromLocationId, e.ToLocationId, actorId = e.ActorId, e.At,
    };

    // proposal events sit Pending until their approval stamps the journal id; transfer/depreciate
    // events are immediate (their journal, when one exists, is stamped at creation)
    private static string EventState(AssetEvent e) =>
        e.EventType is "capitalize" or "impair" or "dispose" && e.JournalId == "" ? "Pending" : "Posted";
}
