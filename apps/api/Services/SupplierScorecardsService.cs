using System.Text.Json;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P5 scorecards (062). Metric summary aggregates observations in the period by metric name; fewer
// than 3 observations → NotRated (062 edge). Closure of a corrective action needs an internal
// reviewer outcome (suppliers:review) and is maker-checked against the action's creator.
public sealed class SupplierScorecardsService(
    ISupplierScorecardsRepository scorecards, ISupplierQualificationRepository qualification,
    ISupplierRepository suppliers, IAuditRepository audit, EntityContext ec) : ISupplierScorecardsService
{
    private const int CalcVersion = 1;
    private const int MinRatedObservations = 3;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public (SupplierScorecard? Scorecard, string? Code, string? Message) Create(
        CurrentUser u, string? supplierId, DateOnly periodFrom, DateOnly periodTo)
    {
        if (!suppliers.Exists(supplierId ?? ""))
            return (null, "SUPPLIER_NOT_FOUND", "Supplier does not exist.");
        if (periodTo <= periodFrom)
            return (null, "VALIDATION_ERROR", "Period must end after it starts.");
        var id = $"{supplierId}:{periodFrom:yyyyMMdd}:{periodTo:yyyyMMdd}";
        if (scorecards.Find(id) is not null)
            return (null, "SCORECARD_STATE_ERROR", $"A scorecard for {supplierId} over this period already exists.");

        var inPeriod = qualification.ObservationsFor(supplierId!)
            .Where(o => o.CreatedAt.UtcDateTime >= periodFrom.ToDateTime(TimeOnly.MinValue)
                     && o.CreatedAt.UtcDateTime <= periodTo.ToDateTime(TimeOnly.MaxValue))
            .ToList();
        var rated = inPeriod.Count >= MinRatedObservations;
        var summary = inPeriod
            .GroupBy(o => o.Metric)
            .ToDictionary(g => g.Key, g => new
            {
                definition = $"{g.Key}: mean of recorded observation values in period",
                count = g.Count(),
                mean = decimal.Round(g.Average(o => o.DisputedBy is null ? o.Value : o.AmendedValue ?? o.Value), 2, MidpointRounding.AwayFromZero),
                disputed = g.Count(o => o.DisputedBy is null) != g.Count(),
            });
        var scorecard = new SupplierScorecard(id, ec.TenantId, ec.EntityId, supplierId!, periodFrom.ToString("yyyy-MM-dd"),
            periodTo.ToString("yyyy-MM-dd"), rated ? ScorecardStatus.Rated : ScorecardStatus.NotRated,
            JsonSerializer.Serialize(summary, JsonOpts), CalcVersion, null, u.Id, DateTimeOffset.UtcNow);
        scorecards.Add(scorecard);
        audit.Log(u, "create", "supplier_scorecard", id,
            $"Scorecard for {supplierId} {periodFrom:yyyy-MM-dd}..{periodTo:yyyy-MM-dd}: {scorecard.Status} ({inPeriod.Count} observations, calc v{CalcVersion})");
        scorecards.Save();
        return (scorecard, null, null);
    }

    public object ForSupplier(string supplierId) => scorecards.ForSupplier(supplierId).Select(Proj).ToList();

    public object? Detail(string id)
    {
        var scorecard = scorecards.Find(id);
        return scorecard is null ? null : Proj(scorecard);
    }

    public (SupplierScorecard? Scorecard, string? Code, string? Message) AddSupplierResponse(
        CurrentUser u, string id, string? response)
    {
        var scorecard = scorecards.Find(id);
        if (scorecard is null) return (null, "SCORECARD_NOT_FOUND", "Scorecard not found.");
        if (string.IsNullOrWhiteSpace(response))
            return (null, "VALIDATION_ERROR", "The supplier response text is required.");
        if (scorecard.SupplierResponse is not null)
            return (null, "SCORECARD_STATE_ERROR", "A supplier response is already on record for this scorecard.");
        scorecards.SetField(scorecard, nameof(SupplierScorecard.SupplierResponse), response.Trim());
        audit.Log(u, "record-response", "supplier_scorecard", id, $"Supplier response recorded on {id} by {u.Email}");
        scorecards.Save();
        return (scorecard, null, null);
    }

    public (CorrectiveAction? Action, string? Code, string? Message) AddAction(
        CurrentUser u, string scorecardId, string? finding, string? action)
    {
        var scorecard = scorecards.Find(scorecardId);
        if (scorecard is null) return (null, "SCORECARD_NOT_FOUND", "Scorecard not found.");
        if (string.IsNullOrWhiteSpace(finding) || string.IsNullOrWhiteSpace(action))
            return (null, "VALIDATION_ERROR", "Finding and action are required.");
        var corrective = new CorrectiveAction($"{scorecardId}:ca{DateTimeOffset.UtcNow.Ticks}", ec.TenantId, ec.EntityId,
            scorecardId, finding.Trim(), action.Trim(), "Open", null, null, null, u.Id, DateTimeOffset.UtcNow);
        scorecards.AddAction(corrective);
        audit.Log(u, "create", "corrective_action", corrective.Id, $"Corrective action {corrective.Id} opened on {scorecardId}");
        scorecards.Save();
        return (corrective, null, null);
    }

    // 062 AC3: closure requires an internal reviewer outcome, by someone other than the creator
    public (CorrectiveAction? Action, string? Code, string? Message) CloseAction(
        CurrentUser actor, string actionId, string? outcome)
    {
        var action = scorecards.FindAction(actionId);
        if (action is null) return (null, "CORRECTIVE_ACTION_NOT_FOUND", "Corrective action not found.");
        if (!actor.Has("suppliers:review"))
            return (null, "FORBIDDEN", "suppliers:review permission required to close a corrective action.");
        if (action.CreatedBy == actor.Id)
            return (null, "CORRECTIVE_ACTION_STATE_ERROR", "Maker-checker: the action creator cannot close their own action.");
        if (action.Status is "Closed")
            return (null, "CORRECTIVE_ACTION_STATE_ERROR", "Corrective action is already closed.");
        if (string.IsNullOrWhiteSpace(outcome))
            return (null, "REVIEW_OUTCOME_REQUIRED", "Closure requires an internal reviewer outcome (062 AC3).");
        scorecards.SetField(action, nameof(CorrectiveAction.Status), "Closed");
        scorecards.SetField(action, nameof(CorrectiveAction.Outcome), outcome.Trim());
        scorecards.SetField(action, nameof(CorrectiveAction.ClosedBy), actor.Id);
        scorecards.SetField(action, nameof(CorrectiveAction.ClosedAt), DateTimeOffset.UtcNow);
        audit.Log(actor, "close", "corrective_action", actionId, $"Corrective action {actionId} closed by {actor.Email}: {outcome}");
        scorecards.Save();
        return (action, null, null);
    }

    private static object Proj(SupplierScorecard s) => new
    {
        s.Id, s.SupplierId, s.PeriodFrom, s.PeriodTo, s.Status, s.CalcVersion,
        metrics = s.MetricSummaryJson is null ? (JsonElement?)null : JsonDocument.Parse(s.MetricSummaryJson).RootElement.Clone(),
        s.SupplierResponse, s.CreatedBy, s.CreatedAt,
    };
}
