using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// 004 — entity fiscal calendars: create a year layout (4-4-5 or plain months), activate it
// against the coverage chain (overlap/gap rejected), materializing FiscalPeriod rows.
public interface IFiscalCalendarService
{
    object List();
    (FiscalCalendar? calendar, string? code, string? message) Create(CurrentUser user, CreateFiscalCalendarRequest request);
    (FiscalCalendar? calendar, string? code, string? message) Activate(CurrentUser user, string calendarId);
}

// 062/063 — close checklists + the money gate consumed by FiscalPeriodService.Close.
public interface ICloseService
{
    object List(string? periodId);
    (List<CloseTask>? tasks, string? code, string? message) CreateChecklist(CurrentUser user, CreateCloseChecklistRequest request);
    (CloseTask? task, string? code, string? message) AddTask(CurrentUser user, string periodId, AddCloseTaskRequest request);
    (CloseTask? task, string? code, string? message) Complete(CurrentUser user, string periodId, string taskId, CompleteCloseTaskRequest request);
    (CloseTask? task, string? code, string? message) MarkNotApplicable(CurrentUser user, string periodId, string taskId, NaCloseTaskRequest request);
    List<CloseTask> UnresolvedMandatory(string periodId);
    void WaiveAllForClose(CurrentUser user, string periodId, string reason);
}
