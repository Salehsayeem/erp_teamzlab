using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// PM6 — gross-to-net inputs (ERP-PEOPLE-041/042/043/044): versioned elections with explicit
// verification, effective-dated contribution rules reconciling to run output, garnishment
// orders with period/SoD/conflict gates, benefit enrollments emitting versioned deduction
// instructions whose provider-feed reconciliation flags WITHOUT overwriting.
public sealed class PayrollInputsService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit,
    IPeopleRepository people) : IPayrollInputsService
{
    private const string MandatoryElectionKey = "payroll.mandatory_election";
    private const string ElectionKindKey = "payroll.election_kind";

    public object ListTaxElections(CurrentUser user, string? employmentId) =>
        db.TaxElections.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId)
            .Where(e => employmentId == null || e.EmploymentId == employmentId)
            .OrderBy(e => e.Id).ToList().Select(ProjectElection).ToList();

    public (TaxElection? election, string? code, string? message) CreateTaxElection(CurrentUser user, Dtos.CreateTaxElectionRequest request)
    {
        if (!db.Employments.Any(w => w.Id == request.EmploymentId
                && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId))
            return (null, "WORKER_NOT_FOUND", "Worker not found.");
        var election = new TaxElection(numbers.Next("TXE"), ec.TenantId, ec.EntityId, request.EmploymentId,
            request.Kind, request.Value, ElectionStatus.Draft, request.EffectiveFrom, request.EffectiveTo,
            null, null, user.Id, DateTimeOffset.UtcNow);
        db.TaxElections.Add(election);
        audit.Log(user, "create", "tax_election", election.Id,
            $"Tax election {request.Kind} for {request.EmploymentId} from {request.EffectiveFrom:yyyy-MM-dd} (Draft — verification is explicit)");
        db.SaveChanges();
        return (election, null, null);
    }

    public (TaxElection? election, string? code, string? message) VerifyElection(CurrentUser user, string electionId)
    {
        var election = db.TaxElections.FirstOrDefault(e => e.Id == electionId
            && e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);
        if (election is null) return (null, "ELECTION_NOT_FOUND", "Tax election not found.");
        if (election.Status is not (ElectionStatus.Draft or ElectionStatus.Verified))
            return (null, "ELECTION_STATE_ERROR", $"Election {electionId} is {election.Status}; only Draft/Verified elections verify.");
        db.Entry(election).Property(nameof(TaxElection.Status)).CurrentValue = ElectionStatus.Verified;
        db.Entry(election).Property(nameof(TaxElection.VerifiedBy)).CurrentValue = user.Id;
        db.Entry(election).Property(nameof(TaxElection.VerifiedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(user, "verify", "tax_election", electionId, $"Tax election {electionId} verified by {user.Id}");
        db.SaveChanges();
        return (election, null, null);
    }

    // payroll config rides finance_settings (data, not constants — R-PEO2 precedent)
    public (object? data, string? code, string? message) UpdatePayrollConfig(CurrentUser user, Dtos.UpdatePayrollConfigRequest request)
    {
        if (request.MandatoryElection is { } mandatory)
            UpsertSetting(MandatoryElectionKey, mandatory ? "true" : "false");
        if (request.ElectionKind is { Length: > 0 } kind)
            UpsertSetting(ElectionKindKey, kind);
        audit.Log(user, "update", "payroll_config", "payroll",
            $"Payroll config updated: mandatoryElection={request.MandatoryElection?.ToString() ?? "unchanged"}, electionKind={request.ElectionKind ?? "unchanged"}");
        db.SaveChanges();
        return (ReadConfig(), null, null);
    }

    public object ReadConfig() => new
    {
        mandatoryElection = Setting(MandatoryElectionKey) == "true",
        electionKind = Setting(ElectionKindKey),
    };

    // 041 AC2: the election payroll consumes is the VERIFIED one effective at the pay date
    public bool HasVerifiedElection(string employmentId, string? kind, DateOnly payDate) =>
        db.TaxElections.Any(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId
            && e.EmploymentId == employmentId && e.Status == ElectionStatus.Verified
            && (kind == null || e.Kind == kind)
            && e.EffectiveFrom <= payDate
            && (e.EffectiveTo == null || e.EffectiveTo >= payDate));

    public object ListContributionRules(CurrentUser user) =>
        db.ContributionRules.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId)
            .OrderBy(r => r.Code).ThenBy(r => r.EffectiveFrom).ToList().Select(ProjectRule).ToList();

    public (ContributionRule? rule, string? code, string? message) CreateContributionRule(CurrentUser user, Dtos.CreateContributionRuleRequest request)
    {
        if (request.RatePct < 0) return (null, "VALIDATION_ERROR", "RatePct must be non-negative.");
        if (people.FindPayCode(request.PayCodeId) is null) return (null, "PAY_CODE_NOT_FOUND", $"Pay code {request.PayCodeId} not found in the catalog.");
        var rule = new ContributionRule(numbers.Next("CGR"), ec.TenantId, ec.EntityId, request.Code,
            request.Name, request.Party, request.RatePct, request.ThresholdAmount, request.CeilingAmount,
            request.PayCodeId, request.EffectiveFrom, request.EffectiveTo, 1, true, user.Id, DateTimeOffset.UtcNow);
        db.ContributionRules.Add(rule);
        audit.Log(user, "create", "contribution_rule", rule.Id,
            $"Contribution rule {request.Code} {request.RatePct:0.##}% ({request.Party}) on {request.PayCodeId} from {request.EffectiveFrom:yyyy-MM-dd}");
        db.SaveChanges();
        return (rule, null, null);
    }

    // 042 AC2: expected = Σ per-worker clamped(gross × rate) vs the run's posted lines for the
    // rule's pay code — reconciled to the gross-to-net output, not asserted in a vacuum
    public (object? data, string? code, string? message) ReconcileContribution(CurrentUser user, string ruleId, string runId)
    {
        var rule = db.ContributionRules.FirstOrDefault(r => r.Id == ruleId
            && r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);
        if (rule is null) return (null, "CONTRIBUTION_RULE_NOT_FOUND", "Contribution rule not found.");
        var run = db.PayRuns.Include(r => r.Lines).FirstOrDefault(r => r.Id == runId
            && r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");

        var posted = decimal.Round(run.Lines.Where(l => l.PayCodeId == rule.PayCodeId).Sum(l => l.Amount), 2,
            MidpointRounding.AwayFromZero);
        var expected = 0m;
        foreach (var gross in run.Lines.Where(l => l.Kind == PayCodeKind.Earning)
                     .GroupBy(l => l.EmploymentId).Select(g => g.Sum(l => l.Amount)))
        {
            var baseAmount = rule.ThresholdAmount is { } t ? Math.Max(gross, t) : gross;
            if (rule.CeilingAmount is { } c) baseAmount = Math.Min(baseAmount, c);
            expected += decimal.Round(baseAmount * rule.RatePct / 100m, 2, MidpointRounding.AwayFromZero);
        }
        expected = decimal.Round(expected, 2, MidpointRounding.AwayFromZero);
        return (new
        {
            ruleId, code = rule.Code, runId, expected, posted,
            variance = decimal.Round(expected - posted, 2, MidpointRounding.AwayFromZero),
            reconciles = Math.Abs(expected - posted) <= 0.01m,
        }, null, null);
    }

    public object ListGarnishments(CurrentUser user, string? employmentId) =>
        db.GarnishmentOrders.Where(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId)
            .Where(o => employmentId == null || o.EmploymentId == employmentId)
            .OrderBy(o => o.Priority).ThenBy(o => o.Id).ToList().Select(ProjectGarnishment).ToList();

    public (GarnishmentOrder? order, string? code, string? message) CreateGarnishment(CurrentUser user, Dtos.CreateGarnishmentRequest request)
    {
        if (!db.Employments.Any(w => w.Id == request.EmploymentId
                && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId))
            return (null, "WORKER_NOT_FOUND", "Worker not found.");
        if (request.AmountPerPeriod <= 0) return (null, "VALIDATION_ERROR", "AmountPerPeriod must be greater than 0.");
        // 043 AC2: conflicting orders (same priority, overlapping effective periods) are a
        // BLOCKING exception at the write — never silently stacked
        var conflict = db.GarnishmentOrders.Where(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId
                && o.EmploymentId == request.EmploymentId && o.Priority == request.Priority
                && o.Status == GarnishmentStatus.Active).ToList()
            .FirstOrDefault(o => DeferredUtil.Overlaps(request.EffectiveFrom, request.EffectiveTo, o.EffectiveFrom, o.EffectiveTo));
        if (conflict is not null)
            return (null, "GARNISHMENT_CONFLICT",
                $"Worker {request.EmploymentId} already holds an ACTIVE garnishment with priority {request.Priority} effective {conflict.EffectiveFrom:yyyy-MM-dd}..{conflict.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"} — conflicting orders block (043 AC2).");
        var order = new GarnishmentOrder(numbers.Next("GRO"), ec.TenantId, ec.EntityId, request.EmploymentId,
            request.Kind, request.Priority, request.AmountPerPeriod, request.ProtectedMinimum, request.CourtRef,
            request.EffectiveFrom, request.EffectiveTo, GarnishmentStatus.Active, null, null, null,
            user.Id, DateTimeOffset.UtcNow);
        db.GarnishmentOrders.Add(order);
        audit.Log(user, "create", "garnishment_order", order.Id,
            $"Garnishment order {request.Kind} p{request.Priority} {request.AmountPerPeriod:0.##}/period for {request.EmploymentId} from {request.EffectiveFrom:yyyy-MM-dd}");
        db.SaveChanges();
        return (order, null, null);
    }

    // 043 AC2: release = authorized status change (payroll:release, checked by controller)
    // + evidence; orders outside their effective period are unusable — release refuses.
    public (GarnishmentOrder? order, string? code, string? message) ReleaseGarnishment(CurrentUser releaser, string orderId, Dtos.ReleaseGarnishmentRequest request)
    {
        var order = db.GarnishmentOrders.FirstOrDefault(o => o.Id == orderId
            && o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId);
        if (order is null) return (null, "GARNISHMENT_NOT_FOUND", "Garnishment order not found.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!DeferredUtil.Covers(order.EffectiveFrom, order.EffectiveTo, today))
            return (null, "GARNISHMENT_NOT_ACTIVE",
                $"Order {orderId} is outside its effective period ({order.EffectiveFrom:yyyy-MM-dd}..{order.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"}); it is unusable today (043 AC2).");
        if (order.Status != GarnishmentStatus.Active)
            return (null, "GARNISHMENT_STATE_ERROR", $"Order {orderId} is {order.Status}; only Active orders release.");
        db.Entry(order).Property(nameof(GarnishmentOrder.Status)).CurrentValue = GarnishmentStatus.Released;
        db.Entry(order).Property(nameof(GarnishmentOrder.ReleaseEvidence)).CurrentValue = request.Evidence;
        db.Entry(order).Property(nameof(GarnishmentOrder.ReleasedBy)).CurrentValue = releaser.Id;
        db.Entry(order).Property(nameof(GarnishmentOrder.ReleasedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(releaser, "release", "garnishment_order", orderId,
            $"Garnishment order {orderId} RELEASED with evidence '{request.Evidence}' by {releaser.Id} (authorized status change, 043 AC2)");
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, "GARNISHMENT_STATE_ERROR", $"Order {orderId} was released concurrently; the retry wrote nothing.");
        }
        return (order, null, null);
    }

    public object ListBenefitPlans(CurrentUser user) =>
        db.BenefitPlans.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId)
            .OrderBy(p => p.Code).ToList().Select(ProjectPlan).ToList();

    public (BenefitPlan? plan, string? code, string? message) CreateBenefitPlan(CurrentUser user, Dtos.CreateBenefitPlanRequest request)
    {
        if (people.FindPayCode(request.PayCodeId) is null) return (null, "PAY_CODE_NOT_FOUND", $"Pay code {request.PayCodeId} not found in the catalog.");
        if (db.BenefitPlans.Any(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId && p.Code == request.Code))
            return (null, "BENEFIT_PLAN_EXISTS", $"Benefit plan {request.Code} already exists.");
        var plan = new BenefitPlan(numbers.Next("BPL"), ec.TenantId, ec.EntityId, request.Code, request.Name,
            request.ProviderCode, request.EmployeePct, request.PayCodeId, true, user.Id, DateTimeOffset.UtcNow);
        db.BenefitPlans.Add(plan);
        audit.Log(user, "create", "benefit_plan", plan.Id, $"Benefit plan {request.Code} ({request.ProviderCode}) on {request.PayCodeId}");
        db.SaveChanges();
        return (plan, null, null);
    }

    // 044 AC1: enrollment emits a versioned deduction instruction for the effective period
    public (BenefitEnrollment? enrollment, string? code, string? message) CreateEnrollment(CurrentUser user, Dtos.CreateEnrollmentRequest request)
    {
        var plan = db.BenefitPlans.FirstOrDefault(p => p.Id == request.PlanId
            && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        if (plan is null) return (null, "BENEFIT_PLAN_NOT_FOUND", "Benefit plan not found.");
        if (!db.Employments.Any(w => w.Id == request.EmploymentId
                && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId))
            return (null, "WORKER_NOT_FOUND", "Worker not found.");
        var overlapping = db.BenefitEnrollments.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId
                && e.PlanId == plan.Id && e.EmploymentId == request.EmploymentId && e.Status == "Active").ToList()
            .FirstOrDefault(e => DeferredUtil.Overlaps(request.EffectiveFrom, request.EffectiveTo, e.EffectiveFrom, e.EffectiveTo));
        if (overlapping is not null)
            return (null, "ENROLLMENT_OVERLAP", $"Worker already holds an active enrollment in {plan.Code} from {overlapping.EffectiveFrom:yyyy-MM-dd}.");
        var enrollment = new BenefitEnrollment(numbers.Next("ENR"), ec.TenantId, ec.EntityId, plan.Id,
            request.EmploymentId, request.EffectiveFrom, request.EffectiveTo, "Active", user.Id, DateTimeOffset.UtcNow);
        db.BenefitEnrollments.Add(enrollment);
        var period = DeferredUtil.PeriodOf(request.EffectiveFrom);
        db.DeductionInstructions.Add(new DeductionInstruction($"{enrollment.Id}:{period}:v1", ec.TenantId,
            ec.EntityId, enrollment.Id, period, InstructionAmount(plan), 1, "enrollment", user.Id, DateTimeOffset.UtcNow));
        audit.Log(user, "create", "benefit_enrollment", enrollment.Id,
            $"Enrollment {plan.Code} for {request.EmploymentId} from {request.EffectiveFrom:yyyy-MM-dd} — deduction instruction v1 emitted for {period}");
        db.SaveChanges();
        return (enrollment, null, null);
    }

    public (object? data, string? code, string? message) EnrollmentDetail(CurrentUser user, string enrollmentId)
    {
        var enrollment = db.BenefitEnrollments.FirstOrDefault(e => e.Id == enrollmentId
            && e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);
        if (enrollment is null) return (null, "ENROLLMENT_NOT_FOUND", "Enrollment not found.");
        var instructions = db.DeductionInstructions.Where(i => i.EnrollmentId == enrollmentId)
            .OrderBy(i => i.Period).ThenBy(i => i.Version).ToList();
        return (new
        {
            enrollment = ProjectEnrollment(enrollment),
            instructions = instructions.Select(i => new { i.Id, i.Period, i.Amount, i.Version, i.Source }),
        }, null, null);
    }

    // 044 AC2: provider feeds are ID-REFERENCED facts — the same source event id replays as a
    // duplicate (ignored), a mismatching amount is flagged out-of-sync, and neither overwrites
    // the instruction or the enrollment.
    public (object? data, string? code, string? message) PostProviderFeed(CurrentUser user, string enrollmentId, Dtos.PostProviderFeedRequest request)
    {
        var enrollment = db.BenefitEnrollments.FirstOrDefault(e => e.Id == enrollmentId
            && e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);
        if (enrollment is null) return (null, "ENROLLMENT_NOT_FOUND", "Enrollment not found.");
        var feedId = $"{enrollmentId}:{request.Period}:feed:{request.SourceEventId}";
        var duplicate = db.DeductionInstructions.Any(i => i.Id == feedId);
        if (!duplicate)
        {
            db.DeductionInstructions.Add(new DeductionInstruction(feedId, ec.TenantId, ec.EntityId,
                enrollmentId, request.Period, request.Amount, 1, $"provider:{request.SourceEventId}",
                user.Id, DateTimeOffset.UtcNow));
            audit.Log(user, "provider_feed", "benefit_enrollment", enrollmentId,
                $"Provider feed {request.SourceEventId} for period {request.Period}: {request.Amount:0.00} recorded");
            db.SaveChanges();
        }
        return (new { enrollmentId, period = request.Period, sourceEventId = request.SourceEventId, duplicate },
            null, null);
    }

    public (object? data, string? code, string? message) ReconcileEnrollment(CurrentUser user, string enrollmentId, string period)
    {
        var enrollment = db.BenefitEnrollments.FirstOrDefault(e => e.Id == enrollmentId
            && e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);
        if (enrollment is null) return (null, "ENROLLMENT_NOT_FOUND", "Enrollment not found.");
        var rows = db.DeductionInstructions.Where(i => i.EnrollmentId == enrollmentId && i.Period == period).ToList();
        var emitted = rows.Where(i => i.Source == "enrollment").ToList();
        var feeds = rows.Where(i => i.Source.StartsWith("provider:", StringComparison.Ordinal)).ToList();
        var flags = new List<string>();
        if (emitted.Count == 0) flags.Add("MISSING_INSTRUCTION");
        if (emitted.Count > 1) flags.Add("DUPLICATE_INSTRUCTION");
        if (feeds.Count == 0) flags.Add("MISSING_FEED");
        if (feeds.GroupBy(f => f.Source).Any(g => g.Count() > 1)) flags.Add("DUPLICATE_FEED");
        foreach (var feed in feeds.Where(f => emitted.Any(e => Math.Abs(e.Amount - f.Amount) > 0.01m)))
            flags.Add("OUT_OF_SYNC");
        return (new
        {
            enrollmentId, period,
            instruction = emitted.OrderBy(i => i.Version).Select(i => new { i.Id, i.Amount, i.Version }).FirstOrDefault(),
            feedCount = feeds.Count, flags, outOfSync = flags.Any(f => f == "OUT_OF_SYNC" || f == "MISSING_FEED"),
        }, null, null);
    }

    // the instruction amount: flat when the pay code is Amount-basis, else the plan percentage
    private decimal InstructionAmount(BenefitPlan plan)
    {
        var code = people.FindPayCode(plan.PayCodeId);
        return code is { CalcBasis: "Amount" } ? code.RatePct ?? 0m : plan.EmployeePct;
    }

    private void UpsertSetting(string key, string value)
    {
        var id = $"FS-{key}";
        var setting = db.FinanceSettings.FirstOrDefault(s => s.Id == id);
        if (setting is null)
            db.FinanceSettings.Add(new FinanceSetting(id, ec.TenantId, ec.EntityId, key, value));
        else
            db.Entry(setting).Property(nameof(FinanceSetting.Value)).CurrentValue = value;
    }

    private string? Setting(string key) =>
        db.FinanceSettings.FirstOrDefault(s => s.Id == $"FS-{key}")?.Value;

    private static object ProjectElection(TaxElection e) => new
    {
        e.Id, e.EmploymentId, e.Kind, e.Value, status = e.Status.ToString(),
        e.EffectiveFrom, e.EffectiveTo, e.VerifiedBy, e.VerifiedAt,
    };

    private static object ProjectRule(ContributionRule r) => new
    {
        r.Id, r.Code, r.Name, r.Party, r.RatePct, r.ThresholdAmount, r.CeilingAmount, r.PayCodeId,
        r.EffectiveFrom, r.EffectiveTo, r.Version, r.Active,
    };

    private static object ProjectGarnishment(GarnishmentOrder o) => new
    {
        o.Id, o.EmploymentId, o.Kind, o.Priority, o.AmountPerPeriod, o.ProtectedMinimum, o.CourtRef,
        o.EffectiveFrom, o.EffectiveTo, status = o.Status.ToString(), o.ReleaseEvidence, o.ReleasedBy, o.ReleasedAt,
    };

    private static object ProjectPlan(BenefitPlan p) => new
    {
        p.Id, p.Code, p.Name, p.ProviderCode, p.EmployeePct, p.PayCodeId, p.Active,
    };

    private static object ProjectEnrollment(BenefitEnrollment e) => new
    {
        e.Id, e.PlanId, e.EmploymentId, e.EffectiveFrom, e.EffectiveTo, e.Status,
    };
}

