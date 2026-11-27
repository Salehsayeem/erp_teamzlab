namespace Erp.Api;

// --- FINANCE-deep P1: entity fiscal calendars (004), close checklists (062/063).
// Tenant+entity on every table; ids from the shared doc_ sequences (doc_fcal/doc_ctk).

// 004: CalendarMonths = plain monthly periods; FourFourFive = 12 periods walking 4-4-5 weeks
// plus an ADJUSTMENT period that carries the 53rd-week edge.
public enum CalendarPattern { CalendarMonths, FourFourFive }

public record FiscalCalendar(string Id, string TenantId, string LegalEntityId, string Name, int Year,
    CalendarPattern Pattern, DateOnly StartDate, DateOnly EndDate, bool Activated,
    string? ActivatedBy = null, DateTimeOffset? ActivatedAt = null)
{
    public List<CalendarPeriod> Periods { get; set; } = [];
}

// one configured period of a calendar year — IsAdjustment marks the designated adjustment period
public record CalendarPeriod(string Name, DateOnly StartDate, DateOnly EndDate, bool IsAdjustment);

public enum CloseTaskStatus { Pending, Completed, Waived, NotApplicable }

// 062: a close-checklist task for one fiscal period. Terminal states carry their provenance
// (evidence ref / waiver reason / N-A reason + who + when) — rows are never removed, so the
// close evidence always retains the unresolved items a waiver let through (063 AC2).
public record CloseTask(string Id, string TenantId, string LegalEntityId, string PeriodId, string Title,
    bool Mandatory, CloseTaskStatus Status, DateOnly? DueDate = null, string? DependsOnTaskId = null,
    string? EvidenceRef = null, string? WaiverReason = null, string? NaReason = null,
    string? CompletedBy = null, DateTimeOffset? CompletedAt = null);
