using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// 004 — entity fiscal calendars. A calendar year drafts its period layout (4-4-5 walking weeks
// + an ADJUSTMENT period carrying the year remainder, or plain months); the engine is 52-WEEK
// 4-4-5 ONLY — a 53-week configuration cannot be named and is refused (CALENDAR_VALIDATION,
// spec 004 edge). ACTIVATION validates the layout and the coverage chain (004 AC2: overlap →
// CALENDAR_OVERLAP, hole → CALENDAR_GAP — posting into a hole would otherwise die later on
// PERIOD_NOT_CONFIGURED) and materializes FiscalPeriod rows so the FinanceGuards lock and the
// period status board see the calendar immediately.
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
        // P1 review 4 — a mid-year StartDate walks the period layout past Dec 31 and materializes
        // INVERTED rows (StartDate > EndDate); only January 1 of Year lays out cleanly
        if (request.StartDate != new DateOnly(request.Year, 1, 1))
            return (null, "VALIDATION_ERROR",
                $"StartDate must be January 1 of {request.Year}; a mid-year start would materialize inverted period rows.");
        var endDate = new DateOnly(request.Year, 12, 31);
        if (Calendars.Any(c => c.Year == request.Year && c.Pattern == pattern))
            return (null, "CALENDAR_EXISTS",
                $"A {pattern} calendar for {request.Year} already exists for this legal entity.");

        var periods = BuildPeriods(pattern, request.StartDate, endDate);
        if (UnnameableLayout(pattern, periods))
            return (null, "CALENDAR_VALIDATION", LayoutError(request.Year, request.StartDate, endDate));
        var calendar = new FiscalCalendar(numbers.Next("FCAL"), ec.TenantId, ec.EntityId, name,
            request.Year, pattern, request.StartDate, endDate, Activated: false)
        {
            Periods = periods,
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

        // P1 review 3 — stored rows are re-validated before materialization: the layout guard runs
        // BEFORE the coverage checks so the message names the row's layout defect, not a downstream gap
        if (UnnameableLayout(calendar.Pattern, BuildPeriods(calendar.Pattern, calendar.StartDate, calendar.EndDate)))
            return (null, "CALENDAR_VALIDATION", LayoutError(calendar.Year, calendar.StartDate, calendar.EndDate));
        // P1 review 4 — the Jan-1 rule re-checked on stored rows (rows created before the rule,
        // or via any future write path, cannot activate a mid-year layout either)
        if (calendar.StartDate != new DateOnly(calendar.Year, 1, 1))
            return (null, "VALIDATION_ERROR",
                $"Calendar {calendar.Name} starts mid-year ({calendar.StartDate:yyyy-MM-dd}); StartDate must be January 1 of {calendar.Year} — its period layout would materialize inverted rows.");

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
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
        {
            SqlState: "23505",
            ConstraintName: "PK_fiscal_periods",
        })
        {
            // P1 review 5 — FP-<name> period ids share one GLOBAL PK across legal entities until
            // R14's entity-segmented id scheme (Option B) lands; the entity-scoped pre-check above
            // cannot see another entity's same-named period, so the PK is the real guard. Loud
            // 409, never a 500 (catch narrowed to THAT constraint per the P5 void-catch lesson).
            return (null, "CALENDAR_PERIOD_CONFLICT",
                $"A fiscal period id that {calendar.Name} would materialize (FP-<period name>) already exists for another legal entity — the FP-<name> id scheme is global until the R14 entity-segmented id redesign.");
        }
        return (activated, null, null);
    }

    // 4-4-5: quarters of 4-4-5 weeks — 12 periods cover 364 days; the year remainder (1-2 days
    // on a Gregorian year) is the ADJUSTMENT period. The engine is 52-week 4-4-5 only: a
    // 53-week configuration cannot be laid out as 12 periods + a ≤1-week remainder and is
    // refused by UnnameableLayout (spec 004 edge, deferred by ruling).
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

    // P1 review 3 — loud layout validation: any inverted row (the period walk overrunning the
    // year end) or a >1-week adjustment remainder (a 53-week year the engine cannot name) is a
    // CALENDAR_VALIDATION refusal, never silently materialized.
    private static bool UnnameableLayout(CalendarPattern pattern, List<CalendarPeriod> periods)
    {
        if (periods.Any(p => p.StartDate > p.EndDate)) return true;
        if (pattern != CalendarPattern.FourFourFive) return false;
        var adj = periods.LastOrDefault(p => p.IsAdjustment);
        return adj is not null && adj.EndDate.DayNumber - adj.StartDate.DayNumber + 1 > 7;
    }

    private static string LayoutError(int year, DateOnly start, DateOnly end) =>
        $"Fiscal year {year} cannot be laid out as 12 × 4-4-5 periods plus an adjustment remainder of a week or less ({start:yyyy-MM-dd}..{end:yyyy-MM-dd}); the engine is 52-week 4-4-5 only — 53-week configurations are not supported.";
}
