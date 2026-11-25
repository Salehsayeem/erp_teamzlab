using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Production-order lifecycle + shop-floor reporting (ERP-MFG-007/008/010/012/013/041/042/043/067/069,
// rulings R-MFG1..11). Postings (issue, return, completion, scrap disposal, close residual) guard the
// fiscal period FIRST, then commit journal + stock movement (InventoryRepository.ApplyStock/DrawStock —
// never direct stock_levels writes) + order state + audit + outbox in ONE SaveChanges.
//
// Costing (R-MFG5): BOM-line UnitCost is entered tenant data; the planned unit cost of output is
// Σ(QtyPer × UnitCost) ÷ OutputQtyPer of the RELEASED revision; completion value is rounded once at
// the total level so a fully reconciled order closes with an exact zero WIP remainder.
//
// Concurrency: production_orders.status is an EF concurrency token (T7 review 1a) and every write
// path serializes on the order row (SELECT … FOR UPDATE) so the five-value ledger cannot lose an
// update; a stale retry throws DbUpdateConcurrencyException → 409 MFG_CONCURRENCY_ERROR with
// NOTHING written. Issue/return/completion/scrap emit `mfg.issue.posted` (the brief's event list
// covers issue/return under one name) with `kind` in the payload.
public sealed class ProductionOrderService(
    IManufacturingRepository mfg, IInventoryRepository inventory, IJournalRepository journals,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : IProductionOrderService
{
    private const string InventoryAccount = "1400";
    private const string WipAccount = "1420";
    private const string ScrapAccount = "5200";
    private const string WipAccountName = "Work in Process Inventory";
    private const string InventoryAccountName = "Inventory";
    private const string ScrapAccountName = "Production Scrap and Yield Loss";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public object List(ProductionOrderStatus? status, string? outputSku) =>
        mfg.AllOrders(status, outputSku).Select(ProjOrder).ToList();

    // ---- 007: create + guarded transitions ---------------------------------------------

    public (object? data, string? code, string? message) Create(CurrentUser user, Dtos.CreateProductionOrderRequest request)
    {
        if (Guards.Check(request.OutputSku, "OutputSku") is { } skuError) return (null, "VALIDATION_ERROR", skuError);
        if (Guards.CheckPositive(request.DemandedQty, "DemandedQty") is { } qtyError) return (null, "VALIDATION_ERROR", qtyError);
        if (Guards.Check(request.Unit, "Unit") is { } unitError) return (null, "VALIDATION_ERROR", unitError);
        if (request.OverTolerancePct < 0) return (null, "VALIDATION_ERROR", "OverTolerancePct must be >= 0.");

        // 022 AC1: the effective revision is resolved at create and frozen as the planning basis
        var (revision, code, message) = ResolveEffective(request.OutputSku);
        if (code is not null) return (null, code, message);

        var orderId = numbers.Next("MFO");
        // MF1 (024 AC3): the effective approved routing is resolved at create as the planning
        // basis — OPTIONAL: orders without routings keep working (scheduling needs one, not release)
        var plannedRoutingId = ResolveEffectiveRouting(request.OutputSku);
        var order = new ProductionOrder(orderId, ec.TenantId, ec.EntityId, orderId, request.OutputSku,
            request.Unit, request.DemandedQty, request.DemandedQty, // PlannedQty defaults to DemandedQty (008)
            0, 0, ProductionOrderStatus.Draft, request.OverTolerancePct, revision!.Id, null,
            null, null, null, null, null, user.Id, DateTimeOffset.UtcNow, plannedRoutingId);
        mfg.AddOrder(order);
        audit.Log(user, "create", "production_order", order.Id,
            $"Production order {order.OrderNo} drafted for {request.OutputSku}: demanded {request.DemandedQty} {request.Unit}, " +
            $"planned {order.PlannedQty}, basis revision {revision.Revision} ({revision.Id}), tolerance {request.OverTolerancePct}%");
        mfg.Save();
        return (ProjOrder(order), null, null);
    }

    public (object? data, string? code, string? message) Transition(CurrentUser user, string orderId, Dtos.TransitionOrderRequest request)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");

        if (request.NewPlannedQty is not null)
            return AmendPlannedQty(user, order, request);
        return MoveStatus(user, order, request.Status, request.Reason);
    }

    // 008 AC3: pre-release edits mutate PlannedQty in place; post-release edits are versioned
    // change rows that carry who/when/why — the two paths never combine with a status move
    private (object? data, string? code, string? message) AmendPlannedQty(
        CurrentUser user, ProductionOrder order, Dtos.TransitionOrderRequest request)
    {
        if (request.Status != order.Status)
            return (null, "VALIDATION_ERROR",
                "Quantity amendments cannot change status in one call — echo the current status and move the order separately.");
        var newQty = request.NewPlannedQty ?? 0m;
        if (newQty <= 0)
            return (null, "VALIDATION_ERROR", "NewPlannedQty must be greater than 0.");
        if (newQty == order.PlannedQty)
            return (null, "VALIDATION_ERROR", "NewPlannedQty equals the current planned quantity.");

        var postRelease = order.Status is ProductionOrderStatus.Released
            or ProductionOrderStatus.InProcess or ProductionOrderStatus.Completed;
        if (postRelease && string.IsNullOrWhiteSpace(request.Reason))
            return (null, "VALIDATION_ERROR", "An amendment reason is required after release (008 AC3).");

        if (!postRelease)
        {
            mfg.SetOrderPlannedQty(order, newQty);
            audit.Log(user, "amend", "production_order", order.Id,
                $"PlannedQty {order.PlannedQty} → {newQty} before release (no amendment row)");
            if (SaveOrder(order, "AMENDMENT_CONFLICT") is { } preErr) return preErr;
            return (new { order = ProjOrder(order), amendment = (object?)null }, null, null);
        }

        var amendment = new OrderAmendment($"{order.Id}:v{mfg.AmendmentsFor(order.Id).Count + 1}",
            ec.TenantId, ec.EntityId, order.Id, "PlannedQty", order.PlannedQty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            newQty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Reason!, user.Id, DateTimeOffset.UtcNow);
        mfg.AddAmendment(amendment);
        mfg.SetOrderPlannedQty(order, newQty);
        audit.Log(user, "amend", "production_order", order.Id,
            $"PlannedQty amended {amendment.OldValue} → {amendment.NewValue}: {request.Reason} (008 AC3)");
        if (SaveOrder(order, "AMENDMENT_CONFLICT") is { } postErr) return postErr;
        return (new { order = ProjOrder(order), amendment }, null, null);
    }

    // 007 AC2: the guarded map — skips are illegal; release and close live behind their own gates
    private (object? data, string? code, string? message) MoveStatus(
        CurrentUser user, ProductionOrder order, ProductionOrderStatus target, string? reason)
    {
        var legal = order.Status switch
        {
            ProductionOrderStatus.Draft => new[] { ProductionOrderStatus.Planned, ProductionOrderStatus.Cancelled },
            ProductionOrderStatus.Planned => new[] { ProductionOrderStatus.Cancelled }, // release goes through /releases
            ProductionOrderStatus.Released => new[] { ProductionOrderStatus.InProcess },
            ProductionOrderStatus.InProcess => new[] { ProductionOrderStatus.Completed },
            _ => Array.Empty<ProductionOrderStatus>(), // Completed/Closed/Cancelled: gate endpoints only
        };
        if (!legal.Contains(target))
            return (null, "MFG_TRANSITION_ERROR",
                $"Illegal transition {order.Status} → {target} (007). Allowed from {order.Status}: " +
                (legal.Length == 0 ? "nothing — the order is terminal here" : string.Join(", ", legal)) + ".");

        if (target == ProductionOrderStatus.Cancelled && string.IsNullOrWhiteSpace(reason))
            return (null, "VALIDATION_ERROR", "A cancellation reason is required (007).");

        // fix round 1 (F2.1): InProcess → Completed is gated on consumption reconciliation — an
        // order may not BECOME unclosable. Zero quantity mismatch AND zero credit-WIP remainder,
        // else 409 listing the mismatches; a debit WIP remainder may still pass (it closes later
        // as the acknowledged residual). Shortfall is deliberately not gated here: Completed
        // orders keep reporting open (ReportingScope) so it stays repairable before close.
        if (target == ProductionOrderStatus.Completed && order.Status == ProductionOrderStatus.InProcess)
        {
            var (gateItems, _, _, gateWipRemainder, _, _) = Reconcile(order);
            var mismatches = gateItems.Where(i => i.Code.StartsWith("consumption:", StringComparison.Ordinal)).ToList();
            if (mismatches.Count > 0 || gateWipRemainder < 0)
            {
                var detail = mismatches.Count > 0
                    ? "unreconciled consumption: " + string.Join("; ", mismatches.Select(m => m.Code))
                    : "";
                if (gateWipRemainder < 0)
                    detail += (detail.Length > 0 ? "; " : "") +
                        $"credit WIP remainder {gateWipRemainder:0.00} (R-MFG6 — credits exceed debits)";
                return (gateItems.Select(i => (object)new { code = i.Code, detail = i.Detail, qty = i.Qty }).ToArray(),
                    "MFG_CLOSE_BLOCKED",
                    "Completion gate blocked — " + detail + ".");
            }
        }

        if (target == ProductionOrderStatus.Cancelled) mfg.SetOrderCancelled(order, reason!);
        else mfg.SetOrderStatus(order, target);
        audit.Log(user, "transition", "production_order", order.Id, $"Order {order.OrderNo}: {order.Status} → {target}");
        if (SaveOrder(order) is { } err) return err;
        return (ProjOrder(order), null, null);
    }

    // ---- 010/022: the release gate ------------------------------------------------------

    public (object? data, string? code, string? message) Release(CurrentUser releaser, string orderId, string? reason)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (order.CreatedBy == releaser.Id)
            return (null, "MFG_RELEASE_ERROR", "Maker-checker: the order creator cannot release their own order (010 AC3).");

        // readiness gate: every failing prerequisite is listed machine-readable (010 AC1)
        var prerequisites = new List<(string Code, string Detail)>();
        if (order.Status != ProductionOrderStatus.Planned)
            prerequisites.Add(("MFG_STATE_ERROR", $"order must be Planned to release (current: {order.Status})"));

        BomRevision? revision = null;
        List<BomLine> lines = [];
        var (resolved, resolveCode, resolveMessage) = ResolveEffective(order.OutputSku);
        if (resolveCode is not null)
        {
            prerequisites.Add((resolveCode, resolveMessage!));
        }
        else
        {
            revision = resolved;
            lines = mfg.LinesForRevision(resolved!.Id);
            if (lines.Count == 0)
                prerequisites.Add(("BOM_NO_LINES",
                    $"revision {resolved.Revision} of {order.OutputSku} has no component lines"));
        }

        // MF2 (028 AC3): when the entity policy requires finite scheduling, the order must carry
        // a PUBLISHED finite schedule run — an infinite-mode published plan is rejected here
        var routingId = order.PlannedRoutingRevisionId;
        var releasedRoutingId = ResolveEffectiveRouting(order.OutputSku) ?? routingId;

        // MF6 (050 AC1): subcontract operations must carry valid supplier/service config at
        // release — a blocked supplier blocks release without an exception path
        foreach (var op in db.RoutingOperations
                     .Where(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId
                         && o.RoutingRevisionId == releasedRoutingId && o.Kind == OperationKind.Subcontract)
                     .ToList())
        {
            if (string.IsNullOrWhiteSpace(op.ServiceDescription))
                prerequisites.Add(("SUBCONTRACT_SERVICE_MISSING", $"subcontract operation {op.OperationCode} (seq {op.Seq}) has no service description"));
            var supplier = db.Suppliers.FirstOrDefault(s => s.Id == op.SupplierId
                && s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
            if (supplier is null)
                prerequisites.Add(("SUBCONTRACT_SUPPLIER_MISSING", $"subcontract operation {op.OperationCode} references unknown supplier {op.SupplierId}"));
            else if (supplier.Status != SupplierLifecycleStatus.Active)
                prerequisites.Add(("SUBCONTRACT_SUPPLIER_BLOCKED",
                    $"subcontract operation {op.OperationCode}: supplier {supplier.Name} is {supplier.Status} — release blocked (050)"));
        }
        if (FiniteScheduleRequired())
        {
            var publishedRun = (from pub in db.SchedulePublications
                    join run in db.ScheduleRuns on pub.ScheduleRunId equals run.Id
                    where pub.TenantId == ec.TenantId && pub.LegalEntityId == ec.EntityId
                        && run.TenantId == ec.TenantId && run.LegalEntityId == ec.EntityId
                        && run.OrderId == order.Id
                    orderby pub.At descending
                    select run).FirstOrDefault();
            if (publishedRun is null || publishedRun.Mode != ScheduleRunMode.Finite)
                prerequisites.Add(("SCHEDULE_NOT_FINITE",
                    publishedRun is null
                        ? "the entity policy requires a published finite schedule before release"
                        : $"the published schedule run {publishedRun.Id} is infinite mode; the policy requires finite (028)"));
        }

        if (prerequisites.Count > 0)
            return (prerequisites.Select(p => (object)new { code = p.Code, detail = p.Detail }).ToArray(),
                "MFG_RELEASE_BLOCKED",
                "Release gate failed: " + string.Join("; ", prerequisites.Select(p => p.Code)));

        // 010 AC2/022 AC2: passing release freezes the revision resolved NOW — a change since
        // create is shown as an explicit planned-vs-released delta
        var revisionChanged = revision!.Id != order.PlannedBomRevisionId;
        var plannedRevision = order.PlannedBomRevisionId is { } pid ? mfg.FindBom(pid) : null;

        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        LockOrderRow(order.Id);
        mfg.SetOrderReleased(order, revision.Id, order.PlannedQty, releasedRoutingId);
        audit.Log(releaser, "release", "production_order", order.Id,
            $"Order {order.OrderNo} released: {order.PlannedQty} {order.Unit} of {order.OutputSku}" +
            (revisionChanged ? $" — revision moved {plannedRevision?.Revision} → {revision.Revision} (022)" : "") +
            (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}"));
        audit.Emit(releaser, "mfg.order.released", new
        {
            orderId = order.Id, orderNo = order.OrderNo, outputSku = order.OutputSku,
            releasedQty = order.ReleasedQty, releasedBomRevisionId = revision.Id,
            plannedBomRevisionId = order.PlannedBomRevisionId, revisionChanged, releasedBy = releaser.Id,
        });
        if (SaveOrder(order) is { } err) return err;
        if (ownsTx) tx!.Commit();

        return (new
        {
            order = ProjOrder(order),
            release = new
            {
                plannedBomRevisionId = order.PlannedBomRevisionId,
                releasedBomRevisionId = revision.Id,
                plannedRoutingRevisionId = order.PlannedRoutingRevisionId,
                releasedRoutingRevisionId = order.ReleasedRoutingRevisionId,
                revisionChanged,
            },
        }, null, null);
    }

    // ---- 042: material issues + returns (real stock, real journals) ----------------------

    public (object? data, string? code, string? message) IssueMaterial(CurrentUser user, string orderId, Dtos.MaterialIssueRequest request)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (Guards.Check(request.ComponentSku, "ComponentSku") is { } skuError) return (null, "VALIDATION_ERROR", skuError);
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null, "VALIDATION_ERROR", qtyError);
        var (revision, lines, code, message) = ReportingScope(order);
        if (code is not null) return (null, code, message);

        var line = lines.FirstOrDefault(l => l.ComponentSku == request.ComponentSku);
        if (line is null)
            return (null, "MFG_COMPONENT_ERROR",
                $"{request.ComponentSku} is not a component of the released revision {revision!.Revision} of {order.OutputSku}; there is no cost basis for the issue.");

        var periodError = GuardPeriod();
        if (periodError is not null) return (null, periodError.Value.Code, periodError.Value.Message);

        var value = decimal.Round(request.Qty * line.UnitCost, 2, MidpointRounding.AwayFromZero);

        // the stock check and the drain serialize on the order row so two concurrent issues
        // cannot both pass against the same on-hand qty
        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        LockOrderRow(order.Id);
        // fix round 1 (F2.2): availability is read INSIDE the lock from committed state — a
        // concurrent drain that committed before the lock is seen here, not silently raced past
        var onHand = inventory.QtyOf(request.ComponentSku);
        if (onHand < request.Qty)
            return (null, "INSUFFICIENT_STOCK",
                $"Insufficient stock for {request.ComponentSku}: {onHand} on hand, {request.Qty} requested (042 AC1).");
        // fix round 1 (F2.2): availability is re-read INSIDE the lock — the pre-lock check above
        // only short-circuits; the committed on-hand under the lock is the one that gates the drain
        onHand = inventory.QtyOf(request.ComponentSku);
        if (onHand < request.Qty)
            return (null, "INSUFFICIENT_STOCK",
                $"Insufficient stock for {request.ComponentSku}: {onHand} on hand, {request.Qty} requested (042 AC1).");

        var issueId = numbers.Next("ISS");
        var journal = NewJournal(numbers.Next("J"), user,
            $"Material issue {issueId} on {orderId} ({request.ComponentSku} x{request.Qty} @ {line.UnitCost:0.00})",
            [new JournalLine(WipAccount, WipAccountName, value, 0), new JournalLine(InventoryAccount, InventoryAccountName, 0, value)]);

        var issue = new MaterialIssue(issueId, ec.TenantId, ec.EntityId, orderId, request.ComponentSku,
            MovementKind.Issue, request.Qty, line.UnitCost, value, null, journal.Id, user.Id, DateTimeOffset.UtcNow);
        mfg.AddIssue(issue);
        journals.Add(journal);
        inventory.DrawStock(request.ComponentSku, request.Qty); // deterministic multi-location drain (task-2 §4)

        audit.Log(user, "issue", "material_issue", issue.Id,
            $"Issued {request.Qty} x {request.ComponentSku} @ {line.UnitCost:0.00} = {value:0.00} to {order.OrderNo}; journal {journal.Id}");
        EmitIssue(user, order, issue, "issue");
        if (SaveOrder(order) is { } issueErr) return issueErr;
        if (ownsTx) tx!.Commit();
        return (new { issue = ProjIssue(issue), journal }, null, null);
    }

    public (object? data, string? code, string? message) ReturnIssue(CurrentUser user, string orderId, Dtos.IssueReturnRequest request)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (Guards.Check(request.IssueId, "IssueId") is { } issueError) return (null, "VALIDATION_ERROR", issueError);
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null, "VALIDATION_ERROR", qtyError);
        var (_, _, code, message) = ReportingScope(order);
        if (code is not null) return (null, code, message);

        var source = mfg.FindIssue(orderId, request.IssueId);
        if (source is null || source.Kind != MovementKind.Issue)
            return (null, "ISSUE_NOT_FOUND", $"Issue {request.IssueId} was not found on order {orderId}.");

        var periodError = GuardPeriod();
        if (periodError is not null) return (null, periodError.Value.Code, periodError.Value.Message);

        var value = decimal.Round(request.Qty * source.UnitCost, 2, MidpointRounding.AwayFromZero);

        // the returnable re-check and the write serialize on the order row (042 AC3 under races);
        // fix round 1 (F2.2): the bound itself is computed INSIDE the lock from committed returns —
        // a concurrent return can no longer slip past the pre-flight check
        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        LockOrderRow(order.Id);
        var returnable = source.Qty - mfg.ReturnedQtyFor(source.Id);
        if (request.Qty > returnable)
            return (null, "ISSUE_RETURN_OVER",
                $"Return of {request.Qty} exceeds the {returnable} still returnable on issue {source.Id} (issued {source.Qty}, already returned {source.Qty - returnable}) (042 AC3).");

        var returnId = numbers.Next("ISS");
        var journal = NewJournal(numbers.Next("J"), user,
            $"Material return {returnId} of issue {source.Id} on {orderId} ({source.ComponentSku} x{request.Qty})",
            [new JournalLine(InventoryAccount, InventoryAccountName, value, 0), new JournalLine(WipAccount, WipAccountName, 0, value)]);

        var issue = new MaterialIssue(returnId, ec.TenantId, ec.EntityId, orderId, source.ComponentSku,
            MovementKind.Return, request.Qty, source.UnitCost, value, source.Id, journal.Id, user.Id, DateTimeOffset.UtcNow);
        mfg.AddIssue(issue);
        journals.Add(journal);
        var loc = inventory.DefaultReceivingLocation(); // returns land back at the receiving dock
        inventory.ApplyStock(loc.WarehouseId, loc.Id, source.ComponentSku, request.Qty);

        audit.Log(user, "return", "material_issue", issue.Id,
            $"Returned {request.Qty} x {source.ComponentSku} against issue {source.Id} on {order.OrderNo}; journal {journal.Id}");
        EmitIssue(user, order, issue, "return");
        if (SaveOrder(order) is { } returnErr) return returnErr;
        if (ownsTx) tx!.Commit();
        return (new { issue = ProjIssue(issue), journal }, null, null);
    }

    // ---- 041 trim + 043: order-level completion with backflush ---------------------------

    public (object? data, string? code, string? message) ReportCompletion(CurrentUser user, string orderId, Dtos.CompletionRequest request)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null, "VALIDATION_ERROR", qtyError);
        var (revision, lines, code, message) = ReportingScope(order);
        if (code is not null) return (null, code, message);

        // MF6 (052 AC3): completion cannot exceed the ACCEPTED returned quantity when the order
        // carries subcontract operations — quality-gated receipts only count once accepted
        var completionRoutingId = order.ReleasedRoutingRevisionId ?? order.PlannedRoutingRevisionId;
        var hasSubcontractOps = completionRoutingId is not null && db.RoutingOperations.Any(o =>
            o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId
            && o.RoutingRevisionId == completionRoutingId && o.Kind == OperationKind.Subcontract);
        if (hasSubcontractOps)
        {
            var acceptedTotal = db.SubcontractReceipts
                .Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.OrderId == order.Id)
                .Sum(r => r.AcceptedQty);
            if (order.CompletedQty + request.Qty > acceptedTotal)
                return (null, "SUBCONTRACT_ACCEPTANCE_SHORT",
                    $"Completion of {request.Qty} would take completed to {order.CompletedQty + request.Qty} but only " +
                    $"{acceptedTotal} units were accepted back from the subcontractor (052 AC3).");
        }

        var periodError = GuardPeriod();
        if (periodError is not null) return (null, periodError.Value.Code, periodError.Value.Message);

        var plannedUnitCost = PlannedUnitCost(lines, revision!.OutputQtyPer);
        var value = CompletionValue(request.Qty, lines, revision.OutputQtyPer);
        var backflush = lines.Where(l => l.IssuePolicy == IssuePolicy.Backflush)
            .Select(l => (Line: l, Qty: request.Qty * l.QtyPer / revision.OutputQtyPer)).ToList();

        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        LockOrderRow(order.Id);
        // fix round 1 (F2.2): the order row is locked FIRST, then re-read — the ceiling,
        // CompletedQty accumulation and stock availability below all run against committed
        // state inside the lock, so concurrent completions can no longer lose ledger updates
        db.Entry(order).Reload();

        // 008 AC2: over-tolerance completion needs mfg:override (re-checked under the lock)
        var ceiling = order.PlannedQty * (1 + order.OverTolerancePct / 100m);
        if (order.CompletedQty + request.Qty > ceiling && !user.Has("mfg:override"))
            return (null, "MFG_OVER_TOLERANCE",
                $"Completion of {request.Qty} would take completed to {order.CompletedQty + request.Qty}, over the " +
                $"{ceiling} ceiling (planned {order.PlannedQty} + {order.OverTolerancePct}% tolerance); mfg:override required.");

        foreach (var (line, qty) in backflush)
            if (inventory.QtyOf(line.ComponentSku) < qty)
                return (null, "INSUFFICIENT_STOCK",
                    $"Backflush cannot consume {qty} x {line.ComponentSku}: {inventory.QtyOf(line.ComponentSku)} on hand (043).");

        var completionId = numbers.Next("CPL");
        // fix round 1 (F1.2): backflush line VALUES are allocated off the completion credit basis —
        // per-line proportional, the last line as plug — so Σ debit equals the credit exactly and a
        // fully reconciled pure-backflush order closes at zero instead of stranding a rounded cent
        var backflushValues = AllocateBackflushValues(value, backflush, lines);
        var backflushTotal = backflushValues.Sum();
        if (backflush.Count > 0)
        {
            var bfJournal = NewJournal(numbers.Next("J"), user,
                $"Backflush consumption for completion {completionId} on {orderId}",
                [new JournalLine(WipAccount, WipAccountName, backflushTotal, 0), new JournalLine(InventoryAccount, InventoryAccountName, 0, backflushTotal)]);
            journals.Add(bfJournal);
            for (var i = 0; i < backflush.Count; i++)
            {
                var (line, qty) = backflush[i];
                mfg.AddIssue(new MaterialIssue(numbers.Next("ISS"), ec.TenantId, ec.EntityId, orderId, line.ComponentSku,
                    MovementKind.Issue, qty, line.UnitCost, backflushValues[i], null, bfJournal.Id, user.Id, DateTimeOffset.UtcNow, completionId));
                inventory.DrawStock(line.ComponentSku, qty);
            }
        }

        var completionJournal = NewJournal(numbers.Next("J"), user,
            $"Completion {completionId} on {orderId} ({request.Qty} x {order.OutputSku} @ {plannedUnitCost:0.00})",
            [new JournalLine(InventoryAccount, InventoryAccountName, value, 0), new JournalLine(WipAccount, WipAccountName, 0, value)]);
        journals.Add(completionJournal);
        var completion = new CompletionReport(completionId, ec.TenantId, ec.EntityId, orderId,
            request.Qty, request.Note, completionJournal.Id, user.Id, DateTimeOffset.UtcNow);
        mfg.AddCompletion(completion);
        mfg.SetOrderCompletedQty(order, order.CompletedQty + request.Qty);

        // fix round 1 (F1.1): the completion RECEIPTS the output sku — the GL said inventory grew,
        // /stock must agree; the receipt commits with the same SaveChanges as the journal
        var dock = inventory.DefaultReceivingLocation();
        inventory.ApplyStock(dock.WarehouseId, dock.Id, order.OutputSku, request.Qty);

        audit.Log(user, "complete", "completion_report", completion.Id,
            $"Completed {request.Qty} x {order.OutputSku} on {order.OrderNo} @ {plannedUnitCost:0.00} = {value:0.00}; journal {completionJournal.Id}" +
            (backflush.Count > 0 ? $"; backflush {backflush.Count} line(s) = {backflushTotal:0.00}" : ""));
        audit.Emit(user, "mfg.completion.posted", new
        {
            orderId, completionId, orderNo = order.OrderNo, outputSku = order.OutputSku, qty = request.Qty,
            plannedUnitCost, value, journalId = completionJournal.Id,
            backflushBomRevisionId = revision.Id,
            backflush = backflush.Select((b, i) => new
            {
                componentSku = b.Line.ComponentSku, qty = b.Qty, unitCost = b.Line.UnitCost,
                lineValue = backflushValues[i],
            }),
            completedQty = order.CompletedQty, reportedBy = user.Id,
        });
        if (SaveOrder(order) is { } err) return err;
        if (ownsTx) tx!.Commit();

        return (new
        {
            completion,
            journal = completionJournal,
            plannedUnitCost,
            backflush = backflush.Select((b, i) => new
            {
                componentSku = b.Line.ComponentSku, qty = b.Qty, unitCost = b.Line.UnitCost,
                lineValue = backflushValues[i],
            }),
        }, null, null);
    }

    // ---- 067: scrap -----------------------------------------------------------------------

    public (object? data, string? code, string? message) ReportScrap(CurrentUser user, string orderId, Dtos.ScrapRequest request)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (Guards.Check(request.ItemSku, "ItemSku") is { } skuError) return (null, "VALIDATION_ERROR", skuError);
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null, "VALIDATION_ERROR", qtyError);
        if (Guards.Check(request.ReasonCode, "ReasonCode") is { } reasonError) return (null, "VALIDATION_ERROR", reasonError);
        var (revision, lines, code, message) = ReportingScope(order);
        if (code is not null) return (null, code, message);

        // cost basis: the component's BOM line, else the output's planned unit cost — nothing else
        // carries a cost in this slice (R-MFG5)
        var line = lines.FirstOrDefault(l => l.ComponentSku == request.ItemSku);
        var unitCost = line?.UnitCost
            ?? (request.ItemSku == order.OutputSku ? PlannedUnitCost(lines, revision!.OutputQtyPer) : (decimal?)null);
        if (unitCost is null)
            return (null, "MFG_COMPONENT_ERROR",
                $"{request.ItemSku} is neither a component of the released revision nor the output of {order.OrderNo}; there is no cost basis for scrap.");

        Journal? journal = null;
        if (request.Disposition == ScrapDisposition.Disposal) // ReturnToStock posts nothing (067 AC2)
        {
            var periodError = GuardPeriod();
            if (periodError is not null) return (null, periodError.Value.Code, periodError.Value.Message);

            // the disposal check + drain serialize on the order row like every other movement
            var ownsTx = db.Database.CurrentTransaction is null;
            using var tx = ownsTx ? db.Database.BeginTransaction() : null;
            LockOrderRow(order.Id);
            var onHand = inventory.QtyOf(request.ItemSku);
            if (onHand < request.Qty)
                return (null, "INSUFFICIENT_STOCK",
                    $"Cannot dispose {request.Qty} x {request.ItemSku}: {onHand} on hand.");

            var value = decimal.Round(request.Qty * unitCost.Value, 2, MidpointRounding.AwayFromZero);
            // fix round 1 (F4.1): a COMPONENT's disposal flows THROUGH WIP — the scrapped cost
            // leaves the 1420 accumulator at the component's issue cost basis (Dr 5200 / Cr 1420);
            // the OUTPUT's disposal credits 1400 — its cost already left WIP via the completion
            var wipResident = line is not null;
            var creditAccount = wipResident ? WipAccount : InventoryAccount;
            var creditAccountName = wipResident ? WipAccountName : InventoryAccountName;
            var scrapId = numbers.Next("SCR");
            journal = NewJournal(numbers.Next("J"), user,
                $"Scrap {scrapId} on {orderId} ({request.ItemSku} x{request.Qty}, {request.ReasonCode})",
                [new JournalLine(ScrapAccount, ScrapAccountName, value, 0), new JournalLine(creditAccount, creditAccountName, 0, value)]);
            journals.Add(journal);
            inventory.DrawStock(request.ItemSku, request.Qty); // physical disposal leaves inventory
            var scrap = new ScrapReport(scrapId, ec.TenantId, ec.EntityId, orderId, request.ItemSku,
                request.Qty, unitCost.Value, request.ReasonCode, request.Disposition, journal.Id, user.Id, DateTimeOffset.UtcNow);
            mfg.AddScrap(scrap);
            audit.Log(user, "scrap", "scrap_report", scrap.Id,
                $"Scrap {request.Qty} x {request.ItemSku} ({request.ReasonCode}, disposal) on {order.OrderNo} = {value:0.00}; journal {journal.Id}");
            audit.Emit(user, "mfg.scrap.posted", new
            {
                orderId, scrapId, orderNo = order.OrderNo, itemSku = request.ItemSku, qty = request.Qty,
                unitCost = unitCost.Value, value, disposition = request.Disposition.ToString(),
                reasonCode = request.ReasonCode, journalId = journal.Id, reportedBy = user.Id,
            });
            if (SaveOrder(order) is { } saveErr) return saveErr;
            if (ownsTx) tx!.Commit();
            return (new { scrap, journal }, null, null);
        }

        var scrId = numbers.Next("SCR");
        var recorded = new ScrapReport(scrId, ec.TenantId, ec.EntityId, orderId, request.ItemSku,
            request.Qty, unitCost.Value, request.ReasonCode, request.Disposition, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddScrap(recorded);
        audit.Log(user, "scrap", "scrap_report", recorded.Id,
            $"Scrap {request.Qty} x {request.ItemSku} ({request.ReasonCode}, return-to-stock — no movement, no journal) on {order.OrderNo}");
        audit.Emit(user, "mfg.scrap.posted", new
        {
            orderId, scrapId = recorded.Id, orderNo = order.OrderNo, itemSku = request.ItemSku, qty = request.Qty,
            unitCost = unitCost.Value, value = 0m, disposition = request.Disposition.ToString(),
            reasonCode = request.ReasonCode, journalId = (string?)null, reportedBy = user.Id,
        });
        if (SaveOrder(order) is { } err) return err;
        return (new { scrap = recorded, journal = (Journal?)null }, null, null);
    }

    // ---- 013: close-check / close / reopen ------------------------------------------------

    public (object? data, string? code, string? message) CloseCheck(string orderId)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        var (items, wipDebits, wipCredits, wipRemainder, scrapRows, _) = Reconcile(order);
        return (new
        {
            orderId = order.Id, orderNo = order.OrderNo, status = order.Status.ToString(),
            open = items.Count > 0,
            items = items.Select(i => (object)new { code = i.Code, detail = i.Detail, qty = i.Qty }),
            wipDebits, wipCredits, wipRemainder, scrap = scrapRows,
        }, null, null);
    }

    public (object? data, string? code, string? message) Close(CurrentUser closer, string orderId, string? residualAcknowledgment)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (order.CreatedBy == closer.Id)
            return (null, "MFG_CLOSE_ERROR", "Maker-checker: the order creator cannot close their own order (013).");
        if (order.Status != ProductionOrderStatus.Completed)
            return (null, "MFG_STATE_ERROR",
                $"Only Completed orders can be closed (current: {order.Status}); walk the lifecycle first (007).");

        var (items, wipDebits, wipCredits, wipRemainder, _, wipItem) = Reconcile(order);

        // 013 AC1: quantity reconciliation is a hard gate — every unreconciled quantity is named
        var quantityItems = items.Where(i => i.Code != "WIP_REMAINDER").ToList();
        if (quantityItems.Count > 0)
            return (items.Select(i => (object)new { code = i.Code, detail = i.Detail, qty = i.Qty }).ToArray(),
                "MFG_CLOSE_BLOCKED",
                "Close blocked — unreconciled quantities: " + string.Join("; ", quantityItems.Select(i => i.Code)));

        // R-MFG6: a CREDIT remainder blocks close outright — no automatic negative scrap line
        if (wipRemainder < 0)
            return (null, "MFG_CLOSE_WIP_ERROR",
                $"WIP remainder is a CREDIT of {wipRemainder:0.00} (credits exceed debits — over-receipt or mispricing); " +
                "close is blocked (R-MFG6). Investigate the ledger; there is no automatic negative scrap line.");

        // a debit remainder is the residual line: it closes only when explicitly acknowledged
        if (wipRemainder > 0 && string.IsNullOrWhiteSpace(residualAcknowledgment))
            return (items.Select(i => (object)new { code = i.Code, detail = i.Detail, qty = i.Qty }).ToArray(),
                "MFG_CLOSE_BLOCKED",
                $"Close blocked — WIP_REMAINDER debit of {wipRemainder:0.00} must be acknowledged as the residual line " +
                "(it posts Dr 5200 / Cr 1420 on close).");

        var periodError = GuardPeriod();
        if (periodError is not null) return (null, periodError.Value.Code, periodError.Value.Message);

        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        LockOrderRow(order.Id);

        Journal? residual = null;
        if (wipRemainder > 0)
        {
            var ack = residualAcknowledgment!.Trim();
            residual = NewJournal(numbers.Next("J"), closer,
                $"Close residual for {orderId} — acknowledged: {(ack.Length > 80 ? ack[..80] : ack)}",
                [new JournalLine(ScrapAccount, ScrapAccountName, wipRemainder, 0), new JournalLine(WipAccount, WipAccountName, 0, wipRemainder)]);
            journals.Add(residual);
        }

        mfg.SetOrderClosed(order, closer.Id, DateTimeOffset.UtcNow);
        audit.Log(closer, "close", "production_order", order.Id,
            $"Order {order.OrderNo} closed: output {order.CompletedQty}/{order.PlannedQty}, WIP debits {wipDebits:0.00}, " +
            $"credits {wipCredits:0.00}" + (residual is null ? ", no residual" : $", residual {wipRemainder:0.00} posted"));
        EmitOrderClosed(closer, order, wipDebits, wipCredits, wipRemainder, residual?.Id);
        if (SaveOrder(order) is { } err) return err;
        if (ownsTx) tx!.Commit();

        return (new { order = ProjOrder(order), residualJournal = residual }, null, null);
    }

    // 013 AC3: reopen is the only route back; the prior close is preserved on the row and cited
    public (object? data, string? code, string? message) Reopen(CurrentUser user, string orderId, string reason)
    {
        var order = mfg.FindOrder(orderId);
        if (order is null) return (null, "MFG_ORDER_NOT_FOUND", "Production order not found.");
        if (string.IsNullOrWhiteSpace(reason))
            return (null, "VALIDATION_ERROR", "A reopen reason is required (013 AC3).");
        if (order.Status != ProductionOrderStatus.Closed)
            return (null, "MFG_STATE_ERROR", $"Only Closed orders can be reopened (current: {order.Status}).");

        mfg.SetOrderReopened(order, user.Id, DateTimeOffset.UtcNow);
        audit.Log(user, "reopen", "production_order", order.Id,
            $"Order {order.OrderNo} reopened: {reason}. Prior close by {order.ClosedBy} at {order.ClosedAt:u} is preserved (013 AC3); " +
            "reporting freeze lifted.");
        if (SaveOrder(order) is { } err) return err;
        return (new { order = ProjOrder(order) }, null, null);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private (string? Code, string? Message)? GuardPeriod()
    {
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, Today);
        return guardCode is null ? null : (guardCode, guardMsg);
    }

    // routine reporting scope: orders report while Released/InProcess against the FROZEN released
    // revision; fix round 1 (F2.1) adds Completed — reconciliation reporting stays open so a
    // mismatch surfaced at the Completed gate is always repairable; a closed order is frozen
    // outright (013 AC2)
    private (BomRevision? Revision, List<BomLine> Lines, string? Code, string? Message) ReportingScope(ProductionOrder order)
    {
        if (order.Status == ProductionOrderStatus.Closed)
            return (null, new List<BomLine>(), "ORDER_CLOSED",
                $"Order {order.OrderNo} is closed; routine reporting is frozen (013 AC2) — reopen first.");
        if (order.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProcess or ProductionOrderStatus.Completed))
            return (null, new List<BomLine>(), "MFG_STATE_ERROR",
                $"Order {order.OrderNo} must be Released, InProcess or Completed for reporting (current: {order.Status}).");
        if (order.ReleasedBomRevisionId is not { } revisionId)
            return (null, new List<BomLine>(), "MFG_STATE_ERROR", $"Order {order.OrderNo} has no frozen BOM revision.");
        var revision = mfg.FindBom(revisionId);
        if (revision is null)
            return (null, new List<BomLine>(), "MFG_STATE_ERROR", $"The frozen BOM revision {revisionId} no longer exists.");
        return (revision, mfg.LinesForRevision(revisionId), null, null);
    }

    // 022/014: exactly one live approved revision per sku per date — zero is not effective,
    // more than one means the overlap guard was bypassed
    private (BomRevision? revision, string? code, string? message) ResolveEffective(string outputSku)
    {
        var matches = mfg.EffectiveRevisionsFor(outputSku, Today);
        return matches.Count switch
        {
            0 => (null, "BOM_NOT_EFFECTIVE",
                $"No approved BOM revision of {outputSku} is effective today; planning and release are blocked (014 AC2)."),
            > 1 => (null, "BOM_EFFECTIVE_OVERLAP",
                $"{matches.Count} approved revisions of {outputSku} claim today ({string.Join(", ", matches.Select(r => r.Revision))}) — effective intervals overlap."),
            _ => (matches[0], null, null),
        };
    }

    // MF1 (024): the effective APPROVED routing for a sku today — null when the item has no
    // routing (pilot trim: routings are optional; scheduling refuses orders without one)
    private string? ResolveEffectiveRouting(string outputSku) =>
        db.RoutingRevisions
            .Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId
                && r.OutputSku == outputSku && r.Status == RoutingStatus.Approved
                && r.EffectiveFrom <= Today && (r.EffectiveTo == null || r.EffectiveTo >= Today))
            .OrderBy(r => r.Revision)
            .Select(r => r.Id)
            .FirstOrDefault();

    // MF2 (028): the entity's finite-scheduling policy is tenant data, not a constant
    private bool FiniteScheduleRequired() =>
        db.FinanceSettings
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Key == "mfg.finite_schedule_required")
            .OrderBy(s => s.Id)
            .FirstOrDefault()?.Value == "true";

    // the close reconciliation (013 AC1 + 067 AC3): output, per-component consumption and the WIP
    // ledger are compared one by one; scrap disposal of a component counts as that component's
    // physical consumption; expected consumption is summed per completion so it matches the
    // backflush quantities exactly (no division drift)
    private (List<(string Code, string Detail, decimal Qty)> Items, decimal WipDebits, decimal WipCredits,
        decimal WipRemainder, List<object> ScrapRows, (string Code, string Detail, decimal Qty)? WipItem)
        Reconcile(ProductionOrder order)
    {
        var revision = order.ReleasedBomRevisionId is { } rid ? mfg.FindBom(rid) : null;
        var lines = revision is null ? new List<BomLine>() : mfg.LinesForRevision(revision.Id);
        var issues = mfg.IssuesFor(order.Id);
        var completions = mfg.CompletionsFor(order.Id);
        var scraps = mfg.ScrapsFor(order.Id);

        var items = new List<(string Code, string Detail, decimal Qty)>();
        if (order.CompletedQty < order.PlannedQty)
            items.Add(("OUTPUT_SHORTFALL",
                $"output short by {order.PlannedQty - order.CompletedQty} (planned {order.PlannedQty}, completed {order.CompletedQty})",
                order.PlannedQty - order.CompletedQty));

        foreach (var line in lines)
        {
            var expected = completions.Sum(c => c.Qty * line.QtyPer / revision!.OutputQtyPer);
            var issued = issues.Where(i => i.Kind == MovementKind.Issue && i.ComponentSku == line.ComponentSku).Sum(i => i.Qty);
            var returned = issues.Where(i => i.Kind == MovementKind.Return && i.ComponentSku == line.ComponentSku).Sum(i => i.Qty);
            var disposed = scraps.Where(s => s.Disposition == ScrapDisposition.Disposal && s.ItemSku == line.ComponentSku).Sum(s => s.Qty);
            var actual = issued - returned + disposed;
            if (actual != expected)
                items.Add(($"consumption:{line.ComponentSku}",
                    $"expected {expected} (completed x QtyPer), actual {actual} (issued {issued} − returned {returned} + disposed {disposed})",
                    actual - expected));
        }

        // fix round 1 (F4.1): a component's scrap disposal flows THROUGH WIP — the ledger carries
        // the disposed cost as a WIP debit (at the issue cost basis, mirroring the Dr 5200 /
        // Cr 1420 journal) while the completion still credits the FULL BOM cost, so the scrapped
        // portion reconciles to zero instead of stranding a hard-blocked remainder
        var componentSkus = lines.Select(l => l.ComponentSku).ToHashSet();
        var disposalThroughWip = decimal.Round(scraps
            .Where(s => s.Disposition == ScrapDisposition.Disposal && componentSkus.Contains(s.ItemSku))
            .Sum(s => decimal.Round(s.Qty * s.UnitCost, 2, MidpointRounding.AwayFromZero)), 2, MidpointRounding.AwayFromZero);
        var wipDebits = decimal.Round(issues.Where(i => i.Kind == MovementKind.Issue).Sum(i => i.LineValue) + disposalThroughWip, 2, MidpointRounding.AwayFromZero);
        var wipCredits = decimal.Round(
            issues.Where(i => i.Kind == MovementKind.Return).Sum(i => i.LineValue)
            + (revision is null ? 0m : completions.Sum(c => CompletionValue(c.Qty, lines, revision.OutputQtyPer))),
            2, MidpointRounding.AwayFromZero);
        var wipRemainder = decimal.Round(wipDebits - wipCredits, 2, MidpointRounding.AwayFromZero);
        (string, string, decimal)? wipItem = null;
        if (wipRemainder != 0)
        {
            wipItem = ("WIP_REMAINDER",
                $"WIP ledger debits {wipDebits:0.00} − credits {wipCredits:0.00} = {wipRemainder:0.00}", wipRemainder);
            items.Add(wipItem.Value);
        }

        var scrapRows = scraps.Select(s => (object)new
        {
            scrapId = s.Id, s.ItemSku, s.Qty, s.UnitCost, s.ReasonCode,
            disposition = s.Disposition.ToString(),
            value = s.Disposition == ScrapDisposition.Disposal
                ? decimal.Round(s.Qty * s.UnitCost, 2, MidpointRounding.AwayFromZero) : 0m,
            s.JournalId,
        }).ToList();
        return (items, wipDebits, wipCredits, wipRemainder, scrapRows, wipItem);
    }

    // 069: the close event carries the cost handoff payload (journal + payload IS the handoff)
    private void EmitOrderClosed(CurrentUser closer, ProductionOrder order, decimal wipDebits, decimal wipCredits,
        decimal wipRemainder, string? residualJournalId)
    {
        var revision = order.ReleasedBomRevisionId is { } rid ? mfg.FindBom(rid) : null;
        var lines = revision is null ? new List<BomLine>() : mfg.LinesForRevision(revision.Id);
        var issues = mfg.IssuesFor(order.Id);
        var completions = mfg.CompletionsFor(order.Id);
        var scraps = mfg.ScrapsFor(order.Id);
        var scrapTotal = decimal.Round(scraps.Where(s => s.Disposition == ScrapDisposition.Disposal)
            .Sum(s => decimal.Round(s.Qty * s.UnitCost, 2, MidpointRounding.AwayFromZero)), 2, MidpointRounding.AwayFromZero);
        var completionTotal = completions.Sum(c => CompletionValue(c.Qty, lines, revision?.OutputQtyPer ?? 1m));
        audit.Emit(closer, "mfg.order.closed", new
        {
            orderId = order.Id, orderNo = order.OrderNo, item = order.OutputSku,
            currency = EntityContext.SeedEntity.Currency,
            quantities = new
            {
                demanded = order.DemandedQty, planned = order.PlannedQty,
                released = order.ReleasedQty, completed = order.CompletedQty,
            },
            cost = new
            {
                // material issued to WIP net of returns — component scrap disposals are reported
                // once, in `scrap` (fix round 1: they are WIP debits in the ledger, not material)
                material = decimal.Round(issues.Where(i => i.Kind == MovementKind.Issue).Sum(i => i.LineValue)
                    - issues.Where(i => i.Kind == MovementKind.Return).Sum(i => i.LineValue), 2, MidpointRounding.AwayFromZero),
                scrap = scrapTotal,
                residual = wipRemainder > 0 ? wipRemainder : 0m,
                plannedUnitCost = revision is null ? 0m : PlannedUnitCost(lines, revision.OutputQtyPer),
                costingBasis = revision is null
                    ? "no released revision"
                    : $"planned unit cost from released BOM revision {revision.Revision} ({revision.Id}), R-MFG5",
            },
            sourceEventRefs = new
            {
                issueIds = issues.Where(i => i.Kind == MovementKind.Issue).Select(i => i.Id).ToArray(),
                returnIds = issues.Where(i => i.Kind == MovementKind.Return).Select(i => i.Id).ToArray(),
                completionIds = completions.Select(c => c.Id).ToArray(),
                completionJournalIds = completions.Where(c => c.JournalId is not null).Select(c => c.JournalId!).ToArray(),
                scrapIds = scraps.Select(s => s.Id).ToArray(),
                residualJournalId,
            },
            closedBy = closer.Id,
        });
    }

    private Journal NewJournal(string id, CurrentUser user, string memo, JournalLine[] lines) =>
        new(id, ec.TenantId, ec.EntityId, JournalStatus.Posted, memo, lines, DateTimeOffset.UtcNow, user.Id);

    // the 1420 WIP accumulator every MFG posting flows through
    private static decimal BatchCostSum(IReadOnlyList<BomLine> lines) => lines.Sum(l => l.QtyPer * l.UnitCost);

    private static decimal PlannedUnitCost(IReadOnlyList<BomLine> lines, decimal outputQtyPer) =>
        decimal.Round(BatchCostSum(lines) / outputQtyPer, 2, MidpointRounding.AwayFromZero);

    // rounded once at the total level so a fully reconciled order closes with an exact zero remainder
    private static decimal CompletionValue(decimal qty, IReadOnlyList<BomLine> lines, decimal outputQtyPer) =>
        decimal.Round(qty * BatchCostSum(lines) / outputQtyPer, 2, MidpointRounding.AwayFromZero);

    // the scalar lock gains the tenant+entity predicates (fix round 1, minor a — mirrors
    // SalesOrderService.LockCustomerForExposure): the lock, the re-reads and the commit all
    // serialize on the same scoped row
    private void LockOrderRow(string orderId) =>
        db.Database.SqlQuery<string>(
            $"SELECT id AS \"Value\" FROM production_orders WHERE id = {orderId} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId} FOR UPDATE").Single();

    // fix round 1 (F1.2): backflush line values are allocated off the completion credit basis —
    // each line takes its proportional share of the credit attributable to backflush lines, the
    // LAST line plugs the remainder — so Σ line debit equals the credit basis exactly. Per-line
    // raw rounding used to leave a cents-level CREDIT remainder that R-MFG6 hard-blocked forever.
    private static List<decimal> AllocateBackflushValues(decimal completionValue,
        List<(BomLine Line, decimal Qty)> backflush, IReadOnlyList<BomLine> allLines)
    {
        var raw = backflush.Select(b => decimal.Round(b.Qty * b.Line.UnitCost, 2, MidpointRounding.AwayFromZero)).ToList();
        var rawSum = raw.Sum();
        var backflushCost = backflush.Sum(b => b.Line.QtyPer * b.Line.UnitCost);
        var allCost = allLines.Sum(l => l.QtyPer * l.UnitCost);
        var target = allCost == 0 ? 0m : decimal.Round(completionValue * backflushCost / allCost, 2, MidpointRounding.AwayFromZero);

        var values = new List<decimal>();
        var assigned = 0m;
        for (var i = 0; i < backflush.Count; i++)
        {
            var share = i == backflush.Count - 1
                ? target - assigned // the plug: Σ values == target, exactly
                : rawSum == 0 ? 0m : decimal.Round(target * raw[i] / rawSum, 2, MidpointRounding.AwayFromZero);
            values.Add(share);
            assigned += share;
        }
        return values;
    }

    // the tokenized UPDATE matched zero rows — a stale retry wrote nothing (T7 review 1a)
    private (object? data, string? code, string? message) Concurrency() =>
        (null, "MFG_CONCURRENCY_ERROR",
            "The order changed under a stale snapshot — the guarded status UPDATE matched zero rows and nothing was written; reload and retry.");

    private (object? data, string? code, string? message)? SaveOrder(ProductionOrder order, string? uniqueConflictCode = null)
    {
        try
        {
            mfg.Save();
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency();
        }
        // fix round 1 (minor b): concurrent post-release amendments both compute {orderId}:v{n}
        // from the same count — the loser's natural-key insert collides and maps to an explicit
        // 409 instead of an unhandled exception
        catch (DbUpdateException ex) when (uniqueConflictCode is not null && IsUniqueViolation(ex))
        {
            return (null, uniqueConflictCode,
                "Another amendment claimed the same version of this order at the same moment — the amendment " +
                $"natural key {order.Id}:vN collided; reload the order and retry with the current planned quantity.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private void EmitIssue(CurrentUser user, ProductionOrder order, MaterialIssue issue, string kind)
    {
        // the brief's event list names issue/return under mfg.issue.posted — `kind` disambiguates
        audit.Emit(user, "mfg.issue.posted", new
        {
            orderId = order.Id, orderNo = order.OrderNo, issueId = issue.Id, componentSku = issue.ComponentSku,
            qty = issue.Qty, unitCost = issue.UnitCost, lineValue = issue.LineValue, kind,
            linkedIssueId = issue.LinkedIssueId, linkedCompletionId = issue.LinkedCompletionId,
            journalId = issue.JournalId, releasedBomRevisionId = order.ReleasedBomRevisionId, reportedBy = user.Id,
        });
    }

    private static object ProjOrder(ProductionOrder o) => new
    {
        o.Id, o.OrderNo, o.OutputSku, o.Unit, o.Status,
        o.DemandedQty, o.PlannedQty, o.ReleasedQty, o.CompletedQty,
        remainingQty = o.PlannedQty - o.CompletedQty, // computed, never stored twice (008 AC1)
        o.OverTolerancePct, o.PlannedBomRevisionId, o.ReleasedBomRevisionId,
        o.PlannedRoutingRevisionId, o.ReleasedRoutingRevisionId,
        o.CancelReason, o.ClosedBy, o.ClosedAt, o.ReopenedBy, o.ReopenedAt, o.CreatedBy, o.CreatedAt,
    };

    private static object ProjIssue(MaterialIssue i) => new
    {
        i.Id, i.OrderId, i.ComponentSku, i.Kind, i.Qty, i.UnitCost, i.LineValue,
        i.LinkedIssueId, i.LinkedCompletionId, i.JournalId, i.ReportedBy, i.ReportedAt,
    };
}
