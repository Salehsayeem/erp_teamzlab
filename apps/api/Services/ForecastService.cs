using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Forecast categories + snapshots (ERP-SALES-018/020): unweighted pipeline vs weighted
// (stage-weighted) vs submitted owner override, with immutable synchronous snapshots
// (Q-S7: on-demand generation until the jobs infra lands).
public sealed class ForecastService(
    IOpportunityRepository opportunities, IIdentityRepository identities,
    IFinanceSettingRepository settings, IAuditRepository audit, EntityContext ec, ErpDbContext db) : IForecastService
{
    // 018 AC2: submission records owner/period/time/version — a resubmission bumps Version
    public (object? data, bool resubmitted, string? code, string? message) Submit(CurrentUser u, string owner, string period, ForecastCategory category, decimal? overrideValue)
    {
        if (identities.FindActiveUserById(owner) is null)
            return (null, false, "VALIDATION_ERROR", "Owner must be an active user.");
        if (!PeriodRegex(period)) return (null, false, "VALIDATION_ERROR", "Period must be \"YYYY-MM\".");
        if (!u.Has("forecasts:write")) return (null, false, "FORBIDDEN", "forecasts:write permission required.");
        var id = $"{owner}:{period}:{category}";
        var existing = db.ForecastSubmissions.FirstOrDefault(s =>
            s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Id == id);
        if (existing is not null)
        {
            db.Entry(existing).Property(nameof(ForecastSubmission.OverrideValue)).CurrentValue = overrideValue;
            db.Entry(existing).Property(nameof(ForecastSubmission.Version)).CurrentValue = existing.Version + 1;
            db.Entry(existing).Property(nameof(ForecastSubmission.SubmittedBy)).CurrentValue = u.Id;
            db.Entry(existing).Property(nameof(ForecastSubmission.SubmittedAt)).CurrentValue = DateTimeOffset.UtcNow;
            audit.Log(u, "resubmit", "forecast_submission", id, $"Version {existing.Version} (override {overrideValue:0.00})");
            db.SaveChanges();
            return (SubmissionView(existing), true, null, null);
        }
        var submission = new ForecastSubmission(id, ec.TenantId, ec.EntityId, owner, period, category,
            overrideValue, 1, u.Id, DateTimeOffset.UtcNow);
        db.ForecastSubmissions.Add(submission);
        audit.Log(u, "submit", "forecast_submission", id, $"Category {category} (override {overrideValue:0.00})");
        db.SaveChanges();
        return (SubmissionView(submission), false, null, null);
    }

    // 018 AC1: buckets reported separately; opportunities whose owner has no submission for
    // the period are counted explicitly as unclassified
    public (object? data, string? code, string? message) Report(CurrentUser u, string period)
    {
        if (!PeriodRegex(period)) return (null, "VALIDATION_ERROR", "Period must be \"YYYY-MM\".");
        if (!u.Has("forecasts:read")) return (null, "FORBIDDEN", "forecasts:read permission required.");
        var weights = StageWeights();
        var open = opportunities.All().Where(o => o.Status == OpportunityStatus.Open).ToList();
        var submissions = db.ForecastSubmissions
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Period == period).ToList();
        var ownersWithSubmission = submissions.Select(s => s.OwnerId).ToHashSet();
        var unclassified = open.Where(o => !ownersWithSubmission.Contains(o.OwnerId)).ToList();

        return (new
        {
            period,
            unweighted = new
            {
                count = open.Count,
                total = decimal.Round(open.Sum(OpportunityValue), 2, MidpointRounding.AwayFromZero),
            },
            weighted = new
            {
                total = decimal.Round(open.Sum(o => OpportunityValue(o) * weights.GetValueOrDefault(o.Stage, 0m)), 2, MidpointRounding.AwayFromZero),
            },
            @override = new
            {
                count = submissions.Count,
                total = decimal.Round(submissions.Sum(s => s.OverrideValue ?? 0), 2, MidpointRounding.AwayFromZero),
            },
            submissions = submissions.Select(SubmissionView),
            unclassified = new { count = unclassified.Count },
        }, null, null);
    }

    // 020: synchronous freeze — payload carries included ids/values/categories/generatedAt;
    // rows are immutable (later edits never move them); failures store an Incomplete row
    public (object? data, string? code, string? message) CreateSnapshot(CurrentUser u, string period)
    {
        if (!PeriodRegex(period)) return (null, "VALIDATION_ERROR", "Period must be \"YYYY-MM\".");
        if (!u.Has("forecasts:write")) return (null, "FORBIDDEN", "forecasts:write permission required.");
        var snapshot = new ForecastSnapshot($"SNAP-{period}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            ec.TenantId, ec.EntityId, period, SnapshotStatus.Complete, "", u.Id, DateTimeOffset.UtcNow);
        try
        {
            var open = opportunities.All().Where(o => o.Status == OpportunityStatus.Open).ToList();
            var payload = new
            {
                includedIds = open.Select(o => o.Id),
                values = open.Select(o => new { o.Id, value = OpportunityValue(o), stage = o.Stage, owner = o.OwnerId }),
                unweightedTotal = decimal.Round(open.Sum(OpportunityValue), 2, MidpointRounding.AwayFromZero),
                generatedAt = DateTimeOffset.UtcNow,
            };
            snapshot = snapshot with { PayloadJson = JsonSerializer.Serialize(payload, Idem.JsonOptions) };
            db.ForecastSnapshots.Add(snapshot);
            audit.Log(u, "snapshot", "forecast", period, $"Snapshot frozen ({open.Count} opportunities)");
            db.SaveChanges();
            return (SnapshotView(snapshot), null, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // 020 edge: a failed generation is recorded as Incomplete, never silently dropped
            snapshot = snapshot with { Status = SnapshotStatus.Incomplete };
            db.ForecastSnapshots.Add(snapshot);
            audit.Log(u, "snapshot_failed", "forecast", period, $"Snapshot generation failed: {ex.GetType().Name}");
            db.SaveChanges();
            return (SnapshotView(snapshot), "FORECAST_SNAPSHOT_INCOMPLETE", "Snapshot generation failed — recorded as Incomplete.");
        }
    }

    public (object? data, string? code, string? message) Snapshots(string period)
    {
        var rows = db.ForecastSnapshots
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Period == period)
            .OrderBy(s => s.GeneratedAt).ToList();
        return (rows.Select(SnapshotView).ToList(), null, null);
    }

    private Dictionary<string, decimal> StageWeights()
    {
        var raw = settings.Get("sales.stage_weights")?.Value;
        if (raw is null) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, decimal>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return []; // unparseable weights → weight 0 everywhere, never a wrong forecast
        }
    }

    private static bool PeriodRegex(string period) =>
        period.Length == 7 && period[4] == '-' && int.TryParse(period.AsSpan(0, 4), out var y) && y > 0
        && int.TryParse(period.AsSpan(5, 2), out var m) && m is >= 1 and <= 12;

    // 017 AC1: the opportunity value is the sum of its lines — same math as the opp list
    private static decimal OpportunityValue(Opportunity o) =>
        o.Lines.Sum(l => decimal.Round(l.Qty * l.TargetPrice, 2, MidpointRounding.AwayFromZero));

    private static object SubmissionView(ForecastSubmission s) => new
    {
        s.Id, owner = s.OwnerId, s.Period, s.Category, s.OverrideValue, s.Version, submittedBy = s.SubmittedBy, s.SubmittedAt,
    };

    private static object SnapshotView(ForecastSnapshot s) => new
    {
        s.Id, s.Period, s.Status, payload = JsonDocument.Parse(string.IsNullOrWhiteSpace(s.PayloadJson)
            ? "{}" : s.PayloadJson).RootElement.Clone(), s.GeneratedBy, s.GeneratedAt,
    };
}
