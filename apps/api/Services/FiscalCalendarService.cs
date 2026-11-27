using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// 004 — entity fiscal calendars. A calendar year drafts its period layout (4-4-5 walking weeks
// + an ADJUSTMENT period carrying the 53rd-week edge, or plain months); ACTIVATION validates the
// coverage chain (004 AC2: overlap → CALENDAR_OVERLAP, hole → CALENDAR_GAP — posting into a hole
// would otherwise die later on PERIOD_NOT_CONFIGURED) and materializes FiscalPeriod rows so the
// FinanceGuards lock and the period status board see the calendar immediately.
public sealed class FiscalCalendarService(ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : IFiscalCalendarService
{
    private IQueryable<FiscalCalendar> Calendars => db.FiscalCalendars
        .Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);

    private IQueryable<FiscalPeriod> Periods => db.FiscalPeriods
        .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);

    public object List() => Calendars.OrderBy(c => c.StartDate).ToList();

    public (FiscalCalendar? calendar, string? code, string? message) Create(CurrentUser user, CreateFiscalCalendarRequest request)
    {
        var name = request.Name.Trim();
        if (Guards.Check(name, "Name", 100) is { } nameError) return (null, "VALIDATION_ERROR", nameError);
        if (request.Year is < 2000 or > 2200)
            return (null, "VALIDATION_ERROR", "Year must be between 2000 and 2200.");
        if (!Enum.TryParse<CalendarPattern>(request.Pattern, ignoreCase: true, out var pattern))
            return (null, "VALIDATION_ERROR", $"Pattern '{request.Pattern}' is not a calendar pattern (CalendarMonths, FourFourFive).");
        if (request.StartDate.Year != request.Year)
            return (null, "VALIDATION_ERROR", "StartDate must fall inside the calendar's Year.");
        var endDate = new DateOnly(request.Year, 12, 31);
        if (endDate <= request.StartDate)
            return (null, "VALIDATION_ERROR", "The calendar year must end after its StartDate.");
        if (Calendars.Any(c => c.Year == request.Year && c.Pattern == pattern))
            return (null, "CALENDAR_EXISTS",
                $"A {pattern} calendar for {request.Year} already exists for this legal entity.");

        var calendar = new FiscalCalendar(numbers.Next("FCAL"), ec.TenantId, ec.EntityId, name,
            request.Year, pattern, request.StartDate, endDate, Activated: false)
        {
            Periods = BuildPeriods(pattern, request.StartDate, endDate),
        };
        db.FiscalCalendars.Add(calendar);
        audit.Log(user, "create", "fiscal_calendar", calendar.Id,
            $"Calendar {calendar.Name} drafted for {request.Year} ({pattern}: {calendar.Periods.Count} periods {request.StartDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}, incl. adjustment period).");
        db.SaveChanges();
        return (calendar, null, null);
    }

    public (FiscalCalendar? calendar, string? code, string? message) Activate(CurrentUser user, string calendarId)
    {
        var calendar = Calendars.FirstOrDefault(c => c.Id == calendarId);
        if (calendar is null) return (null, "CALENDAR_NOT_FOUND", "Fiscal calendar not found.");
        if (calendar.Activated) return (null, "CALENDAR_ALREADY_ACTIVATED", $"Calendar {calendar.Name} is already activated.");

        var coverage = Calendars.Where(c => c.Activated && c.Id != calendar.Id).OrderBy(c => c.StartDate).ToList();
        // 004 AC2 — overlap: any intersecting coverage is ambiguous bookkeeping
        var overlapped = coverage.FirstOrDefault(c => c.StartDate <= calendar.EndDate && c.EndDate >= calendar.StartDate);
        if (overlapped is not null)
            return (null, "CALENDAR_OVERLAP",
                $"Coverage {calendar.StartDate:yyyy-MM-dd}..{calendar.EndDate:yyyy-MM-dd} overlaps activated calendar {overlapped.Name} ({overlapped.StartDate:yyyy-MM-dd}..{overlapped.EndDate:yyyy-MM-dd}).");
        // 004 AC2 — gap: the chain must stay contiguous; a new year has to extend it at an edge
        var previous = coverage.LastOrDefault(c => c.EndDate < calendar.StartDate);
        var next = coverage.FirstOrDefault(c => c.StartDate > calendar.EndDate);
        if (coverage.Count > 0
            && (previous is null || previous.EndDate.AddDays(1) != calendar.StartDate)
            && (next is null || calendar.EndDate.AddDays(1) != next.StartDate))
            return (null, "CALENDAR_GAP",
                $"Coverage {calendar.StartDate:yyyy-MM-dd}..{calendar.EndDate:yyyy-MM-dd} would leave a gap in the activated chain" +
                (previous is not null ? $" after {previous.Name} (ends {previous.EndDate:yyyy-MM-dd})" : "") +
                (next is not null ? $" before {next.Name} (starts {next.StartDate:yyyy-MM-dd})" : "") + ".");

        var names = calendar.Periods.Select(p => p.Name).ToList();
        var clash = Periods.Any(p => names.Contains(p.Name));
        if (clash)
            return (null, "PERIOD_OVERLAP",
                $"A fiscal period named for {calendar.Name}'s periods already exists; calendars materialize FP-<name> periods and cannot re-create them.");

        foreach (var p in calendar.Periods)
            db.FiscalPeriods.Add(new FiscalPeriod($"FP-{p.Name}", ec.TenantId, ec.EntityId, p.Name,
                p.StartDate, p.EndDate, PeriodStatus.Open, IsAdjustment: p.IsAdjustment));
        var activated = calendar with { Activated = true, ActivatedBy = user.Id, ActivatedAt = DateTimeOffset.UtcNow };
        db.FiscalCalendars.Entry(calendar).CurrentValues.SetValues(activated);
        audit.Log(user, "activate", "fiscal_calendar", calendar.Id,
            $"Calendar {calendar.Name} activated by {user.Email}: {calendar.Periods.Count} periods materialized ({calendar.StartDate:yyyy-MM-dd}..{calendar.EndDate:yyyy-MM-dd}); adjustment period {calendar.Periods.Last(p => p.IsAdjustment).Name} designated.");
        audit.Emit(user, "finance.calendar.activated",
            new { calendarId = calendar.Id, year = calendar.Year, pattern = calendar.Pattern.ToString(), periods = calendar.Periods.Count, activatedBy = user.Id });
        db.SaveChanges();
        return (activated, null, null);
    }

    // 4-4-5: quarters of 4-4-5 weeks — 12 periods cover 336 days, everything up to year end is
    // the ADJUSTMENT period (a 53-week year pushes its extra week there, never into a named month)
    private static List<CalendarPeriod> BuildPeriods(CalendarPattern pattern, DateOnly start, DateOnly end)
    {
        var periods = new List<CalendarPeriod>();
        if (pattern == CalendarPattern.CalendarMonths)
        {
            for (var m = 0; m < 12; m++)
            {
                var monthStart = start.AddMonths(m);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                periods.Add(new CalendarPeriod(monthStart.ToString("yyyy-MM"),
                    monthStart, monthEnd > end ? end : monthEnd, IsAdjustment: false));
            }
        }
        else
        {
            int[] weeks = [4, 4, 5];
            var cursor = start;
            for (var q = 0; q < 4; q++)
            {
                for (var i = 0; i < 3; i++)
                {
                    var periodEnd = cursor.AddDays(weeks[i] * 7 - 1);
                    periods.Add(new CalendarPeriod($"{start.Year}-P{periods.Count + 1:00}", cursor, periodEnd, IsAdjustment: false));
                    cursor = periodEnd.AddDays(1);
                }
            }
            periods.Add(new CalendarPeriod($"{start.Year}-ADJ", cursor, end, IsAdjustment: true));
        }
        return periods;
    }
}
