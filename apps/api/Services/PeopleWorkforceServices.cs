using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// PM9 — training & credentials (ERP-PEOPLE-058): effective-dated credentials whose EXPIRY is
// computed on read (Current + past ExpiresOn → Expired), waivers as an explicit named-approver
// act (never silent), and the requirement check the PM1 assignment gate consumes.
public sealed class CredentialService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : ICredentialService
{
    public object List(CurrentUser user, string? employmentId) =>
        db.Credentials.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId)
            .Where(c => employmentId == null || c.EmploymentId == employmentId)
            .OrderBy(c => c.EmploymentId).ThenBy(c => c.Code).ToList()
            .Select(Project).ToList();

    public (Credential? credential, string? code, string? message) Create(CurrentUser user, Dtos.CreateCredentialRequest request)
    {
        if (!db.Employments.Any(w => w.Id == request.EmploymentId
                && w.TenantId == ec.TenantId && w.LegalEntityId == ec.EntityId))
            return (null, "WORKER_NOT_FOUND", "Worker not found.");
        if (db.Credentials.Any(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId
                && c.EmploymentId == request.EmploymentId && c.Code == request.Code
                && c.Status != CredentialStatus.Waived))
            return (null, "CREDENTIAL_EXISTS",
                $"Worker {request.EmploymentId} already holds credential {request.Code}; supersede via a waiver first.");
        var credential = new Credential(numbers.Next("CRE"), ec.TenantId, ec.EntityId, request.EmploymentId,
            request.Code, request.Name, request.IssuedBy, request.IssuedOn, request.ExpiresOn,
            string.IsNullOrWhiteSpace(request.EvidenceRef) ? CredentialStatus.Unverified : CredentialStatus.Current,
            request.EvidenceRef, null, null, null, user.Id, DateTimeOffset.UtcNow);
        db.Credentials.Add(credential);
        audit.Log(user, "create", "credential", credential.Id,
            $"Credential {request.Code} for {request.EmploymentId} (expires {request.ExpiresOn?.ToString("yyyy-MM-dd") ?? "never"}) recorded");
        db.SaveChanges();
        return (credential, null, null);
    }

    // 058 AC2: the waiver is the designated exception act — named approver + reason, never the
    // credential's own maker
    public (Credential? credential, string? code, string? message) Waive(CurrentUser approver, string credentialId, Dtos.WaiveCredentialRequest request)
    {
        var credential = db.Credentials.FirstOrDefault(c => c.Id == credentialId
            && c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);
        if (credential is null) return (null, "CREDENTIAL_NOT_FOUND", "Credential not found.");
        if (credential.Status == CredentialStatus.Waived)
            return (null, "CREDENTIAL_STATE_ERROR", $"Credential {credentialId} is already waived.");
        if (credential.CreatedBy == approver.Id)
            return (null, "CREDENTIAL_WAIVER_ERROR",
                "Maker-checker: the credential maker cannot waive their own credential — a second officer approves the waiver (058 AC2).");
        db.Entry(credential).Property(nameof(Credential.Status)).CurrentValue = CredentialStatus.Waived;
        db.Entry(credential).Property(nameof(Credential.WaiverReason)).CurrentValue = request.Reason;
        db.Entry(credential).Property(nameof(Credential.WaivedBy)).CurrentValue = approver.Id;
        db.Entry(credential).Property(nameof(Credential.WaivedAt)).CurrentValue = DateTimeOffset.UtcNow;
        audit.Log(approver, "waive", "credential", credentialId,
            $"Credential {credential.Code} for {credential.EmploymentId} WAIVED by {approver.Id} — {request.Reason}");
        db.SaveChanges();
        return (credential, null, null);
    }

    private static object Project(Credential c)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var effective = c.Status switch
        {
            CredentialStatus.Waived => "Waived",
            CredentialStatus.Current when c.ExpiresOn is { } exp && exp < today => "Expired",
            _ => c.Status.ToString(),
        };
        return new
        {
            c.Id, c.EmploymentId, c.Code, c.Name, c.IssuedBy, c.IssuedOn, c.ExpiresOn,
            status = c.Status.ToString(), effectiveStatus = effective, c.EvidenceRef,
            c.WaiverReason, c.WaivedBy, c.WaivedAt, c.CreatedBy,
        };
    }
}