// PM7 — payment file handoff (ERP-PEOPLE-050, maker-checker): the file is generated ONLY from
// an approved run snapshot and reconciles instruction count + total to the approved net-pay
// population; release (payroll:release) is a SEPARATE authorization from calculation approval
// and is refused to the file's maker; part-rejection isolates items without retransmitting
// accepted ones.
public sealed class PaymentFileService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : IPaymentFileService
{
    public object ListFiles(CurrentUser user, PaymentFileStatus? status) =>
        db.PaymentFiles.Include(f => f.Items)
            .Where(f => f.TenantId == ec.TenantId && f.LegalEntityId == ec.EntityId)
            .Where(f => status == null || f.Status == status)
            .OrderBy(f => f.Id).ToList().Select(ProjectFile).ToList();

    public (object? data, string? code, string? message) FileDetail(CurrentUser user, string fileId)
    {
        var file = FindFile(fileId);
        return file is null ? (null, "PAYFILE_NOT_FOUND", "Payment file not found.") : (ProjectFile(file), null, null);
    }

    public (object? data, string? code, string? message, bool replayed) CreateForRun(CurrentUser user, string runId, Dtos.CreatePaymentFileRequest request)
    {
        var run = db.PayRuns.Include(r => r.Lines).FirstOrDefault(r => r.Id == runId
            && r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.", false);
        if (run.Status is not (PayRunStatus.Approved or PayRunStatus.Posted))
            return (null, "PAYRUN_NOT_APPROVED",
                $"Run {runId} is {run.Status}; the payment file is generated only from an approved snapshot (050 AC1).", false);
        var existing = db.PaymentFiles.Include(f => f.Items).FirstOrDefault(f => f.RunId == runId
            && f.TenantId == ec.TenantId && f.LegalEntityId == ec.EntityId);
        if (existing is not null) return (ProjectFile(existing), null, null, true); // idempotent create
        if (string.IsNullOrWhiteSpace(request.Provider))
            return (null, "VALIDATION_ERROR", "Provider is required.", false);

        var perWorker = run.Lines.GroupBy(l => l.EmploymentId).OrderBy(g => g.Key)
            .Select(g => (employmentId: g.Key,
                net: decimal.Round(g.Sum(l => l.Kind == PayCodeKind.Earning ? l.Amount : -l.Amount), 2,
                    MidpointRounding.AwayFromZero)))
            .ToList();
        var totalNet = decimal.Round(perWorker.Sum(w => w.net), 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(totalNet - run.NetTotal) > 0.01m)
            return (null, "PAYFILE_RECONCILIATION_BROKEN",
                $"Per-worker instructions total {totalNet:0.00} but the approved run net is {run.NetTotal:0.00} — refusing to hand off a file that does not reconcile (050 AC1).", false);

        var content = "employmentId,netAmount,currency\n"
            + string.Join("\n", perWorker.Select(w => $"{w.employmentId},{w.net:0.00},{run.Currency}"));
        var file = new PaymentFile(numbers.Next("PAY"), ec.TenantId, ec.EntityId, runId,
            PaymentFileStatus.Prepared, request.Provider, "CSV", "1",
            DeferredUtil.Sha256Hex(content), perWorker.Count, totalNet, run.Currency, run.PayDate,
            null, null, user.Id, DateTimeOffset.UtcNow)
        {
            Items = perWorker.Select(w => new PaymentFileItem(w.employmentId, w.net, run.Currency)).ToList(),
        };
        db.PaymentFiles.Add(file);
        DeferredUtil.PinOwned(db, ec, file.Items);
        audit.Log(user, "create", "payment_file", file.Id,
            $"Payment file {file.Id} from run {runId}: {perWorker.Count} instruction(s), total {totalNet:0.00} {run.Currency}, sha256 {file.ContentHash[..12]}… (maker {user.Id})");
        audit.Emit(user, "people.payfile.created", new
        {
            fileId = file.Id, runId, itemCount = perWorker.Count, totalNet, hash = file.ContentHash,
            provider = request.Provider, format = "CSV", formatVersion = "1", maker = user.Id,
        });
        db.SaveChanges();
        return (ProjectFile(db.PaymentFiles.Include(f => f.Items).First(f => f.Id == file.Id)), null, null, false);
    }

    // 050 AC2: calculation approval ≠ funds release — payroll:release (R-FIN, controller gate)
    // AND a checker who is not the maker; provenance (maker, checker, hash, provider, format,
    // date) is all on the row after this call.
    public (object? data, string? code, string? message) Release(CurrentUser releaser, string fileId)
    {
        var file = FindFile(fileId);
        if (file is null) return (null, "PAYFILE_NOT_FOUND", "Payment file not found.");
        if (file.Status != PaymentFileStatus.Prepared)
            return (null, "PAYFILE_STATE_ERROR", $"Payment file {fileId} is {file.Status}; only Prepared files release.");
        if (file.CreatedBy == releaser.Id)
            return (null, "PAYROLL_RELEASE_ERROR",
                "Maker-checker: the file maker cannot release their own file — funds release needs a second authorizer (050 AC2).");
        db.Entry(file).Property(nameof(PaymentFile.Status)).CurrentValue = PaymentFileStatus.Released;
        db.Entry(file).Property(nameof(PaymentFile.ReleasedBy)).CurrentValue = releaser.Id;
        db.Entry(file).Property(nameof(PaymentFile.ReleasedAt)).CurrentValue = DateTimeOffset.UtcNow;
        foreach (var item in file.Items.Where(i => i.Status == PaymentItemStatus.Pending))
            item.Status = PaymentItemStatus.Released;
        audit.Log(releaser, "release", "payment_file", fileId,
            $"Payment file {fileId} RELEASED to {file.Provider} ({file.Format} v{file.FormatVersion}), {file.ItemCount} item(s) {file.TotalNet:0.00} {file.Currency}, maker {file.CreatedBy}, checker {releaser.Id}");
        audit.Emit(releaser, "people.payfile.released", new
        {
            fileId, runId = file.RunId, releasedBy = releaser.Id, maker = file.CreatedBy,
            hash = file.ContentHash, itemCount = file.ItemCount, totalNet = file.TotalNet, payDate = file.PayDate,
        });
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, "PAYFILE_STATE_ERROR", $"Payment file {fileId} was released concurrently; the retry wrote nothing.");
        }
        return (ProjectFile(FindFile(fileId)!), null, null);
    }

    // 050 edge: part-rejection isolates the rejected instructions; the accepted ones keep their
    // Released state and are NOT retransmitted (no second file, no re-emit of accepted rows)
    public (object? data, string? code, string? message) RejectItems(CurrentUser releaser, string fileId, Dtos.RejectPaymentFileItemsRequest request)
    {
        var file = FindFile(fileId);
        if (file is null) return (null, "PAYFILE_NOT_FOUND", "Payment file not found.");
        if (file.Status is not (PaymentFileStatus.Prepared or PaymentFileStatus.Released))
            return (null, "PAYFILE_STATE_ERROR", $"Payment file {fileId} is {file.Status}; items reject only while Prepared/Released.");
        if (request.Items.Count == 0) return (null, "VALIDATION_ERROR", "At least one rejection item is required.");

        var rejected = new List<object>();
        var accepted = new List<object>();
        foreach (var reject in request.Items)
        {
            var item = file.Items.FirstOrDefault(i => i.EmploymentId == reject.EmploymentId);
            if (item is null)
                return (null, "PAYFILE_ITEM_NOT_FOUND", $"Worker {reject.EmploymentId} has no instruction in file {fileId}.");
            if (item.Status == PaymentItemStatus.Rejected)
                return (null, "PAYFILE_STATE_ERROR", $"Instruction for {reject.EmploymentId} is already rejected.");
            if (string.IsNullOrWhiteSpace(reject.Reason))
                return (null, "VALIDATION_ERROR", "Each rejection needs a reason.");
            item.Status = PaymentItemStatus.Rejected;
            item.RejectReason = reject.Reason;
            rejected.Add(new { item.EmploymentId, reason = reject.Reason });
        }
        foreach (var item in file.Items.Where(i => i.Status != PaymentItemStatus.Rejected))
            accepted.Add(new { item.EmploymentId, status = item.Status.ToString() }); // untouched, not retransmitted

        if (file.Items.Any(i => i.Status == PaymentItemStatus.Rejected) && file.Status == PaymentFileStatus.Released)
            db.Entry(file).Property(nameof(PaymentFile.Status)).CurrentValue = PaymentFileStatus.PartiallyRejected;
        audit.Log(releaser, "reject_items", "payment_file", fileId,
            $"Payment file {fileId}: {rejected.Count} instruction(s) rejected ({string.Join(", ", request.Items.Select(i => i.EmploymentId))}); accepted items stay Released and are not retransmitted");
        db.SaveChanges();
        return (new
        {
            fileId, status = FindFile(fileId)!.Status.ToString(),
            rejected, accepted,
        }, null, null);
    }

    private PaymentFile? FindFile(string fileId) =>
        db.PaymentFiles.Include(f => f.Items).FirstOrDefault(f => f.Id == fileId
            && f.TenantId == ec.TenantId && f.LegalEntityId == ec.EntityId);

    private static object ProjectFile(PaymentFile f) => new
    {
        f.Id, f.RunId, status = f.Status.ToString(), f.Provider, f.Format, f.FormatVersion, f.ContentHash,
        f.ItemCount, f.TotalNet, f.Currency, f.PayDate, f.CreatedBy, f.ReleasedBy, f.ReleasedAt,
        items = f.Items.Select(i => new
        {
            i.EmploymentId, i.NetAmount, i.Currency, status = i.Status.ToString(), i.RejectReason,
        }),
    };
}

