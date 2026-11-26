namespace Erp.Api.Interfaces;

public interface ISalesOrderService
{
    object List();
    // sourceQuoteRevisionId (030 AC1): the accepted quote revision stamped onto the generated order
    (SalesOrder? order, string? code, string? message) Create(CurrentUser user, string customerId, IReadOnlyList<SoLine> lines, string? sourceQuoteRevisionId = null);
    // Submit mirrors the MFG gate pattern: on ONBOARDING_CONTROLS_UNMET the unmet control ids
    // ride back as `data` and the controller wraps them in FailBodyItems (013 AC2)
    (object? data, string? code, string? message) Submit(CurrentUser user, string id);
    (SalesOrder? order, string? code, string? message) Approve(CurrentUser approver, string id, string? reason);
    (Shipment? shipment, Journal? journal, string? code, string? message) Ship(CurrentUser user, string id, string itemSku, decimal qty, DateOnly? expectedDate = null);
    // R-T7f: the journal-only Invoice shortcut was RETIRED — invoice documents live in IInvoiceService.
    // R-T7i: approval is gated on so:approve; over-limit approvals land CreditHold (057 AC2).
    (SalesOrder? order, string? code, string? message) ReleaseCreditHold(CurrentUser user, string id);

    // --- deferred-screens wave (S4): order lifecycle completion ---
    (object? data, string? code, string? message) Detail(string soId);
    (OrderHold? hold, string? code, string? message) PlaceHold(CurrentUser user, string soId, OrderHoldType holdType, string reason, string? source);
    (OrderHold? hold, string? code, string? message) ReleaseHold(CurrentUser user, string holdId, string? reason);
    (SalesOrderAmendment? amendment, string? code, string? message) CreateAmendment(CurrentUser user, string soId, IReadOnlyList<SoLine> lines, string rationale);
    (SalesOrderAmendment? amendment, string? code, string? message) ApproveAmendment(CurrentUser user, string amendmentId, string? reason);
    (object? data, string? code, string? message) CancelOrder(CurrentUser user, string soId, string reason, CancellationScope scope);
    (RecurringSchedule? schedule, string? code, string? message) CreateSchedule(CurrentUser user, string customerId, RecurringFrequency frequency, DateOnly nextRunDate, IReadOnlyList<SoLine> lines);
    (RecurringSchedule? schedule, string? code, string? message) PauseSchedule(CurrentUser user, string scheduleId);
    (object? data, string? code, string? message) Generate(CurrentUser user, string scheduleId, string period);
}
