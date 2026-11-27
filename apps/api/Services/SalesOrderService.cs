using Erp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class SalesOrderService(
    ICustomerRepository customers, ISalesOrderRepository orders, IPurchaseOrderRepository costing,
    IInventoryRepository inventory, IJournalRepository journals, IInvoiceRepository invoiceDocs,
    INumberSeries numbers, IAuditRepository audit, IQuoteRepository quoteRevisions,
    ICustomer360Repository customer360, EntityContext ec, ErpDbContext db) : ISalesOrderService
{
    public object List() =>
        orders.All().Select(s => new
        {
            s.Id, s.CustomerId, s.Status, s.Lines, s.CreatedBy, s.CreatedAt, s.ApprovedBy, s.ApprovedAt, s.RejectionReason,
            s.SourceQuoteRevisionId,
            total = s.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
            // S4 list badges: blocking holds + unapproved amendments are the two things a
            // dispatcher scans the list for
            activeHolds = orders.ActiveHoldCount(s.Id),
            pendingAmendment = s.Status == SoStatus.PendingAmendmentApproval
                || orders.AmendmentsFor(s.Id).Any(a => a.Status == AmendmentStatus.Pending),
        }).ToList();

    // S4: order detail — lines carry fulfilled/remaining/backorder + expected date (036/037),
    // plus the lifecycle rows (holds/amendments/cancellations)
    public (object? data, string? code, string? message) Detail(string soId)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        var shipped = orders.ShippedBySku(soId);
        return (new
        {
            so.Id, so.CustomerId, so.Status, so.CreatedBy, so.CreatedAt, so.ApprovedBy, so.ApprovedAt,
            so.RejectionReason, so.SourceQuoteRevisionId,
            total = so.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
            lines = so.Lines.Select(l =>
            {
                var fulfilled = shipped.TryGetValue(l.ItemSku, out var f) ? f : 0;
                var remaining = decimal.Max(0, l.Qty - fulfilled);
                return new
                {
                    l.ItemSku, l.Description, l.Qty, l.UnitPrice, l.ExpectedDate,
                    fulfilledQty = fulfilled,
                    remainingQty = remaining,
                    // backorder = the remaining slice that carries an expected date (037)
                    backorderQty = l.ExpectedDate is not null ? remaining : 0,
                };
            }),
            holds = orders.HoldsFor(soId),
            amendments = orders.AmendmentsFor(soId).Select(a => new
            {
                a.Id, a.Revision, a.Status, a.Rationale, a.Lines, a.CreatedBy, a.CreatedAt, a.ApprovedBy, a.ApprovedAt, a.RejectionReason,
            }),
            cancellations = orders.CancellationsFor(soId),
        }, null, null);
    }

    // --- S4: holds (035) -------------------------------------------------------------

    // any blocking hold prevents progression; typed holds carry reason/source/set actor-time
    public (OrderHold? hold, string? code, string? message) PlaceHold(CurrentUser u, string soId, OrderHoldType holdType, string reason, string? source)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Confirmed)
            return (null, "SO_STATE_ERROR", $"Only confirmed sales orders can be held (current: {so.Status}).");
        if (!u.Has("so:write")) return (null, "FORBIDDEN", "so:write permission required.");
        var hold = new OrderHold($"{soId}:H{orders.HoldsFor(soId).Count + 1}", ec.TenantId, ec.EntityId,
            soId, holdType, OrderHoldStatus.Active, reason, source ?? u.RoleId, u.Id, DateTimeOffset.UtcNow);
        orders.AddHold(hold);
        audit.Log(u, "hold", "sales_order", soId, $"{holdType} hold: {reason}");
        audit.Emit(u, "sales.so.held", new { soId, holdType, reason, setBy = u.Id });
        orders.Save();
        return (hold, null, null);
    }

    public (OrderHold? hold, string? code, string? message) ReleaseHold(CurrentUser u, string holdId, string? reason)
    {
        var hold = orders.FindHold(holdId);
        if (hold is null) return (null, "HOLD_NOT_FOUND", "Order hold not found.");
        if (!u.Has("so:write")) return (null, "FORBIDDEN", "so:write permission required.");
        try
        {
            orders.SetHoldCleared(hold, u.Id);
            audit.Log(u, "hold_release", "sales_order", hold.SalesOrderId, $"Hold {holdId} cleared: {reason ?? "no reason given"}");
            orders.Save();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, "HOLD_ALREADY_CLEARED", "Hold was cleared concurrently by another session.");
        }
        return (hold, null, null);
    }

    // --- S4: amendments (034) --------------------------------------------------------

    // material edit → amendment with the NEW lines; fulfillment/billing see them only after
    // the required approval (034 AC2) — the order flips to PendingAmendmentApproval meanwhile
    public (SalesOrderAmendment? amendment, string? code, string? message) CreateAmendment(
        CurrentUser u, string soId, IReadOnlyList<SoLine> lines, string rationale)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Confirmed)
            return (null, "SO_AMENDMENT_ERROR", $"Only confirmed sales orders can be amended (current: {so.Status}).");
        if (!u.Has("so:write")) return (null, "FORBIDDEN", "so:write permission required.");
        var revisionNo = orders.AmendmentsFor(soId).Count + 1;
        var amendment = new SalesOrderAmendment($"{soId}:A{revisionNo}", ec.TenantId, ec.EntityId, soId,
            revisionNo, AmendmentStatus.Pending, rationale, u.Id, DateTimeOffset.UtcNow, lines);
        orders.AddAmendment(amendment);
        orders.SetSoStatus(so, SoStatus.PendingAmendmentApproval);
        audit.Log(u, "amend", "sales_order", soId, $"Amendment {revisionNo} proposed: {rationale}");
        orders.Save();
        return (amendment, null, null);
    }

    public (SalesOrderAmendment? amendment, string? code, string? message) ApproveAmendment(
        CurrentUser u, string amendmentId, string? reason)
    {
        var amendment = orders.FindAmendment(amendmentId);
        if (amendment is null) return (null, "AMENDMENT_NOT_FOUND", "Order amendment not found.");
        var so = orders.Find(amendment.SalesOrderId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (amendment.Status != AmendmentStatus.Pending)
            return (null, "SO_AMENDMENT_ERROR", $"Only pending amendments can be approved (current: {amendment.Status}).");
        if (amendment.CreatedBy == u.Id)
            return (null, "SO_AMENDMENT_ERROR", "Maker-checker: the proposer cannot approve their own amendment.");
        if (!u.Has("so:approve")) return (null, "FORBIDDEN", "so:approve permission required.");

        if (reason is not null)
        {
            orders.SetAmendmentStatus(amendment, AmendmentStatus.Rejected, u.Id);
            Set(amendment, nameof(SalesOrderAmendment.RejectionReason), reason);
            orders.SetSoStatus(so, SoStatus.Confirmed); // prior values were never touched
            audit.Log(u, "amend_reject", "sales_order", so.Id, $"Amendment rejected: {reason}");
            orders.Save();
            return (amendment, null, null);
        }

        orders.SetAmendmentStatus(amendment, AmendmentStatus.Approved, u.Id);
        // copies, not the tracked instances — a shared-type owned row can only belong to one owner
        orders.ReplaceLines(so, amendment.Lines.Select(l => l with { }).ToList());
        orders.SetSoStatus(so, SoStatus.Confirmed);
        audit.Log(u, "amend_approve", "sales_order", so.Id, $"Amendment {amendment.Revision} applied by {u.Email}");
        audit.Emit(u, "sales.so.amended", new { soId = so.Id, amendmentId, approvedBy = u.Id });
        orders.Save();
        return (amendment, null, null);
    }

    // --- S4: cancellations (039) -----------------------------------------------------

    // scope Remaining cancels the unshipped remainder (so:write); scope Full after any
    // shipment is post-release and needs so:approve by an officer who is not the maker.
    // Posted invoices remain untouched — corrections ride the credit-note flow (039 AC2).
    // Reservation release is out of scope (Q-S4: no formal reservation contract this wave).
    public (object? data, string? code, string? message) CancelOrder(CurrentUser u, string soId, string reason, CancellationScope scope)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status is SoStatus.Cancelled or SoStatus.Rejected or SoStatus.Invoiced)
            return (null, "SO_CANCEL_ERROR", $"A {so.Status} order cannot be cancelled.");
        var shipped = orders.ShippedBySku(soId);
        var hasShipments = shipped.Any(kv => kv.Value > 0);
        if (scope == CancellationScope.Full && hasShipments)
        {
            if (!u.Has("so:approve"))
                return (null, "FORBIDDEN", "Post-release full cancellation requires so:approve.");
            if (u.Id == so.CreatedBy)
                return (null, "FORBIDDEN", "Post-release full cancellation requires an approver who is not the maker.");
        }
        else if (!u.Has("so:write"))
        {
            return (null, "FORBIDDEN", "so:write permission required.");
        }

        var remainingByLine = so.Lines.Select(l => new
        {
            itemSku = l.ItemSku,
            ordered = l.Qty,
            fulfilled = shipped.TryGetValue(l.ItemSku, out var f) ? f : 0,
            remaining = decimal.Max(0, l.Qty - (shipped.TryGetValue(l.ItemSku, out var g) ? g : 0)),
        }).ToList();
        var remainingQty = remainingByLine.Sum(r => r.remaining);
        var cancellation = new OrderCancellation($"{soId}:C{orders.CancellationsFor(soId).Count + 1}",
            ec.TenantId, ec.EntityId, soId, scope, reason,
            System.Text.Json.JsonSerializer.Serialize(new { scope, remainingQty, lines = remainingByLine }, Idem.JsonOptions),
            u.Id, DateTimeOffset.UtcNow);
        orders.AddCancellation(cancellation);
        orders.SetSoStatus(so, SoStatus.Cancelled);
        audit.Log(u, "cancel", "sales_order", soId, $"{scope} cancellation: {reason}; remaining {remainingQty:0.##}");
        audit.Emit(u, "sales.so.cancelled", new { soId, scope, reason, remainingQty, cancelledBy = u.Id });
        orders.Save();
        return (new
        {
            so.Id, so.Status, cancellation.Scope, cancellation.Reason, remainingQty, lines = remainingByLine,
            cancelledBy = u.Id, cancelledAt = cancellation.CancelledAt, postedInvoicesUntouched = true,
        }, null, null);
    }

    // --- S4: recurring schedules (040) -----------------------------------------------

    public (RecurringSchedule? schedule, string? code, string? message) CreateSchedule(
        CurrentUser u, string customerId, RecurringFrequency frequency, DateOnly nextRunDate, IReadOnlyList<SoLine> lines)
    {
        if (!customers.Exists(customerId))
            return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        if (nextRunDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return (null, "VALIDATION_ERROR", "nextRunDate must be today or later.");
        if (!u.Has("recurring:write")) return (null, "FORBIDDEN", "recurring:write permission required.");
        var schedule = new RecurringSchedule(numbers.Next("RCS"), ec.TenantId, ec.EntityId, customerId,
            frequency, RecurringScheduleStatus.Active, nextRunDate, u.Id, DateTimeOffset.UtcNow, lines);
        orders.AddSchedule(schedule);
        audit.Log(u, "create", "recurring_schedule", schedule.Id, $"{frequency} schedule for {customerId}");
        orders.Save();
        return (schedule, null, null);
    }

    public (RecurringSchedule? schedule, string? code, string? message) PauseSchedule(CurrentUser u, string scheduleId)
    {
        var schedule = orders.FindSchedule(scheduleId);
        if (schedule is null) return (null, "SCHEDULE_NOT_FOUND", "Recurring schedule not found.");
        if (!u.Has("recurring:write")) return (null, "FORBIDDEN", "recurring:write permission required.");
        if (schedule.Status != RecurringScheduleStatus.Active)
            return (null, "SCHEDULE_STATE_ERROR", $"Only active schedules can be paused (current: {schedule.Status}).");
        orders.SetScheduleStatus(schedule, RecurringScheduleStatus.Paused); // history preserved (040 AC2)
        audit.Log(u, "pause", "recurring_schedule", scheduleId, "Schedule paused");
        orders.Save();
        return (schedule, null, null);
    }

    // 040 AC1: generation is keyed (schedule, period) — the unique index plus this check keep
    // it at ≤1 occurrence per period; a paused schedule produces nothing (040 AC2).
    // Roll-forward rule (040 edge): monthly clips to the month end, weekly steps 7 days —
    // Bangladesh local time has no DST, so no clock shifts are applied.
    public (object? data, string? code, string? message) Generate(CurrentUser u, string scheduleId, string period)
    {
        var schedule = orders.FindSchedule(scheduleId);
        if (schedule is null) return (null, "SCHEDULE_NOT_FOUND", "Recurring schedule not found.");
        if (schedule.Status == RecurringScheduleStatus.Paused)
            return (null, "SCHEDULE_PAUSED", "Paused schedules generate nothing while paused.");
        if (!u.Has("recurring:write")) return (null, "FORBIDDEN", "recurring:write permission required.");
        var periodKey = NormalizePeriod(period, schedule.Frequency);
        if (periodKey is null)
            return (null, "VALIDATION_ERROR", $"Period must be {(schedule.Frequency == RecurringFrequency.Monthly ? "\"YYYY-MM\"" : "\"YYYY-Www\" (ISO week)")} for a {schedule.Frequency} schedule.");

        var existing = orders.FindOccurrence(scheduleId, periodKey);
        if (existing is not null)
            return (OccurrenceView(schedule, periodKey, existing, replay: true), null, null);

        var so = new SalesOrder(numbers.Next("SO"), ec.TenantId, ec.EntityId, schedule.CustomerId,
            // fresh SoLine instances — the template's owned rows stay owned by the schedule
            SoStatus.Draft, schedule.Lines.Select(l => l with { }).ToList(), u.Id, DateTimeOffset.UtcNow);
        orders.Add(so);
        var occurrence = new RecurringOccurrence($"{scheduleId}:{periodKey}", ec.TenantId, ec.EntityId,
            scheduleId, periodKey, so.Id, u.Id, DateTimeOffset.UtcNow);
        orders.AddOccurrence(occurrence);
        audit.Log(u, "generate", "recurring_schedule", scheduleId, $"Occurrence {periodKey} → {so.Id}");
        audit.Emit(u, "sales.recurring.generated", new { scheduleId, period = periodKey, orderId = so.Id });
        orders.Save();
        return (OccurrenceView(schedule, periodKey, occurrence, replay: false), null, null);
    }

    private static object OccurrenceView(RecurringSchedule schedule, string periodKey, RecurringOccurrence occurrence, bool replay) => new
    {
        schedule.Id, period = periodKey, occurrence.OrderId, occurrenceId = occurrence.Id, replay,
        scheduleStatus = schedule.Status.ToString(),
    };

    private static string? NormalizePeriod(string period, RecurringFrequency frequency)
    {
        if (frequency == RecurringFrequency.Monthly)
            return DateTime.TryParseExact(period, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var m)
                ? $"{m:yyyy-MM}" : null;
        return DateTime.TryParseExact(period, "yyyy-'W'ww", null, System.Globalization.DateTimeStyles.None, out var w)
            ? $"{w.Year:0000}-W{System.Globalization.ISOWeek.GetWeekOfYear(w):00}" : null;
    }

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;

    public (SalesOrder? order, string? code, string? message) Create(CurrentUser u, string customerId, IReadOnlyList<SoLine> lines, string? sourceQuoteRevisionId = null)
    {
        if (!customers.Exists(customerId))
            return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        // 030 AC1: quote→order conversion — the referenced revision must exist and be Approved;
        // its id is stamped immutably on the order (visible on the generated order)
        if (sourceQuoteRevisionId is not null)
        {
            var revision = quoteRevisions.FindRevision(sourceQuoteRevisionId);
            if (revision is null)
                return (null, "QUOTE_REVISION_NOT_FOUND", "The referenced quote revision does not exist.");
            if (revision.Status != QuoteRevisionStatus.Approved)
                return (null, "QUOTE_STATE_ERROR", $"Only approved quote revisions convert to orders (current: {revision.Status}).");
        }
        var so = new SalesOrder(numbers.Next("SO"), ec.TenantId, ec.EntityId, customerId, SoStatus.Draft, lines, u.Id, DateTimeOffset.UtcNow,
            sourceQuoteRevisionId: sourceQuoteRevisionId);
        orders.Add(so);
        audit.Log(u, "create", "sales_order", so.Id, $"SO created for customer {customerId} with {lines.Count} line(s)");
        orders.Save();
        return (so, null, null);
    }

    public (object? data, string? code, string? message) Submit(CurrentUser u, string id)
    {
        var so = orders.Find(id);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Draft) return (null, "SO_NOT_SUBMITTABLE", "Only draft sales orders can be submitted.");
        // 013 AC2: an account with unmet mandatory onboarding controls cannot submit — the
        // unmet control ids ride back inside the envelope's error items
        var unmet = customer360.UnmetControlIds(so.CustomerId);
        if (unmet.Count > 0)
            return (unmet.Select(c => (object)new { code = c, detail = "mandatory onboarding control not met" }).ToArray(),
                "ONBOARDING_CONTROLS_UNMET", "This account has unmet mandatory onboarding controls — clear them before submitting.");
        orders.MarkSubmitted(so);
        audit.Log(u, "submit", "sales_order", id, "SO submitted for approval");
        orders.Save();
        return (so, null, null);
    }

    // R-T7i: approval is gated on so:approve (moved off so:write); approver ≠ creator still holds.
    // 057 AC2: an approval that would push exposure past the customer's credit limit lands the
    // order in CreditHold instead of Confirmed — release is a separate credit:override act.
    // Explicit decision binding (click-through fu-2): approvals always approve — a reason is an
    // approval note recorded in the audit trail, never a silent rejection. Rejection lives on
    // its own endpoint and requires a reason.
    public (SalesOrder? order, string? code, string? message) Approve(CurrentUser approver, string soId, string? reason)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.PendingApproval) return (null, "SO_APPROVAL_ERROR", $"Only pending sales orders can be approved (current: {so.Status}).");
        if (so.CreatedBy == approver.Id) return (null, "SO_APPROVAL_ERROR", "Maker-checker: the creator cannot approve their own sales order.");
        if (!approver.Has("so:approve")) return (null, "FORBIDDEN", "so:approve permission required.");

        // review 2a: the exposure check and the approval commit are serialized per customer —
        // SELECT … FOR UPDATE on the customer row inside the approval transaction means a
        // concurrent approval for the same customer waits, then re-reads fresh balances.
        // Without this, two officers approving together could both pass the limit check
        // against committed state and both confirm. When Idem.Wrap already opened the request
        // transaction, we join it instead of nesting a second one.
        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        LockCustomerForExposure(so.CustomerId);

        var (overLimit, exposure) = ExposureIfApproved(so);
        if (overLimit)
        {
            orders.MarkCreditHold(so, approver.Id);
            audit.Log(approver, "credit_hold", "sales_order", so.Id,
                $"SO approved but held: exposure {exposure:0.00} exceeds the customer credit limit");
            orders.Save();
            if (ownsTx) tx!.Commit();
            return (so, null, null);
        }

        orders.MarkConfirmed(so, approver.Id);
        audit.Log(approver, "approve", "sales_order", so.Id,
            reason is null ? $"SO approved by {approver.Email}" : $"SO approved by {approver.Email} — note: {reason}");
        audit.Emit(approver, "sales.so.approved", new { soId = so.Id, approver = approver.Id });
        orders.Save();
        if (ownsTx) tx!.Commit();
        return (so, null, null);
    }

    // Rejection is its own decision: requires a reason, lands the SO in Rejected.
    public (SalesOrder? order, string? code, string? message) Reject(CurrentUser approver, string soId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (null, "DECISION_REASON_REQUIRED", "A rejection reason is required.");
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.PendingApproval) return (null, "SO_APPROVAL_ERROR", $"Only pending sales orders can be rejected (current: {so.Status}).");
        if (so.CreatedBy == approver.Id) return (null, "SO_APPROVAL_ERROR", "Maker-checker: the creator cannot reject their own sales order.");
        if (!approver.Has("so:approve")) return (null, "FORBIDDEN", "so:approve permission required.");

        orders.MarkRejected(so, approver.Id, reason);
        audit.Log(approver, "reject", "sales_order", so.Id, $"SO rejected: {reason}");
        orders.Save();
        return (so, null, null);
    }

    // 057 AC2 release: CreditHold → Confirmed, credit:override gate, audited, outbox event
    public (SalesOrder? order, string? code, string? message) ReleaseCreditHold(CurrentUser user, string soId)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.CreditHold) return (null, "SO_STATE_ERROR", $"Only credit-held sales orders can be released (current: {so.Status}).");
        if (!user.Has("credit:override")) return (null, "FORBIDDEN", "credit:override permission required.");

        orders.MarkConfirmed(so, user.Id);
        audit.Log(user, "credit_release", "sales_order", so.Id, $"Credit hold released by {user.Email}; SO confirmed");
        audit.Emit(user, "sales.credit.released", new { soId = so.Id, releasedBy = user.Id });
        orders.Save();
        return (so, null, null);
    }

    public (Shipment? shipment, Journal? journal, string? code, string? message) Ship(
        CurrentUser user, string soId, string itemSku, decimal qty, DateOnly? expectedDate = null)
    {
        var so = orders.Find(soId);
        if (so is null) return (null, null, "SO_NOT_FOUND", "Sales order not found.");
        if (so.Status != SoStatus.Confirmed) return (null, null, "SHIPMENT_ERROR", $"Only confirmed sales orders can be shipped (current: {so.Status}).");
        if (qty <= 0) return (null, null, "VALIDATION_ERROR", "Shipment qty must be greater than 0.");
        var line = so.Lines.FirstOrDefault(l => l.ItemSku == itemSku);
        if (line is null) return (null, null, "ITEM_NOT_FOUND", $"Item {itemSku} is not on this sales order.");

        // 035 AC1: any blocking hold prevents progression — shipment is the progression here
        if (orders.ActiveHoldCount(soId) > 0)
            return (null, null, "SO_HELD", "This order has an active hold — clear it before shipping.");

        // 036: a line cannot ship past its ordered qty (fulfilled/remaining stay meaningful)
        var alreadyShipped = orders.ShippedBySku(soId).TryGetValue(itemSku, out var s) ? s : 0;
        if (alreadyShipped + qty > line.Qty)
            return (null, null, "SHIPMENT_ERROR",
                $"Shipment exceeds the ordered qty: {itemSku} ordered {line.Qty:0.##}, already shipped {alreadyShipped:0.##}, requested {qty:0.##}.");

        var onHand = inventory.QtyOf(itemSku);
        if (onHand < qty)
            return (null, null, "INSUFFICIENT_STOCK", $"Insufficient stock for {itemSku}: available {onHand}, requested {qty}.");

        // task-5: shipments post the COGS journal at today — the centralized period lock applies
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, null, guardCode, guardMsg);

        inventory.DrawStock(itemSku, qty);

        var shipment = new Shipment(numbers.Next("SHP"), ec.TenantId, ec.EntityId, soId, itemSku, qty, DateTimeOffset.UtcNow, user.Id);
        orders.AddShipment(shipment);

        // COGS at inventory cost basis — weighted average of receipt PO unit prices (0 when no receipts yet)
        var unitCost = costing.AverageUnitCost(itemSku);
        var amount = decimal.Round(unitCost * qty, 2, MidpointRounding.AwayFromZero);
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
            $"COGS for shipment {shipment.Id} (SO {soId})",
            new JournalLine[] { new("5100", "Cost of Goods Sold", amount, 0), new("1400", "Inventory", 0, amount) },
            DateTimeOffset.UtcNow, user.Id);
        journals.Add(journal);

        // all lines shipped? flip SO status
        var shippedBySku = orders.ShippedBySku(soId);
        if (so.Lines.All(l => shippedBySku.TryGetValue(l.ItemSku, out var got) && got >= l.Qty))
            orders.MarkShipped(so);
        // 037: a shortfall leaves a measurable remaining + expected date on the line
        else if (expectedDate is not null)
            orders.SetLineExpectedDate(so, itemSku, expectedDate.Value);

        audit.Log(user, "ship", "shipment", shipment.Id, $"Shipped {qty} x {itemSku} against {soId}; journal {journal.Id} posted");
        audit.Emit(user, "sales.shipment.posted", new { shipmentId = shipment.Id, soId, itemSku, qty, journalId = journal.Id });
        orders.Save(); // stock draw + shipment + journal + audit + outbox in ONE SaveChanges
        return (shipment, journal, null, null);
    }

    // would confirming this order push the customer's exposure past the credit limit?
    // exposure = open posted invoices (− credits + debits) + open Confirmed/CreditHold SO totals (R-T7e)
    private (bool overLimit, decimal exposure) ExposureIfApproved(SalesOrder so)
    {
        var customer = customers.Find(so.CustomerId);
        if (customer?.CreditLimit is not { } limit) return (false, 0); // no limit configured → no hold

        var openInvoiced = OpenInvoiced(so.CustomerId);
        var openOrders = orders.OpenForCustomer(so.CustomerId).Where(o => o.Id != so.Id).Sum(CreditMath.OrderTotal);
        var exposure = openInvoiced + openOrders + CreditMath.OrderTotal(so);
        return (exposure > limit, exposure);
    }

    internal decimal OpenInvoiced(string customerId) =>
        invoiceDocs.AllInvoices(InvoiceStatus.Posted, customerId)
            .Sum(i => CreditMath.InvoiceOpen(i, invoiceDocs.AdjustmentsForInvoice(i.Id)));

    // review 2a: parameterized row lock — the caller's transaction holds it until commit, so
    // exposure computation and commit are serialized per customer (pair with ExposureIfApproved)
    private void LockCustomerForExposure(string customerId) =>
        db.Customers
            .FromSql($"SELECT * FROM customers WHERE id = {customerId} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId} FOR UPDATE")
            .ToList();
}
