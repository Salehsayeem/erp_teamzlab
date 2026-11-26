using System.Globalization;
using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Payroll runs (ERP-PEOPLE-034/035/037/038/039/046/047/048/049/051/053/055/056, rulings
// R-PEO2/5/6/7/9/10/11). The run is the frozen payroll document: calculation pulls APPROVED time
// inside the period (034) and freezes deterministic lines (038); approval is maker-checker
// (calculator ≠ approver, 048) gated by preflight validations (046); posting writes ONE balanced
// journal Dr 6100 / Cr 2310 / Cr 2300 (053) with FinanceGuards.EnsurePeriodOpen on the run's
// PayDate (R-PEO11) and flips the run atomically in one SaveChanges; reversals post a separately
// balanced compensating journal under a NEW run id linked via ReversalOfRunId (055/049).
public sealed class PayrollService(
    IPeopleRepository people, IJournalRepository journals, IAccountRepository accounts,
    IFinanceSettingRepository settings, INumberSeries numbers, IAuditRepository audit,
    EntityContext ec, ErpDbContext db) : IPayrollService
{
    // the salary expense control account is AddFinanceSeed's ACC-6100 — never a second code
    private const string SalaryExpenseAccount = "6100";
    private const string DeductionsPayableAccount = "2310";
    private const string SalariesPayableAccount = "2300";
    private const string OtMultiplierSetting = "payroll.ot_multiplier";
    private const decimal DefaultOtMultiplier = 1.5m; // R-PEO2
    private const decimal ReconciliationTolerance = 0.01m; // 039 AC2

    private const string BasicCode = "BASIC";
    private const string OvertimeCode = "OVERTIME";

    // ---- catalog (037) ---------------------------------------------------------------

    public object ListPayCodes(CurrentUser user, PayCodeKind? kind) =>
        people.AllPayCodes(kind).Select(ProjectCode).ToList();

    // 037 AC1: a code carries its required accounting mapping BEFORE it can be active — creation
    // without one is refused; the Version column is the hook for later mapping-change revisions
    public (EarningDeductionCode? code, string? errorCode, string? message) CreatePayCode(CurrentUser user, Dtos.CreatePayCodeRequest request)
    {
        if (people.FindPayCode(request.Code) is not null)
            return (null, "PAY_CODE_EXISTS", $"Pay code {request.Code} already exists for this entity.");
        var debit = accounts.FindByCode(request.DebitAccount);
        if (debit is null) return (null, "ACCOUNT_NOT_FOUND", $"Account {request.DebitAccount} is not in the chart of accounts.");
        var credit = accounts.FindByCode(request.CreditAccount);
        if (credit is null) return (null, "ACCOUNT_NOT_FOUND", $"Account {request.CreditAccount} is not in the chart of accounts.");
        if (!debit.DirectPostingAllowed || !credit.DirectPostingAllowed)
            return (null, "ACCOUNT_NOT_POSTABLE", "Pay code mappings must use direct-postable accounts.");

        var code = new EarningDeductionCode(request.Code, ec.TenantId, ec.EntityId, request.Code,
            request.Kind, request.Name, request.CalcBasis, request.RatePct, request.DebitAccount,
            request.CreditAccount, Active: true, DateOnly.FromDateTime(DateTime.UtcNow), Version: 1, user.Id, DateTimeOffset.UtcNow);
        people.AddPayCode(code);
        audit.Log(user, "create", "pay_code", code.Code,
            $"Pay code {code.Code} ({request.Kind}, {request.CalcBasis}, v{code.Version}) mapped {debit.Code}/{credit.Code} — active from {code.EffectiveFrom:yyyy-MM-dd}");
        people.Save();
        return (code, null, null);
    }

    // fix R4-6: a typo'd mapping must be correctable — edits bump the version (037 AC2);
    // posted runs keep their frozen lines, only FUTURE runs consume the new values
    public (EarningDeductionCode? code, string? errorCode, string? message) UpdatePayCode(CurrentUser user, string codeCode, Dtos.UpdatePayCodeRequest request)
    {
        var code = people.FindPayCode(codeCode);
        if (code is null) return (null, "PAY_CODE_NOT_FOUND", $"Pay code {codeCode} not found.");
        if (!code.Active)
            return (null, "PAY_CODE_INACTIVE", $"Pay code {codeCode} is inactive; reactivate-on-edit is not supported in the pilot — create a new code.");
        if (request.CalcBasis is not null && request.CalcBasis is not ("Hours" or "Amount" or "Percent"))
            return (null, "VALIDATION_ERROR", "CalcBasis must be Hours, Amount or Percent.");
        var effectiveRate = request.RatePct ?? code.RatePct;
        var effectiveBasis = request.CalcBasis ?? code.CalcBasis;
        if (effectiveBasis == "Amount" && (effectiveRate is null || effectiveRate < 0))
            return (null, "VALIDATION_ERROR", "Amount-basis codes need a non-negative RatePct.");

        var changed = new List<string>();
        if (request.Name is not null && request.Name != code.Name) changed.Add($"name='{request.Name}'");
        if (request.RatePct is not null && request.RatePct != code.RatePct) changed.Add($"ratePct={request.RatePct}");
        if (request.CalcBasis is not null && request.CalcBasis != code.CalcBasis) changed.Add($"calcBasis={request.CalcBasis}");
        if (changed.Count == 0)
            return (code, null, null); // no-op edit is idempotent, no version churn

        people.UpdatePayCode(code, request.Name, request.RatePct, request.CalcBasis);
        audit.Log(user, "update", "pay_code", code.Code,
            $"Pay code {code.Code} updated to v{code.Version + 1}: {string.Join(", ", changed)} — posted runs keep their frozen lines");
        audit.Emit(user, "people.paycode.updated", new
        {
            code = code.Code, version = code.Version + 1, name = request.Name, ratePct = request.RatePct,
            calcBasis = request.CalcBasis, updatedBy = user.Id,
        });
        people.Save();
        return (people.FindPayCode(codeCode), null, null);
    }

    // fix R4-6: deactivation stops consumption in new calculations immediately; closed runs are untouched
    public (EarningDeductionCode? code, string? errorCode, string? message) DeactivatePayCode(CurrentUser user, string codeCode)
    {
        var code = people.FindPayCode(codeCode);
        if (code is null) return (null, "PAY_CODE_NOT_FOUND", $"Pay code {codeCode} not found.");
        if (!code.Active)
            return (null, "PAY_CODE_ALREADY_INACTIVE", $"Pay code {codeCode} is already inactive.");
        people.SetPayCodeActive(code, false);
        audit.Log(user, "deactivate", "pay_code", code.Code,
            $"Pay code {code.Code} deactivated by {user.Email} — excluded from new calculations; posted runs keep their frozen lines");
        audit.Emit(user, "people.paycode.deactivated", new { code = code.Code, deactivatedBy = user.Id });
        people.Save();
        return (people.FindPayCode(codeCode), null, null);
    }

    // ---- runs (035/049) ---------------------------------------------------------------

    public object ListRuns(CurrentUser user, PayRunStatus? status, string? periodId) =>
        people.AllPayRuns(status, periodId).Select(ProjectRun).ToList();

    // 049 AC1: (tenant, entity, payGroup, period) is the run key — the unique index backs it and
    // a repeat request returns the SAME live run; reversals are exempt (new linked id)
    public (PayRun? run, string? code, string? message) CreateRun(CurrentUser user, Dtos.CreatePayRunRequest request)
    {
        var group = people.FindPayGroup(request.PayGroupId);
        if (group is null) return (null, "PAY_GROUP_NOT_FOUND", $"Pay group {request.PayGroupId} not found.");
        var period = db.FiscalPeriods.FirstOrDefault(p => p.Id == request.PeriodId
            && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        if (period is null) return (null, "PERIOD_NOT_FOUND", $"Fiscal period {request.PeriodId} not found.");
        if (period.Status != PeriodStatus.Open)
            return (null, "PERIOD_LOCKED", $"Fiscal period {period.Name} is {period.Status}; runs anchor to Open periods only (R-PEO5).");
        if (request.PayDate < period.StartDate || request.PayDate > period.EndDate)
            return (null, "VALIDATION_ERROR",
                $"PayDate {request.PayDate:yyyy-MM-dd} must fall inside period {period.Name} ({period.StartDate:yyyy-MM-dd}..{period.EndDate:yyyy-MM-dd}); the run anchors to the period via its PayDate (R-PEO5).");

        var existing = people.FindLiveRun(group.Id, period.Id);
        if (existing is not null)
            return (existing, null, null);

        var run = new PayRun(numbers.Next("PRN"), ec.TenantId, ec.EntityId, group.Id, period.Id,
            PayRunStatus.Open, EntityContext.SeedEntity.Currency, request.PayDate, 0, 0, 0,
            CalculatedBy: null, CalculatedAt: null, ApprovedBy: null, ApprovedAt: null,
            JournalId: null, PostedBy: null, PostedAt: null, ReversalOfRunId: null,
            ReversalReason: null, FundsReleased: null, user.Id, DateTimeOffset.UtcNow);
        people.AddPayRun(run);
        audit.Log(user, "create", "pay_run", run.Id,
            $"Pay run {run.Id} opened for group {group.Code}, period {period.Name}, pay date {request.PayDate:yyyy-MM-dd}");
        people.Save();
        return (run, null, null);
    }

    // ---- calculation (034/038/039) ----------------------------------------------------

    // re-calculation is allowed while Open/Calculated; once Approved the snapshot is frozen (048)
    public (PayRun? run, string? code, string? message) Calculate(CurrentUser user, string runId)
    {
        var run = people.FindPayRun(runId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");
        if (run.Status is not (PayRunStatus.Open or PayRunStatus.Calculated))
            return (null, "PAYRUN_STATE_ERROR",
                $"Run {runId} is {run.Status}; recalculation would invalidate the approval snapshot (048 AC2). Late corrections wait for the next period (R-PEO10).");

        var period = FiscalPeriodOf(run);
        var population = people.WorkersInGroup(run.PayGroupId, period.StartDate, period.EndDate);
        var populationIds = population.Select(w => w.Id).ToList();
        var approved = people.TimeInWindow(period.StartDate, period.EndDate, TimeEntryStatus.Approved, populationIds)
            .GroupBy(t => t.EmploymentId).ToDictionary(g => g.Key, g => g.ToList());

        var basic = people.FindPayCode(BasicCode);
        var overtime = people.FindPayCode(OvertimeCode);
        var multiplier = OtMultiplier();
        var lines = new List<PayRunLine>();
        var excluded = new List<string>();

        foreach (var worker in population)
        {
            // fix R4-1: the compensation reference date is the worker's window end — for a
            // mid-period leaver that is their termination date, so terms covering the days they
            // actually worked still resolve (as-of period end would miss them)
            var windowEnd = WindowEnd(worker, period);
            approved.TryGetValue(worker.Id, out var entries);
            var comp = people.ApplicableCompensation(worker.Id, windowEnd); // 024 AC1: as-of the worker's window end
            if (comp is null)
            {
                excluded.Add(worker.Id); // surfaces in validations (046 + EXCLUDED_WITH_TIME) — never silent
                continue;
            }
            lines.AddRange(BuildEarningLines(worker, comp, entries ?? [], basic, overtime, multiplier,
                WindowStart(worker, period), windowEnd, period));
        }

        // every active deduction code applies to each worker's gross (037; R-PEO7 flat catalog)
        var deductions = people.AllPayCodes(PayCodeKind.Deduction).Where(c => c.Active && c.EffectiveFrom <= period.EndDate)
            .OrderBy(c => c.Code).ToList();
        foreach (var worker in population.Where(w => !excluded.Contains(w.Id)))
        {
            var gross = lines.Where(l => l.EmploymentId == worker.Id && l.Kind == PayCodeKind.Earning).Sum(l => l.Amount);
            if (gross <= 0) continue;
            foreach (var code in deductions)
                lines.Add(BuildDeductionLine(worker, code, gross, run.Currency));
        }

        lines = lines.OrderBy(l => l.EmploymentId).ThenBy(l => l.PayCode)
            .ThenBy(l => l.SourceEntryId ?? string.Empty).ToList();
        var grossTotal = decimal.Round(lines.Where(l => l.Kind == PayCodeKind.Earning).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        var deductionTotal = decimal.Round(lines.Where(l => l.Kind == PayCodeKind.Deduction).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        var netTotal = decimal.Round(grossTotal - deductionTotal, 2, MidpointRounding.AwayFromZero);

        // 039 AC2: gross − deductions = net must reconcile to the lines before anything freezes
        if (Math.Abs(grossTotal - deductionTotal - netTotal) > ReconciliationTolerance)
            return (null, "PAYROLL_RECONCILIATION_BROKEN",
                $"Calculation does not reconcile: gross {grossTotal:0.00} − deductions {deductionTotal:0.00} ≠ net {netTotal:0.00}.");

        people.ReplaceLines(run, lines);
        people.MarkCalculated(run, grossTotal, deductionTotal, netTotal, user.Id, DateTimeOffset.UtcNow);
        audit.Log(user, "calculate", "pay_run", runId,
            $"Pay run {runId} calculated: {lines.Count} line(s), gross {grossTotal:0.00}, deductions {deductionTotal:0.00}, net {netTotal:0.00} " +
            $"for {population.Count - excluded.Count}/{population.Count} worker(s)");
        audit.Emit(user, "people.payrun.calculated", new
        {
            runId, periodId = run.PeriodId, gross = grossTotal, deductions = deductionTotal, net = netTotal,
            workerCount = population.Count - excluded.Count, excludedCount = excluded.Count, lineCount = lines.Count,
        });
        people.Save();
        return (people.FindPayRun(runId), null, null);
    }

    // 038 AC2: every line identifies source input, code, basis quantity, rate, amount, currency.
    // fix R4-2: the monthly salary line carries the proration formula on the line itself —
    // Hours = worked calendar days in [max(period start, hire), min(period end, termination)],
    // Rate = salary / period days, Amount = salary × worked / period days.
    private List<PayRunLine> BuildEarningLines(Employment worker, CompensationEvent comp,
        List<TimeEntry> entries, EarningDeductionCode? basic, EarningDeductionCode? overtime,
        decimal multiplier, DateOnly windowStart, DateOnly windowEnd, FiscalPeriod period)
    {
        var lines = new List<PayRunLine>();
        if (comp.PayBasis == "Hourly")
        {
            foreach (var entry in entries)
            {
                if (entry.Hours > 0 && basic is not null)
                    lines.Add(new PayRunLine(worker.Id, basic.Id, basic.Code, PayCodeKind.Earning,
                        entry.Hours, comp.Amount, decimal.Round(entry.Hours * comp.Amount, 2, MidpointRounding.AwayFromZero),
                        comp.Currency, entry.Id));
                if (entry.OvertimeHours > 0 && overtime is not null)
                {
                    var otRate = decimal.Round(comp.Amount * multiplier, 4, MidpointRounding.AwayFromZero);
                    lines.Add(new PayRunLine(worker.Id, overtime.Id, overtime.Code, PayCodeKind.Earning,
                        entry.OvertimeHours, otRate, decimal.Round(entry.OvertimeHours * otRate, 2, MidpointRounding.AwayFromZero),
                        comp.Currency, entry.Id));
                }
            }
        }
        else // Monthly: the period salary prorates by calendar days in the worker's window (fix R4-2)
        {
            var periodDays = period.EndDate.DayNumber - period.StartDate.DayNumber + 1;
            var workedDays = windowEnd.DayNumber - windowStart.DayNumber + 1;
            if (basic is not null && workedDays > 0)
            {
                var salary = decimal.Round(comp.Amount * workedDays / periodDays, 2, MidpointRounding.AwayFromZero);
                var dailyRate = decimal.Round(comp.Amount / periodDays, 4, MidpointRounding.AwayFromZero);
                lines.Add(new PayRunLine(worker.Id, basic.Id, basic.Code, PayCodeKind.Earning,
                    workedDays, dailyRate, salary, comp.Currency, null));
            }
            var otHours = entries.Sum(e => e.OvertimeHours);
            if (otHours > 0 && overtime is not null)
            {
                var equivalent = decimal.Round(comp.Amount / (worker.WeeklyPlannedHours * 4m), 4, MidpointRounding.AwayFromZero);
                var otRate = decimal.Round(equivalent * multiplier, 4, MidpointRounding.AwayFromZero);
                lines.Add(new PayRunLine(worker.Id, overtime.Id, overtime.Code, PayCodeKind.Earning,
                    otHours, otRate, decimal.Round(otHours * otRate, 2, MidpointRounding.AwayFromZero), comp.Currency, null));
            }
        }
        return lines;
    }

    private static PayRunLine BuildDeductionLine(Employment worker, EarningDeductionCode code, decimal gross, string currency) =>
        code.CalcBasis == "Percent"
            ? new PayRunLine(worker.Id, code.Id, code.Code, PayCodeKind.Deduction, 0, code.RatePct ?? 0,
                decimal.Round(gross * (code.RatePct ?? 0) / 100m, 2, MidpointRounding.AwayFromZero), currency, null)
            : new PayRunLine(worker.Id, code.Id, code.Code, PayCodeKind.Deduction, 1m, code.RatePct ?? 0,
                decimal.Round(code.RatePct ?? 0, 2, MidpointRounding.AwayFromZero), currency, null);

    // R-PEO2: the flat multiplier is finance data (finance_settings), defaulting to 1.5
    private decimal OtMultiplier() =>
        decimal.TryParse(settings.Get(OtMultiplierSetting)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value > 0 ? value : DefaultOtMultiplier;

    // ---- preflight validations (046) --------------------------------------------------

    public (object? data, string? code, string? message) Validations(CurrentUser user, string runId)
    {
        var run = people.FindPayRun(runId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");
        var period = FiscalPeriodOf(run);
        var population = people.WorkersInGroup(run.PayGroupId, period.StartDate, period.EndDate);

        var blocking = BlockingValidations(run, period, population);
        var warnings = Warnings(run, period, population);

        return (new { runId, status = run.Status.ToString(), canFinalize = blocking.Count == 0, blocking, warnings },
            null, null);
    }

    // shared by the dashboard and the approval gate — blockers always block both identically
    private List<object> BlockingValidations(PayRun run)
    {
        var period = FiscalPeriodOf(run);
        return BlockingValidations(run, period, people.WorkersInGroup(run.PayGroupId, period.StartDate, period.EndDate));
    }

    private List<object> BlockingValidations(PayRun run, FiscalPeriod period, List<Employment> population)
    {
        var populationIds = population.Select(w => w.Id).ToList();
        var blocking = new List<object>();

        if (period.Status != PeriodStatus.Open)
            blocking.Add(new { code = "PERIOD_LOCKED", workerCount = 0,
                message = $"Fiscal period {period.Name} is {period.Status}; posting is locked until it reopens.",
                ownerRole = "periods:write" });
        if (population.Count == 0)
            blocking.Add(new { code = "EMPTY_POPULATION", workerCount = 0,
                message = $"Pay group {run.PayGroupId} has no active members; nothing to pay.",
                ownerRole = "payroll:calculate" });

        var pending = people.TimeInWindow(period.StartDate, period.EndDate, TimeEntryStatus.Pending, populationIds);
        if (pending.Count > 0)
        {
            var names = string.Join(", ", pending.Select(t => t.EmploymentId).Distinct());
            blocking.Add(new { code = "UNAPPROVED_TIME", workerCount = pending.Select(t => t.EmploymentId).Distinct().Count(),
                message = $"{pending.Count} unapproved time entr(y/ies) for worker(s) {names} must be approved before finalization (034 AC1).",
                ownerRole = "time:approve" });
        }

        // PM6 (041 AC2): when a mandatory election is configured, payroll selects the VERIFIED
        // election effective at the pay date — a missing one is a blocking REVIEW item, never a
        // silent default. Config rides finance_settings (data, not constants).
        if (db.FinanceSettings.FirstOrDefault(s => s.Id == "FS-payroll.mandatory_election")?.Value == "true")
        {
            var kind = db.FinanceSettings.FirstOrDefault(s => s.Id == "FS-payroll.election_kind")?.Value;
            var missing = population.Where(w => !db.TaxElections.Any(e => e.TenantId == ec.TenantId
                && e.LegalEntityId == ec.EntityId && e.EmploymentId == w.Id && e.Status == ElectionStatus.Verified
                && (kind == null || e.Kind == kind)
                && e.EffectiveFrom <= run.PayDate
                && (e.EffectiveTo == null || e.EffectiveTo >= run.PayDate))).ToList();
            if (missing.Count > 0)
                blocking.Add(new { code = "ELECTION_MISSING", workerCount = missing.Count,
                    message = $"Missing verified election '{kind ?? "(any kind)"}' effective at the pay date for worker(s) {string.Join(", ", missing.Select(w => w.Id))} — routes to review, never a silent default (041 AC2).",
                    ownerRole = "payinputs:write" });
        }

        var missingComp = population.Where(w => people.ApplicableCompensation(w.Id, WindowEnd(w, period)) is null).ToList();
        if (missingComp.Count > 0)
            blocking.Add(new { code = "WORKER_NO_COMPENSATION", workerCount = missingComp.Count,
                message = $"{missingComp.Count} worker(s) without compensation effective at their period-window end: {string.Join(", ", missingComp.Select(w => $"{w.Id} ({w.FullName})"))}.",
                ownerRole = "workers:write" });

        // mapping gates (053 AC2): required earning codes plus every deduction code
        var hasHours = people.TimeInWindow(period.StartDate, period.EndDate, TimeEntryStatus.Approved, populationIds);
        CheckPayCode(people.FindPayCode(BasicCode), hasHours.Any(t => t.Hours > 0), BasicCode, populationIds, blocking);
        CheckPayCode(people.FindPayCode(OvertimeCode), hasHours.Any(t => t.OvertimeHours > 0), OvertimeCode, populationIds, blocking);
        foreach (var deduction in people.AllPayCodes(PayCodeKind.Deduction).Where(c => c.Active && c.EffectiveFrom <= period.EndDate))
            CheckPayCode(deduction, required: true, deduction.Code, populationIds, blocking);
        return blocking;
    }

    private List<object> Warnings(PayRun run, FiscalPeriod period, List<Employment> population)
    {
        var warnings = new List<object>();
        var missingCompIds = population.Where(w => people.ApplicableCompensation(w.Id, WindowEnd(w, period)) is null)
            .Select(w => w.Id).ToHashSet();
        var zeroPay = population.Where(w => !missingCompIds.Contains(w.Id) && !run.Lines.Any(l => l.EmploymentId == w.Id)).ToList();
        if (zeroPay.Count > 0)
            warnings.Add(new { code = "ZERO_PAY_WORKERS", workerCount = zeroPay.Count,
                message = $"{zeroPay.Count} worker(s) with no payable lines (zero-pay): {string.Join(", ", zeroPay.Select(w => w.Id))}." });

        // fix R4-1: a worker whose approved in-window time earned NO lines is surfaced loudly —
        // exclusion for any reason is never silent
        var populationIds = population.Select(w => w.Id).ToList();
        var withTime = people.TimeInWindow(period.StartDate, period.EndDate, TimeEntryStatus.Approved, populationIds)
            .Select(t => t.EmploymentId).Distinct();
        var excludedWithTime = withTime
            .Where(id => !run.Lines.Any(l => l.EmploymentId == id && l.Kind == PayCodeKind.Earning)).ToList();
        if (excludedWithTime.Count > 0)
            warnings.Add(new { code = "EXCLUDED_WITH_TIME", workerCount = excludedWithTime.Count,
                message = $"Worker(s) with approved in-window time earned no pay lines: {string.Join(", ", excludedWithTime)} " +
                    "— their compensation does not cover the days worked; resolve before relying on this run." });
        return warnings;
    }

    // a worker's payable window inside the run period: hire date raises the start, termination lowers the end
    private static DateOnly WindowStart(Employment w, FiscalPeriod period) =>
        w.HireDate > period.StartDate ? w.HireDate : period.StartDate;

    private static DateOnly WindowEnd(Employment w, FiscalPeriod period) =>
        w.Status == EmploymentStatus.Terminated && w.TerminationDate is { } t && t < period.EndDate ? t : period.EndDate;

    private void CheckPayCode(EarningDeductionCode? code, bool required, string label, List<string> populationIds, List<object> blocking)
    {
        if (code is null)
        {
            if (required)
                blocking.Add(new { code = "PAY_CODE_MISSING", workerCount = populationIds.Count,
                    message = $"Required earning code {label} is not configured in the catalog (037).", ownerRole = "paycodes:write" });
            return;
        }
        var problem = !code.Active ? "is inactive"
            : accounts.FindByCode(code.DebitAccount) is null ? $"is mapped to missing debit account {code.DebitAccount}"
            : accounts.FindByCode(code.CreditAccount) is null ? $"is mapped to missing credit account {code.CreditAccount}"
            : null;
        if (problem is not null)
            blocking.Add(new { code = "PAYROLL_MAPPING_MISSING", workerCount = populationIds.Count,
                message = $"Pay code {code.Code} {problem} — posting would fail (053 AC2).", ownerRole = "paycodes:write" });
    }

    private FiscalPeriod FiscalPeriodOf(PayRun run) =>
        db.FiscalPeriods.First(p => p.Id == run.PeriodId && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);

    // ---- preview (047) ------------------------------------------------------------------

    public (object? data, string? code, string? message) Preview(CurrentUser user, string runId)
    {
        var run = people.FindPayRun(runId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");
        if (run.Status is PayRunStatus.Open)
            return (null, "PAYRUN_NOT_CALCULATED", $"Run {runId} has no calculated lines yet; run a calculation first (047).");

        var period = FiscalPeriodOf(run);
        var population = people.WorkersInGroup(run.PayGroupId, period.StartDate, period.EndDate).ToDictionary(w => w.Id);
        var lines = run.Lines.OrderBy(l => l.EmploymentId).ThenBy(l => l.PayCode)
            .ThenBy(l => l.SourceEntryId ?? string.Empty).ToList();

        var workers = lines.GroupBy(l => l.EmploymentId).Select(g => new
        {
            employmentId = g.Key,
            workerNo = population.TryGetValue(g.Key, out var w) ? w.WorkerNo : g.Key,
            name = population.TryGetValue(g.Key, out var w2) ? w2.FullName : g.Key,
            gross = decimal.Round(g.Where(l => l.Kind == PayCodeKind.Earning).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero),
            deductions = decimal.Round(g.Where(l => l.Kind == PayCodeKind.Deduction).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero),
            net = decimal.Round(g.Sum(l => l.Kind == PayCodeKind.Earning ? l.Amount : -l.Amount), 2, MidpointRounding.AwayFromZero),
            flags = g.Sum(l => l.Amount) == 0 ? new[] { "zero-pay" } : Array.Empty<string>(),
            excluded = false,
        }).ToList();
        foreach (var missing in population.Values.Where(w => !lines.Any(l => l.EmploymentId == w.Id)))
            workers.Add(new
            {
                employmentId = missing.Id, workerNo = missing.WorkerNo, name = missing.FullName,
                gross = 0m, deductions = 0m, net = 0m, flags = new[] { "excluded" }, excluded = true,
            });

        var lineGross = decimal.Round(lines.Where(l => l.Kind == PayCodeKind.Earning).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        var lineDed = decimal.Round(lines.Where(l => l.Kind == PayCodeKind.Deduction).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        var reconciles = Math.Abs(lineGross - run.GrossTotal) <= ReconciliationTolerance
            && Math.Abs(lineDed - run.DeductionTotal) <= ReconciliationTolerance
            && Math.Abs(run.GrossTotal - run.DeductionTotal - run.NetTotal) <= ReconciliationTolerance;

        return (new
        {
            runId, status = run.Status.ToString(), payGroupId = run.PayGroupId, periodId = run.PeriodId,
            periodName = period.Name, payDate = run.PayDate, currency = run.Currency,
            grossTotal = run.GrossTotal, deductionTotal = run.DeductionTotal, netTotal = run.NetTotal,
            reconciles, workers,
            lines = lines.Select(l => new
            {
                l.EmploymentId, l.PayCode, kind = l.Kind.ToString(), l.Hours, l.Rate, l.Amount, l.Currency, l.SourceEntryId,
            }),
        }, null, null);
    }

    // ---- approval (048) -----------------------------------------------------------------

    public (PayRun? run, string? code, string? message) Approve(CurrentUser approver, string runId, string? reason)
    {
        var run = people.FindPayRun(runId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");
        if (!approver.Has("payroll:approve")) return (null, "FORBIDDEN", "payroll:approve permission required.");
        if (run.Status != PayRunStatus.Calculated)
            return (null, "PAYRUN_STATE_ERROR",
                run.Status == PayRunStatus.Approved || run.Status == PayRunStatus.Posted
                    ? $"Run {runId} is already {run.Status}; a second approval would double-apply the snapshot (048)."
                    : $"Only calculated runs can be approved (current: {run.Status}).");
        if (run.CalculatedBy == approver.Id)
            return (null, "PAYROLL_APPROVAL_ERROR",
                "Maker-checker: the calculator cannot approve their own run (048 AC1).");

        var blockers = BlockingValidations(run);
        if (blockers.Count > 0)
            return (null, "PAYROLL_VALIDATION_BLOCKED",
                $"Run {runId} has {blockers.Count} blocking validation(s); open GET /pay-runs/{runId}/validations (046).");

        people.MarkApproved(run, approver.Id, DateTimeOffset.UtcNow); // snapshot freezes here (048)
        audit.Log(approver, "approve", "pay_run", runId,
            $"Pay run {runId} approved: gross {run.GrossTotal:0.00}, deductions {run.DeductionTotal:0.00}, net {run.NetTotal:0.00}" +
            (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}"));
        audit.Emit(approver, "people.payrun.approved", new
        {
            runId, approver = approver.Id, reason, gross = run.GrossTotal, deductions = run.DeductionTotal, net = run.NetTotal,
        });
        try
        {
            people.Save();
        }
        catch (DbUpdateConcurrencyException)
        {
            // review-1a mechanism: a concurrent approval won the guarded update; nothing commits
            return (null, "PAYRUN_ALREADY_APPROVED",
                $"Run {runId} was already approved by another officer; the retry carried a stale snapshot and wrote nothing.");
        }
        return (run, null, null);
    }

    // ---- posting (053/049) ----------------------------------------------------------------

    public (object? data, string? code, string? message) Post(CurrentUser approver, string runId)
    {
        var run = people.FindPayRun(runId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");
        if (!approver.Has("payroll:approve")) return (null, "FORBIDDEN", "payroll:approve permission required.");
        if (run.Status == PayRunStatus.Posted)
            return (null, "PAYRUN_ALREADY_POSTED", $"Run {runId} is already posted (journal {run.JournalId}); a retry cannot post twice (049 AC2).");
        if (run.Status is PayRunStatus.Reversed)
            return (null, "PAYRUN_ALREADY_REVERSED", $"Run {runId} is reversed; posting is closed.");
        if (run.Status != PayRunStatus.Approved)
            return (null, "PAYRUN_STATE_ERROR", $"Only approved runs can post (current: {run.Status}).");

        // R-PEO11: the period guard evaluates the run's entered PayDate — FIRST of the posting checks
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, run.PayDate);
        if (guardCode is not null) return (null, guardCode, guardMsg);

        var mappingError = EnsureRunMappings(run);
        if (mappingError is not null) return (null, mappingError.Value.code, mappingError.Value.message);

        // R4 minor: an all-zero run records WITHOUT a journal — no 0/0 line in the ledger
        if (run.GrossTotal == 0m && run.DeductionTotal == 0m && run.NetTotal == 0m)
        {
            people.MarkPosted(run, journalId: null, approver.Id, DateTimeOffset.UtcNow);
            audit.Log(approver, "post", "pay_run", run.Id,
                $"Pay run {run.Id} posted with zero totals; no journal written (0/0 entries are not recorded)");
            audit.Emit(approver, "people.payrun.posted", new
            {
                runId = run.Id, journalId = (string?)null, periodId = run.PeriodId, payDate = run.PayDate,
                gross = run.GrossTotal, deductions = run.DeductionTotal, net = run.NetTotal, currency = run.Currency, zeroPay = true,
            });
            try
            {
                people.Save();
            }
            catch (DbUpdateConcurrencyException)
            {
                return (null, "PAYRUN_ALREADY_POSTED",
                    $"Run {runId} was already posted by another officer; the retry carried a stale snapshot and wrote nothing.");
            }
            return (new { run = ProjectRun(people.FindPayRun(runId)!), journal = (object?)null }, null, null);
        }

        // PM8 (053 AC2): effective-dated account mappings resolve the posting legs — an INACTIVE
        // mapping covering the pay date BLOCKS and names the element; no mapping → landed constants
        var (mappedAccounts, resolvedMappingError) = PayrollCloseService.ResolveAccounts(db, ec, run.PayDate);
        if (resolvedMappingError is not null) return (null, resolvedMappingError.Value.code, resolvedMappingError.Value.message);
        var (salaryAccount, deductionsAccount, netAccount) = mappedAccounts!.Value;

        // 053 AC1: ONE balanced journal — Dr salary gross / Cr deductions payable / Cr net payable,
        // zero-value legs omitted when a total is empty
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
            $"Payroll run {run.Id} for period {run.PeriodId}",
            PayrollJournal(salaryAccount, deductionsAccount, netAccount, run.GrossTotal, run.DeductionTotal, run.NetTotal),
            DateTimeOffset.UtcNow, approver.Id, reversalOfId: null, accountingDate: run.PayDate);
        journals.Add(journal);

        people.MarkPosted(run, journal.Id, approver.Id, DateTimeOffset.UtcNow);
        audit.Log(approver, "post", "pay_run", run.Id,
            $"Pay run {run.Id} posted: journal {journal.Id} Dr {salaryAccount} {run.GrossTotal:0.00} / " +
            $"Cr {deductionsAccount} {run.DeductionTotal:0.00} + Cr {netAccount} {run.NetTotal:0.00}");
        // 053: the FINANCE handoff payload rides the outbox event
        audit.Emit(approver, "people.payrun.posted", new
        {
            runId = run.Id, journalId = journal.Id, periodId = run.PeriodId, payDate = run.PayDate,
            gross = run.GrossTotal, deductions = run.DeductionTotal, net = run.NetTotal, currency = run.Currency,
        });
        try
        {
            people.Save(); // journal + run flip + audit + outbox in ONE SaveChanges (049 AC1)
        }
        catch (DbUpdateConcurrencyException)
        {
            // review-1a mechanism: the guarded Approved→Posted UPDATE matched zero rows — a
            // concurrent poster won; nothing above commits, so no duplicate journal exists
            return (null, "PAYRUN_ALREADY_POSTED",
                $"Run {runId} was already posted by another officer; the retry carried a stale snapshot and wrote nothing.");
        }
        var posted = people.FindPayRun(runId)!;
        return (new { run = ProjectRun(posted), journal }, null, null);
    }

    // 053 AC2: missing/inactive mapping on any line blocks posting and names the lines
    private (string code, string message)? EnsureRunMappings(PayRun run)
    {
        var problems = new List<string>();
        foreach (var group in run.Lines.GroupBy(l => l.PayCodeId))
        {
            var code = people.FindPayCodeById(group.Key);
            if (code is null || !code.Active)
            {
                problems.Add($"{code?.Code ?? group.Key} (lines {string.Join(", ", group.Select(l => l.PayCode))} for worker(s) {string.Join(", ", group.Select(l => l.EmploymentId).Distinct())}) is missing or inactive");
                continue;
            }
            var debit = accounts.FindByCode(code.DebitAccount);
            var credit = accounts.FindByCode(code.CreditAccount);
            if (debit is null || credit is null || !debit.DirectPostingAllowed || !credit.DirectPostingAllowed)
                problems.Add($"{code.Code} (lines for worker(s) {string.Join(", ", group.Select(l => l.EmploymentId).Distinct())}) maps to missing/non-postable accounts {code.DebitAccount}/{code.CreditAccount}");
        }
        return problems.Count == 0
            ? null
            : (("PAYROLL_MAPPING_MISSING", $"Inactive or missing account mapping blocks posting: {string.Join("; ", problems)}."));
    }

    private static JournalLine[] PayrollJournal(string salaryAccount, string deductionsAccount, string netAccount,
        decimal gross, decimal deductions, decimal net) =>
    [
        new JournalLine(salaryAccount, "Salaries and Wages Expense", gross, 0),
        .. deductions != 0 ? new[] { new JournalLine(deductionsAccount, "Payroll Deductions Payable", 0, deductions) } : [],
        .. net != 0 ? new[] { new JournalLine(netAccount, "Salaries Payable", 0, net) } : [],
    ];

    // ---- payslips (051) -------------------------------------------------------------------

    public (object? data, string? code, string? message) Payslip(CurrentUser user, string runId, string workerId)
    {
        var run = people.FindPayRun(runId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");
        if (run.Status is PayRunStatus.Open or PayRunStatus.Calculated)
            return (null, "PAYRUN_NOT_APPROVED", $"Payslips exist only for approved results (run {runId} is {run.Status}; 051).");
        var worker = people.FindWorker(workerId);
        if (worker is null) return (null, "WORKER_NOT_FOUND", "Worker not found.");

        // 051 AC2 scope filter: a worker outside the run's approved population has no slip here
        var lines = run.Lines.Where(l => l.EmploymentId == workerId)
            .OrderBy(l => l.PayCode).ThenBy(l => l.SourceEntryId ?? string.Empty).ToList();
        if (lines.Count == 0) return (null, "PAYSLIP_NOT_FOUND", $"Worker {workerId} has no approved lines in run {runId}.");

        var period = FiscalPeriodOf(run);
        var gross = decimal.Round(lines.Where(l => l.Kind == PayCodeKind.Earning).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        var ded = decimal.Round(lines.Where(l => l.Kind == PayCodeKind.Deduction).Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        var reversalRun = run.Status == PayRunStatus.Reversed
            ? people.AllPayRuns(null, null).FirstOrDefault(r => r.ReversalOfRunId == run.Id)
            : null; // 056: the reversal is shown as a separate linked entry, never by rewriting this slip's history
        return (new
        {
            runId, workerId, workerNo = worker.WorkerNo, name = worker.FullName,
            period = new { id = period.Id, name = period.Name, startDate = period.StartDate, endDate = period.EndDate },
            payDate = run.PayDate, currency = run.Currency, gross, deductions = ded, net = decimal.Round(gross - ded, 2, MidpointRounding.AwayFromZero),
            runStatus = run.Status.ToString(),
            reversed = run.Status == PayRunStatus.Reversed, // R4 minor: an explicit REVERSED marker on the slip
            reversalRunId = reversalRun?.Id,
            lines,
        }, null, null);
    }

    // ---- reversal (055/049) -----------------------------------------------------------------

    public (object? data, string? code, string? message) Reverse(CurrentUser approver, string runId, Dtos.ReversePayRunRequest request)
    {
        var run = people.FindPayRun(runId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.FundsReleased is null)
            return (null, "VALIDATION_ERROR", "A reversal requires both a Reason and the FundsReleased flag (055 AC2).");
        if (!approver.Has("payroll:approve")) return (null, "FORBIDDEN", "payroll:approve permission required.");
        if (run.Status == PayRunStatus.Reversed)
            return (null, "PAYRUN_ALREADY_REVERSED", $"Run {runId} is already reversed; a retry cannot reverse it twice.");
        if (run.Status != PayRunStatus.Posted)
            return (null, "PAYRUN_STATE_ERROR", $"Only posted runs can be reversed (current: {run.Status}).");

        // 055 AC2: the compensating posting needs a reason and the funds-released flag
        var payDate = DateOnly.FromDateTime(DateTime.UtcNow); // the reversal posts at today (R9d precedent)
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, payDate); // R-PEO11 on every posting path
        if (guardCode is not null) return (null, guardCode, guardMsg);

        // PM8: the reversal's legs resolve through the same mapping gate as the original posting
        var (mappedAccounts, mappingError) = PayrollCloseService.ResolveAccounts(db, ec, payDate);
        if (mappingError is not null) return (null, mappingError.Value.code, mappingError.Value.message);
        var (salaryAccount, deductionsAccount, netAccount) = mappedAccounts!.Value;

        var reversal = new PayRun(numbers.Next("PRN"), ec.TenantId, ec.EntityId, run.PayGroupId, run.PeriodId,
            PayRunStatus.Reversed, run.Currency, payDate, run.GrossTotal, run.DeductionTotal, run.NetTotal,
            CalculatedBy: approver.Id, CalculatedAt: DateTimeOffset.UtcNow, ApprovedBy: null, ApprovedAt: null,
            JournalId: null, PostedBy: null, PostedAt: null, ReversalOfRunId: run.Id,
            ReversalReason: request.Reason, FundsReleased: request.FundsReleased, approver.Id, DateTimeOffset.UtcNow)
        {
            // frozen COPY of the lines being reversed (055) — fresh instances, because the
            // originals are tracked owned rows of the original run and cannot be re-parented
            Lines = run.Lines.Select(l => new PayRunLine(l.EmploymentId, l.PayCodeId, l.PayCode, l.Kind,
                l.Hours, l.Rate, l.Amount, l.Currency, l.SourceEntryId)).ToList(),
        };
        people.AddPayRun(reversal);

        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
            $"Reversal of pay run {run.Id}",
            ReversalJournal(salaryAccount, deductionsAccount, netAccount, run.GrossTotal, run.DeductionTotal, run.NetTotal),
            DateTimeOffset.UtcNow, approver.Id, reversalOfId: run.JournalId, accountingDate: payDate);
        journals.Add(journal);

        people.MarkReversed(run, request.Reason, request.FundsReleased!.Value);
        audit.Log(approver, "reverse", "pay_run", run.Id,
            $"Pay run {run.Id} reversed by {approver.Email} via correction run {reversal.Id}: journal {journal.Id} — {request.Reason}");
        audit.Emit(approver, "people.payrun.reversed", new
        {
            originalRunId = run.Id, reversalRunId = reversal.Id, journalId = journal.Id,
            gross = run.GrossTotal, deductions = run.DeductionTotal, net = run.NetTotal, currency = run.Currency,
            fundsReleased = request.FundsReleased, reason = request.Reason,
        });
        try
        {
            people.Save();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, "PAYRUN_ALREADY_REVERSED",
                $"Run {runId} was already reversed by another officer; the retry carried a stale snapshot and wrote nothing.");
        }
        return (new { run = ProjectRun(people.FindPayRun(reversal.Id)!), journal }, null, null);
    }

    // the compensating entry: Dr net payable / Dr deductions payable / Cr salary expense (055 AC1)
    private static JournalLine[] ReversalJournal(string salaryAccount, string deductionsAccount, string netAccount,
        decimal gross, decimal deductions, decimal net) =>
    [
        .. net != 0 ? new[] { new JournalLine(netAccount, "Salaries Payable", net, 0) } : [],
        .. deductions != 0 ? new[] { new JournalLine(deductionsAccount, "Payroll Deductions Payable", deductions, 0) } : [],
        new JournalLine(salaryAccount, "Salaries and Wages Expense", 0, gross),
    ];

    private static object ProjectRun(PayRun r) => new
    {
        r.Id, r.PayGroupId, r.PeriodId, status = r.Status.ToString(), r.Currency, r.PayDate,
        r.GrossTotal, r.DeductionTotal, r.NetTotal, r.CalculatedBy, r.CalculatedAt,
        r.ApprovedBy, r.ApprovedAt, r.JournalId, r.PostedBy, r.PostedAt,
        r.ReversalOfRunId, r.ReversalReason, r.FundsReleased, r.CreatedBy, r.CreatedAt,
        lineCount = r.Lines.Count,
    };

    private static object ProjectCode(EarningDeductionCode c) => new
    {
        c.Id, c.Code, kind = c.Kind.ToString(), c.Name, c.CalcBasis, c.RatePct,
        c.DebitAccount, c.CreditAccount, c.Active, c.EffectiveFrom, c.Version, c.CreatedBy, c.CreatedAt,
    };
}
