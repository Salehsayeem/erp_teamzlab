namespace Erp.Api.Interfaces;

// Sales orders + shipments + lifecycle rows (S4: holds/amendments/cancellations/recurring)
public interface ISalesOrderRepository
{
    SalesOrder? Find(string soId);
    List<SalesOrder> All();
    List<SalesOrder> OpenForCustomer(string customerId); // Confirmed or CreditHold (057 exposure basis)
    void Add(SalesOrder so);
    void MarkSubmitted(SalesOrder so);
    void MarkConfirmed(SalesOrder so, string approverId);
    void MarkRejected(SalesOrder so, string approverId, string reason);
    void MarkShipped(SalesOrder so);
    void MarkInvoiced(SalesOrder so);
    void MarkCreditHold(SalesOrder so, string approverId);
    void AddShipment(Shipment shipment);
    List<Shipment> ShipmentsFor(string soId);
    Dictionary<string, decimal> ShippedBySku(string soId);
    void SetSoStatus(SalesOrder so, SoStatus status);
    void SetLineExpectedDate(SalesOrder so, string itemSku, DateOnly expectedDate);
    void ReplaceLines(SalesOrder so, IReadOnlyList<SoLine> lines);
    // holds (035)
    void AddHold(OrderHold hold);
    OrderHold? FindHold(string holdId);
    List<OrderHold> HoldsFor(string soId);
    int ActiveHoldCount(string soId);
    void SetHoldCleared(OrderHold hold, string clearedBy);
    // amendments (034)
    void AddAmendment(SalesOrderAmendment amendment);
    SalesOrderAmendment? FindAmendment(string amendmentId);
    List<SalesOrderAmendment> AmendmentsFor(string soId);
    void SetAmendmentStatus(SalesOrderAmendment amendment, AmendmentStatus status, string? approverId);
    // cancellations (039)
    void AddCancellation(OrderCancellation cancellation);
    List<OrderCancellation> CancellationsFor(string soId);
    // recurring schedules (040)
    void AddSchedule(RecurringSchedule schedule);
    RecurringSchedule? FindSchedule(string scheduleId);
    List<RecurringSchedule> Schedules();
    void SetScheduleStatus(RecurringSchedule schedule, RecurringScheduleStatus status);
    RecurringOccurrence? FindOccurrence(string scheduleId, string periodKey);
    void AddOccurrence(RecurringOccurrence occurrence);
    int Save();
}