// PM8 — statutory reporting boundary + payroll close (ERP-PEOPLE-053/054): effective-dated
// account/cost mappings consumed by the posting path (missing/inactive mapping blocks and
// NAMES the element), and the period close checklist with authorized waivers + a signed summary.
public sealed class PayrollCloseService(
    ErpDbContext db, EntityContext ec, IAuditRepository audit) : IPayrollCloseService
{
    public static readonly string[] MappingElements =
        ["SALARY_EXPENSE", "DEDUCTIONS_PAYABLE", "SALARIES_PAYABLE"];

    // the landed constants stay as fallback when NO mapping covers the date (053 AC2: an
    // existing-but-inactive mapping blocks; a missing one falls back to the landed defaults)
    public static ((string SalaryExpense, string DeductionsPayable, string SalariesPayable)? accounts, (string code, string message)? error)
        ResolveAccounts(ErpDbContext db, EntityContext ec, DateOnly payDate)
    {
        var salary = "6100";
        var deductions = "2310";
        var netPay = "2300";
        foreach (var element in MappingElements)
        {
            var mapping = db.PayrollAccountMappings
                .Where(m => m.TenantId == ec.TenantId && m.LegalEntityId == ec.EntityId && m.Element == element)
                .ToList()
                .Where(m => m.EffectiveFrom <= payDate && (m.EffectiveTo == null || m.EffectiveTo >= payDate))
                .OrderByDescending(m => m.EffectiveFrom).FirstOrDefault();
            if (mapping is null) continue; // no mapping → landed constant
            if (!mapping.Active)
                return (null, ("PAYROLL_MAPPING_MISSING",
                    $"Account mapping for {element} covering {payDate:yyyy-MM-dd} is INACTIVE — posting is blocked and the element is named (053 AC2)."));
            var account = db.Accounts.FirstOrDefault(a => a.Code == mapping.AccountCode
                && a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);
            if (account is null || !account.DirectPostingAllowed)
                return (null, ("PAYROLL_MAPPING_MISSING",
                    $"Mapping for {element} covering {payDate:yyyy-MM-dd} names account {mapping.AccountCode}, which is missing or not direct-postable (053 AC2)."));
            switch (element)
            {
                case "SALARY_EXPENSE": salary = mapping.AccountCode; break;
                case "DEDUCTIONS_PAYABLE": deductions = mapping.AccountCode; break;
                default: netPay = mapping.AccountCode; break;
            }
        }
        return ((salary, deductions, netPay), null);
    }

    public object ListMappings(CurrentUser user) =>
        db.PayrollAccountMappings.Where(m => m.TenantId == ec.TenantId && m.LegalEntityId == ec.EntityId)
            .OrderBy(m => m.Element).ThenBy(m => m.EffectiveFrom).ToList()
            .Select(m => new { m.Id, m.Element, m.AccountCode, m.EffectiveFrom, m.EffectiveTo, m.Active })
            .ToList();

    public (PayrollAccountMapping? mapping, string? code, string? message) CreateMapping(CurrentUser user, Dtos.CreateAccountMappingRequest request)
    {
        if (!MappingElements.Contains(request.Element))
            return (null, "VALIDATION_ERROR", $"Element must be one of {string.Join(", ", MappingElements)}.");
        var account = db.Accounts.FirstOrDefault(a => a.Code == request.AccountCode
            && a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);
        if (account is null || !account.DirectPostingAllowed)
            return (null, "ACCOUNT_NOT_FOUND", $"Account {request.AccountCode} is missing or not direct-postable.");
        var overlapping = db.PayrollAccountMappings.Where(m => m.TenantId == ec.TenantId && m.LegalEntityId == ec.EntityId
                && m.Element == request.Element).ToList()
            .FirstOrDefault(m => DeferredUtil.Overlaps(request.EffectiveFrom, request.EffectiveTo, m.EffectiveFrom, m.EffectiveTo));
        if (overlapping is not null)
            return (null, "MAPPING_OVERLAP",
                $"Element {request.Element} already maps from {overlapping.EffectiveFrom:yyyy-MM-dd}; mapping intervals may not overlap.");
        var mapping = new PayrollAccountMapping(
            $"map:{request.Element}:{request.EffectiveFrom:yyyyMMdd}", ec.TenantId, ec.EntityId,
            request.Element, request.AccountCode, request.EffectiveFrom, request.EffectiveTo,
            request.Active ?? true, user.Id, DateTimeOffset.UtcNow);
        db.PayrollAccountMappings.Add(mapping);
        audit.Log(user, "create", "payroll_account_mapping", mapping.Id,
            $"Mapping {request.Element} → {request.AccountCode} from {request.EffectiveFrom:yyyy-MM-dd} (active: {mapping.Active})");
        db.SaveChanges();
        return (mapping, null, null);
    }

    // 054 AC1: the checklist reconciles pay / payment / liabilities / GL; every mandatory item
    // must resolve or carry an authorized waiver before the period closes
    public (object? data, string? code, string? message) CloseChecklist(CurrentUser user, string periodId)
    {
        var (items, error) = ChecklistItems(periodId);
        if (error is not null) return (null, error.Value.code, error.Value.message);
        return (new { periodId, canClose = items.All(i => i.status == "Ok"), items = ProjectItems(items) }, null, null);
    }

    // ValueTuples serialize as empty objects — project to named members for the wire
    private static object[] ProjectItems((string code, bool required, string status, string message)[] items) =>
        items.Select(i => (object)new { i.code, i.required, i.status, i.message }).ToArray();

    public (object? data, string? code, string? message, object[]? items) Close(
        CurrentUser closer, string periodId, Dtos.ClosePayPeriodRequest request)
    {
        var close = db.PayrollPeriodCloses.FirstOrDefault(c => c.Id == periodId
            && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);
        if (close is { Status: PayPeriodCloseStatus.Closed })
            return (null, "CLOSE_ALREADY_CLOSED", $"Period {periodId} is already closed; reopen first (054).", null);
        var resuming = close is { Status: PayPeriodCloseStatus.PendingAcknowledgment };

        if (!resuming)
        {
            var (items, error) = ChecklistItems(periodId);
            if (error is not null) return (null, error.Value.code, error.Value.message, null);
            var waivers = request.Waivers ?? [];
            if (waivers.Any(w => string.IsNullOrWhiteSpace(w.Reason)))
                return (null, "VALIDATION_ERROR", "Every waiver needs a reason (054 AC1).", null);
            if (waivers.Count > 0 && waivers.All(w => string.IsNullOrWhiteSpace(w.Code)))
                return (null, "VALIDATION_ERROR", "Every waiver needs the item code it clears.", null);
            var unresolved = items.Where(i => i.status != "Ok" && i.required).ToList();
            foreach (var waiver in waivers.Where(w => unresolved.All(i => i.code != w.Code)))
                return (null, "VALIDATION_ERROR", $"Waiver code '{waiver.Code}' does not match any unresolved mandatory item.", null);
            var waivedCodes = waivers.Select(w => w.Code).ToHashSet();
            var stillOpen = unresolved.Where(i => !waivedCodes.Contains(i.code)).ToList();
            if (stillOpen.Count > 0)
                return (null, "CLOSE_BLOCKED",
                    $"{stillOpen.Count} mandatory reconciliation item(s) unresolved — close is blocked unless each is waived by an authorized approver with a reason (054 AC1).",
                    ProjectItems(stillOpen.ToArray()));

            var runs = RunsFor(periodId);
            var summary = new
            {
                periodId,
                runIds = runs.Select(r => r.Id).ToArray(),
                totals = new
                {
                    gross = decimal.Round(runs.Where(r => r.Status != PayRunStatus.Reversed).Sum(r => r.GrossTotal), 2),
                    deductions = decimal.Round(runs.Where(r => r.Status != PayRunStatus.Reversed).Sum(r => r.DeductionTotal), 2),
                    net = decimal.Round(runs.Where(r => r.Status != PayRunStatus.Reversed).Sum(r => r.NetTotal), 2),
                },
                exceptions = Array.Empty<string>(),
                paymentStatuses = PaymentFilesFor(periodId).Select(f => new { fileId = f.Id, runId = f.RunId, status = f.Status.ToString() }).ToArray(),
                ledgerRefs = runs.Where(r => r.JournalId != null).Select(r => new { runId = r.Id, journalId = r.JournalId }).ToArray(),
                waivers = waivers.Select(w => new { code = w.Code, reason = w.Reason, approvedBy = closer.Id }).ToArray(),
                closedAt = DateTimeOffset.UtcNow, closedBy = closer.Id,
            };
            var json = JsonSerializer.Serialize(summary, DeferredJsonOptions);
            var isNew = close is null;
            close ??= new PayrollPeriodClose(periodId, ec.TenantId, ec.EntityId, periodId,
                PayPeriodCloseStatus.Closed, json, DeferredUtil.Sha256Hex(json), closer.Id,
                DateTimeOffset.UtcNow, null, null, closer.Id, DateTimeOffset.UtcNow);
            if (isNew) db.PayrollPeriodCloses.Add(close);
            db.Entry(close).Property(nameof(PayrollPeriodClose.Status)).CurrentValue =
                request.AcknowledgeLater == true ? PayPeriodCloseStatus.PendingAcknowledgment : PayPeriodCloseStatus.Closed;
            db.Entry(close).Property(nameof(PayrollPeriodClose.SummaryJson)).CurrentValue = json;
            db.Entry(close).Property(nameof(PayrollPeriodClose.SummaryHash)).CurrentValue = DeferredUtil.Sha256Hex(json);
            db.Entry(close).Property(nameof(PayrollPeriodClose.ClosedBy)).CurrentValue = closer.Id;
            db.Entry(close).Property(nameof(PayrollPeriodClose.ClosedAt)).CurrentValue = DateTimeOffset.UtcNow;
            audit.Log(closer, "close", "pay_period", periodId,
                $"Payroll period {periodId} {(request.AcknowledgeLater == true ? "PENDING ACKNOWLEDGMENT" : "CLOSED")}: {runs.Count} run(s), summary sha256 {close.SummaryHash![..12]}…, {waivers.Count} waiver(s)");
            audit.Emit(closer, "people.payperiod.closed", new
            {
                periodId, status = close.Status.ToString(), closedBy = closer.Id,
                summaryHash = close.SummaryHash, waivers = waivers.Count,
            });
        }
        else
        {
            // resumable WITHOUT duplicate execution: the stored signed summary is kept verbatim
            db.Entry(close).Property(nameof(PayrollPeriodClose.Status)).CurrentValue = PayPeriodCloseStatus.Closed;
            db.Entry(close).Property(nameof(PayrollPeriodClose.ClosedBy)).CurrentValue = closer.Id;
            db.Entry(close).Property(nameof(PayrollPeriodClose.ClosedAt)).CurrentValue = DateTimeOffset.UtcNow;
            audit.Log(closer, "close", "pay_period", periodId,
                $"Payroll period {periodId} close resumed from PendingAcknowledgment — stored summary kept, nothing re-executed (054)");
        }

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, "CLOSE_STATE_ERROR", $"Period {periodId} was closed concurrently; the retry wrote nothing.", null);
        }
        return (ProjectClose(close), null, null, null);
    }

    public (object? data, string? code, string? message) Reopen(CurrentUser closer, string periodId, Dtos.ReopenPayPeriodRequest request)
    {
        var close = db.PayrollPeriodCloses.FirstOrDefault(c => c.Id == periodId
            && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);
        if (close is null || close.Status == PayPeriodCloseStatus.Reopened)
            return (null, "CLOSE_STATE_ERROR", $"Period {periodId} has no closed record to reopen.");
        db.Entry(close).Property(nameof(PayrollPeriodClose.Status)).CurrentValue = PayPeriodCloseStatus.Reopened;
        db.Entry(close).Property(nameof(PayrollPeriodClose.ReopenedBy)).CurrentValue = closer.Id;
        db.Entry(close).Property(nameof(PayrollPeriodClose.ReopenedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(closer, "reopen", "pay_period", periodId, $"Payroll period {periodId} REOPENED — {request.Reason}");
        db.SaveChanges();
        return (ProjectClose(close), null, null);
    }

    private ((string code, bool required, string status, string message)[] items, (string code, string message)? error)
        ChecklistItems(string periodId)
    {
        var period = db.FiscalPeriods.FirstOrDefault(p => p.Id == periodId
            && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        if (period is null) return ([], ("PERIOD_NOT_FOUND", $"Fiscal period {periodId} not found."));
        var runs = RunsFor(periodId);
        var files = PaymentFilesFor(periodId);

        var notPosted = runs.Where(r => r.Status != PayRunStatus.Posted).ToList();
        var filesOpen = files.Where(f => f.Status != PaymentFileStatus.Released).ToList();
        var unlinked = runs.Where(r => r.Status == PayRunStatus.Posted && r.JournalId == null).ToList();
        var items = new List<(string code, bool required, string status, string message)>
        {
            notPosted.Count == 0
                ? ("RUNS_POSTED", true, "Ok", $"{runs.Count} run(s) posted.")
                : ("RUNS_POSTED", true, "Unresolved", $"Runs not posted: {string.Join(", ", notPosted.Select(r => r.Id))}"),
            filesOpen.Count == 0
                ? ("PAYFILE_RELEASED", true, "Ok", $"{files.Count} payment file(s) released.")
                : ("PAYFILE_RELEASED", true, "Unresolved",
                    $"Payment file(s) not released for runs {string.Join(", ", filesOpen.Select(f => f.RunId))} — release or waive (050/054)."),
            unlinked.Count == 0
                ? ("GL_LINKED", true, "Ok", "Every posted run carries its journal reference.")
                : ("GL_LINKED", true, "Unresolved", $"Posted runs without journal reference: {string.Join(", ", unlinked.Select(r => r.Id))}"),
        };
        return (items.ToArray(), null);
    }

    private List<PayRun> RunsFor(string periodId) =>
        db.PayRuns.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.PeriodId == periodId)
            .OrderBy(r => r.Id).ToList();

    private List<PaymentFile> PaymentFilesFor(string periodId)
    {
        var runIds = RunsFor(periodId).Select(r => r.Id).ToHashSet();
        return db.PaymentFiles.Include(f => f.Items)
            .Where(f => f.TenantId == ec.TenantId && f.LegalEntityId == ec.EntityId).ToList()
            .Where(f => runIds.Contains(f.RunId)).ToList();
    }

    private static object ProjectClose(PayrollPeriodClose c) => new
    {
        c.Id, periodId = c.PeriodId, status = c.Status.ToString(), c.ClosedBy, c.ClosedAt,
        c.ReopenedBy, c.ReopenedAt, c.SummaryHash,
        summary = c.SummaryJson is null
            ? (object?)null
            : JsonSerializer.Deserialize<JsonElement>(c.SummaryJson, DeferredJsonOptions),
    };

    private static readonly JsonSerializerOptions DeferredJsonOptions =
        new(JsonSerializerDefaults.Web);
}