// PM10 — workforce plan, labor allocation, privacy-thresholded analytics (ERP-PEOPLE-059/060/061).
// Promotion into positions is an EXPLICIT workflow creating linked records; allocation approval
// demands 100% and flags inactive cost objects; analytics enforce small-group suppression
// server-side including the complementary total cell.
public sealed class WorkforceService(
    ErpDbContext db, EntityContext ec, INumberSeries numbers, IAuditRepository audit) : IWorkforceService
{
    private const int SuppressionThreshold = 5; // 061: k=5 pilot setting (D-08 revisits)

    public object ListPlans(CurrentUser user) =>
        db.WorkforcePlans.Include(p => p.Lines)
            .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId)
            .OrderBy(p => p.Code).ToList().Select(ProjectPlan).ToList();

    public (object? data, string? code, string? message) GetPlan(CurrentUser user, string planId)
    {
        var plan = db.WorkforcePlans.Include(p => p.Lines).FirstOrDefault(p => p.Id == planId
            && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        return plan is null ? (null, "WORKFORCE_PLAN_NOT_FOUND", "Workforce plan not found.") : (ProjectPlan(plan), null, null);
    }

    public (WorkforcePlan? plan, string? code, string? message) CreatePlan(CurrentUser user, Dtos.CreateWorkforcePlanRequest request)
    {
        if (request.HorizonEnd < request.HorizonStart)
            return (null, "VALIDATION_ERROR", "HorizonEnd must not precede HorizonStart.");
        if (request.Lines.Count == 0) return (null, "VALIDATION_ERROR", "At least one plan line is required.");
        if (db.WorkforcePlans.Any(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId && p.Code == request.Code))
            return (null, "WORKFORCE_PLAN_EXISTS", $"Workforce plan {request.Code} already exists.");
        var plan = new WorkforcePlan(numbers.Next("WFP"), ec.TenantId, ec.EntityId, request.Code, request.Name,
            request.HorizonStart, request.HorizonEnd, user.Id, request.Assumptions, "Open",
            user.Id, DateTimeOffset.UtcNow)
        {
            Lines = request.Lines.Select(l => new WorkforcePlanLine(l.PositionCode, l.Title, l.Fte,
                l.OrgUnitId, l.GradeCode)).ToList(),
        };
        db.WorkforcePlans.Add(plan);
        DeferredUtil.PinOwned(db, ec, plan.Lines);
        audit.Log(user, "create", "workforce_plan", plan.Id,
            $"Workforce plan {request.Code}: {request.Lines.Count} line(s), horizon {request.HorizonStart:yyyy-MM-dd}..{request.HorizonEnd:yyyy-MM-dd}");
        db.SaveChanges();
        return (plan, null, null);
    }

    // 059 AC2: promotion is an explicit workflow — linked Position rows carry SourcePlanId and
    // the line flips Promoted so a retry cannot double-create
    public (object? data, string? code, string? message) Promote(CurrentUser user, string planId, Dtos.PromotePlanRequest request)
    {
        var plan = db.WorkforcePlans.Include(p => p.Lines).FirstOrDefault(p => p.Id == planId
            && p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
        if (plan is null) return (null, "WORKFORCE_PLAN_NOT_FOUND", "Workforce plan not found.");
        var candidates = plan.Lines
            .Where(l => !l.Promoted && (request.PositionCode == null || l.PositionCode == request.PositionCode))
            .ToList();
        if (candidates.Count == 0)
            return (null, "WORKFORCE_PROMOTION_ERROR",
                $"Plan {plan.Code} has no promotable lines matching the request — every matching line is already promoted (059 AC2).");

        var created = new List<Position>();
        foreach (var line in candidates)
        {
            if (db.Positions.Any(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId
                    && p.Code == line.PositionCode))
                return (null, "WORKFORCE_PROMOTION_ERROR",
                    $"Position {line.PositionCode} already exists; promotion would create a duplicate — resolve manually (059 AC2).");
            var position = new Position(numbers.Next("POS"), ec.TenantId, ec.EntityId, line.PositionCode,
                line.Title, line.OrgUnitId, line.GradeCode, null, line.Fte,
                (int)Math.Ceiling(line.Fte), PositionStatus.Open, plan.Id, line.PositionCode,
                user.Id, DateTimeOffset.UtcNow);
            db.Positions.Add(position);
            created.Add(position);
            line.Promoted = true;
            line.PromotedPositionId = position.Id;
        }
        audit.Log(user, "promote", "workforce_plan", planId,
            $"Workforce plan {plan.Code} promoted: {created.Count} position(s) created and linked ({string.Join(", ", created.Select(p => p.Code))})");
        db.SaveChanges();
        return (new
        {
            planId,
            createdPositions = created.Select(p => new { p.Id, p.Code, p.Title, p.SourcePlanId, p.SourcePlanLineCode }),
        }, null, null);
    }

    public object ListAllocationSets(CurrentUser user) =>
        db.AllocationSets.Include(s => s.Lines)
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId)
            .OrderBy(s => s.Code).ToList().Select(ProjectSet).ToList();

    public (object? data, string? code, string? message) GetAllocationSet(CurrentUser user, string setId)
    {
        var set = db.AllocationSets.Include(s => s.Lines).FirstOrDefault(s => s.Id == setId
            && s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
        return set is null ? (null, "ALLOCATION_SET_NOT_FOUND", "Allocation set not found.") : (ProjectSet(set), null, null);
    }

    public (AllocationSet? set, string? code, string? message) CreateAllocationSet(CurrentUser user, Dtos.CreateAllocationSetRequest request)
    {
        if (request.Lines.Count == 0) return (null, "VALIDATION_ERROR", "At least one allocation line is required.");
        if (db.AllocationSets.Any(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Code == request.Code))
            return (null, "ALLOCATION_SET_EXISTS", $"Allocation set {request.Code} already exists.");
        var set = new AllocationSet(numbers.Next("ALS"), ec.TenantId, ec.EntityId, request.Code, request.Name,
            request.Unit, "Draft", user.Id, DateTimeOffset.UtcNow)
        {
            Lines = request.Lines.Select(l => new AllocationLine(l.CostCenter, l.Pct)).ToList(),
        };
        db.AllocationSets.Add(set);
        DeferredUtil.PinOwned(db, ec, set.Lines);
        audit.Log(user, "create", "allocation_set", set.Id,
            $"Allocation set {request.Code} ({request.Lines.Count} line(s), unit {request.Unit}) — draft");
        db.SaveChanges();
        return (set, null, null);
    }

    // 060 AC1: the set must total 100% (configured unit) BEFORE approval; the approver is R-FIN
    // (money-adjacent) and never the maker; inactive cost objects are HELD for review on the line
    public (object? data, string? code, string? message) ApproveAllocationSet(CurrentUser approver, string setId)
    {
        if (!approver.Has("payroll:approve")) return (null, "FORBIDDEN", "payroll:approve permission required.");
        var set = db.AllocationSets.Include(s => s.Lines).FirstOrDefault(s => s.Id == setId
            && s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
        if (set is null) return (null, "ALLOCATION_SET_NOT_FOUND", "Allocation set not found.");
        if (set.Status != "Draft")
            return (null, "ALLOCATION_STATE_ERROR", $"Allocation set {setId} is {set.Status}; only Draft sets approve.");
        if (set.CreatedBy == approver.Id)
            return (null, "ALLOCATION_APPROVAL_ERROR", "Maker-checker: the set maker cannot approve their own allocation.");
        var total = set.Lines.Sum(l => l.Pct);
        if (Math.Abs(total - 100m) > 0.001m)
            return (null, "ALLOCATION_NOT_100",
                $"Allocation lines total {total:0.##} {set.Unit}; the set must total exactly 100 before approval (060 AC1).");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var line in set.Lines)
        {
            var hasActiveObject = db.PositionAssignments.Any(a => a.TenantId == ec.TenantId
                && a.LegalEntityId == ec.EntityId && a.CostCenter == line.CostCenter
                && a.Status == AssignmentStatus.Active && a.EffectiveFrom <= today
                && (a.EffectiveTo == null || a.EffectiveTo >= today));
            line.HeldForReview = !hasActiveObject; // recorded on the line, never silently dropped
        }
        db.Entry(set).Property(nameof(AllocationSet.Status)).CurrentValue = "Approved";
        audit.Log(approver, "approve", "allocation_set", setId,
            $"Allocation set {set.Code} APPROVED (Σ {total:0.##} {set.Unit}); held-for-review lines: {string.Join(", ", set.Lines.Where(l => l.HeldForReview).Select(l => l.CostCenter).DefaultIfEmpty("none"))}");
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, "ALLOCATION_STATE_ERROR", $"Allocation set {setId} was approved concurrently; the retry wrote nothing.");
        }
        return (ProjectSet(set), null, null);
    }

    // 060 AC2: payroll lines reconcile allocated subtotals to compensation within rounding
    // tolerance — per cost center: allocated pct × run net vs the workers' actual net
    public (object? data, string? code, string? message) ReconcileAllocation(CurrentUser user, string setId, string runId)
    {
        var set = db.AllocationSets.Include(s => s.Lines).FirstOrDefault(s => s.Id == setId
            && s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);
        if (set is null) return (null, "ALLOCATION_SET_NOT_FOUND", "Allocation set not found.");
        var run = db.PayRuns.Include(r => r.Lines).FirstOrDefault(r => r.Id == runId
            && r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);
        if (run is null) return (null, "PAYRUN_NOT_FOUND", "Pay run not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var costCenterByWorker = db.PositionAssignments
            .Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId
                && a.Status == AssignmentStatus.Active && a.EffectiveFrom <= today
                && (a.EffectiveTo == null || a.EffectiveTo >= today))
            .OrderBy(a => a.EffectiveFrom).ToList()
            .GroupBy(a => a.EmploymentId).ToDictionary(g => g.Key, g => g.Last().CostCenter ?? "unassigned");
        var actualByCenter = run.Lines.GroupBy(l => l.EmploymentId)
            .Select(g => (center: costCenterByWorker.GetValueOrDefault(g.Key, "unassigned"),
                net: decimal.Round(g.Sum(l => l.Kind == PayCodeKind.Earning ? l.Amount : -l.Amount), 2,
                    MidpointRounding.AwayFromZero)))
            .GroupBy(x => x.center).ToDictionary(g => g.Key, g => decimal.Round(g.Sum(x => x.net), 2));

        var buckets = set.Lines.Select(line => new
        {
            costCenter = line.CostCenter,
            allocatedPct = line.Pct,
            allocated = decimal.Round(run.NetTotal * line.Pct / 100m, 2, MidpointRounding.AwayFromZero),
            actual = actualByCenter.GetValueOrDefault(line.CostCenter, 0m),
            heldForReview = line.HeldForReview,
        }).Select(b => new
        {
            b.costCenter, b.allocatedPct, b.allocated, b.actual, b.heldForReview,
            variance = decimal.Round(b.actual - b.allocated, 2, MidpointRounding.AwayFromZero),
        }).ToList();
        return (new
        {
            setId, runId, netTotal = run.NetTotal, buckets,
            reconciles = buckets.All(b => Math.Abs(b.variance) <= 0.05m),
        }, null, null);
    }

    // 061 AC1–2: metrics are deterministic on the same data; small groups (n < k) suppress the
    // VALUE, and the complementary total suppresses too whenever any bucket was suppressed —
    // no backdoor through the aggregate. CSV export applies the SAME rule.
    public (object? data, string? code, string? message) Analytics(CurrentUser user, string metric, bool csv)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var assignments = db.PositionAssignments
            .Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId
                && a.Status == AssignmentStatus.Active && a.EffectiveFrom <= today
                && (a.EffectiveTo == null || a.EffectiveTo >= today))
            .ToList();
        var rows = assignments
            .GroupBy(a => a.CostCenter ?? "unassigned")
            .Select(g => new
            {
                key = g.Key,
                headcount = g.Select(a => a.EmploymentId).Distinct().Count(),
                fte = decimal.Round(g.Sum(a => a.Fte), 2),
            })
            .OrderBy(r => r.key).ToList();
        if (metric is not ("headcount" or "fte"))
            return (null, "METRIC_NOT_FOUND", $"Unknown metric '{metric}' — available: headcount, fte.");

        // suppressed buckets carry a NULL value — the count alone stays visible, the metric leaks nothing
        var buckets = rows.Select(r => new
        {
            key = r.key,
            count = r.headcount,
            value = r.headcount < SuppressionThreshold
                ? null
                : (object?)(metric == "headcount" ? r.headcount : (decimal?)r.fte),
            suppressed = r.headcount < SuppressionThreshold,
        }).ToList();
        var anySuppressed = buckets.Any(b => b.suppressed);
        var totalValue = metric == "headcount" ? rows.Sum(r => r.headcount) : rows.Sum(r => r.fte);

        if (csv)
        {
            var lines = new List<string> { "key,count,value" };
            foreach (var b in buckets.Where(b => !b.suppressed))
                lines.Add($"{b.key},{b.count},{FormatValue(b.value, metric)}");
            if (!anySuppressed) // complementary-cell rule: no total when any bucket was suppressed
                lines.Add($"__total__,{rows.Sum(r => r.headcount)},{FormatValue(totalValue, metric)}");
            return (string.Join("\n", lines) + "\n", null, null);
        }

        return (new
        {
            metric, asOf = today, threshold = SuppressionThreshold,
            buckets,
            total = new
            {
                value = anySuppressed ? (object)null : totalValue,
                suppressed = anySuppressed,
            },
        }, null, null);
    }

    private static string FormatValue(object? value, string metric) =>
        value is null ? "" : metric == "headcount" ? value.ToString()! : Convert.ToDecimal(value).ToString("0.##");

    private static object ProjectPlan(WorkforcePlan p) => new
    {
        p.Id, p.Code, p.Name, p.HorizonStart, p.HorizonEnd, p.OwnerUserId, p.Assumptions, p.Status,
        lines = p.Lines.Select(l => new
        {
            l.PositionCode, l.Title, l.Fte, l.OrgUnitId, l.GradeCode, l.Promoted, l.PromotedPositionId,
        }),
    };

    private static object ProjectSet(AllocationSet s) => new
    {
        s.Id, s.Code, s.Name, s.Unit, s.Status, s.CreatedBy,
        lines = s.Lines.Select(l => new { l.CostCenter, l.Pct, l.HeldForReview }),
    };
}
