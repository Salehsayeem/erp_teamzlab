using System.Text.Json;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P5 (040–044, 061). The screening adapter is a documented STUB: outcome derives from the source
// tag (a "Watchlist"-style source yields PossibleMatch; a source tagged UNAVAILABLE simulates an
// outage and yields Unknown per the fail/hold policy). Possible matches queue for a human
// disposition — the stub never auto-confirms. Risk rule v1: 25×possible + 20×unknown + 10 when
// no observations; ≥50 High → ReviewRequired (never silently excluded).
public sealed class SupplierQualificationService(
    ISupplierQualificationRepository qualification, INumberSeries numbers, IAuditRepository audit,
    EntityContext ec) : ISupplierQualificationService
{
    private const int RiskRuleVersion = 1;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public (SupplierQuestionnaire? Questionnaire, string? Code, string? Message) CreateQuestionnaire(
        CurrentUser u, string? name, IReadOnlyList<QuestionnaireQuestionInput>? questions)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, "VALIDATION_ERROR", "Questionnaire name is required.");
        if (questions is null || questions.Count == 0)
            return (null, "VALIDATION_ERROR", "At least one question is required.");
        var questionnaire = new SupplierQuestionnaire(numbers.Next("QNR"), ec.TenantId, ec.EntityId, name.Trim(),
            1, JsonSerializer.Serialize(questions, JsonOpts), u.Id, DateTimeOffset.UtcNow);
        qualification.AddQuestionnaire(questionnaire);
        audit.Log(u, "create", "supplier_questionnaire", questionnaire.Id, $"Questionnaire '{name}' v1 with {questions.Count} question(s)");
        qualification.Save();
        return (questionnaire, null, null);
    }

    public (QuestionnaireResponse? Response, string? Code, string? Message) CreateResponse(
        CurrentUser u, string supplierId, string? questionnaireId, string? answersJson)
    {
        var questionnaire = qualification.FindQuestionnaire(questionnaireId ?? "");
        if (questionnaire is null)
            return (null, "QUESTIONNAIRE_NOT_FOUND", "Questionnaire does not exist.");
        if (string.IsNullOrWhiteSpace(answersJson))
            return (null, "VALIDATION_ERROR", "Answers are required.");
        // 040 AC1: the response pins the questionnaire version it answered
        var response = new QuestionnaireResponse($"QRR-{DateTimeOffset.UtcNow.Ticks}", ec.TenantId, ec.EntityId,
            supplierId, questionnaire.Id, questionnaire.Version, answersJson, null, null, null, null,
            u.Id, DateTimeOffset.UtcNow);
        qualification.AddResponse(response);
        audit.Log(u, "create", "questionnaire_response", response.Id,
            $"Response to {questionnaire.Id} v{questionnaire.Version} recorded for {supplierId}");
        qualification.Save();
        return (response, null, null);
    }

    public (QuestionnaireResponse? Response, string? Code, string? Message) ReviewResponse(
        CurrentUser actor, string responseId, string? reviewDecision, string? fieldCommentsJson)
    {
        var response = qualification.FindResponse(responseId);
        if (response is null) return (null, "QUESTIONNAIRE_NOT_FOUND", "Response not found.");
        if (!Enum.TryParse<ResponseReviewDecision>(reviewDecision, out var decision) || reviewDecision is null
            || !Enum.IsDefined(decision))
            return (null, "VALIDATION_ERROR", "Review decision must be Approved or Returned.");
        if (response.ReviewDecision is not null)
            return (null, "RESPONSE_STATE_ERROR", "This response has already been reviewed.");
        // 040 AC3: a return must carry field-level comments
        if (decision is ResponseReviewDecision.Returned && string.IsNullOrWhiteSpace(fieldCommentsJson))
            return (null, "VALIDATION_ERROR", "Returning a response requires field-level comments (040 AC3).");

        qualification.SetField(response, nameof(QuestionnaireResponse.ReviewDecision), decision.ToString());
        qualification.SetField(response, nameof(QuestionnaireResponse.FieldCommentsJson), fieldCommentsJson);
        qualification.SetField(response, nameof(QuestionnaireResponse.ReviewedBy), actor.Id);
        qualification.SetField(response, nameof(QuestionnaireResponse.ReviewedAt), DateTimeOffset.UtcNow);
        audit.Log(actor, "review", "questionnaire_response", response.Id, $"Response {responseId} {decision} by {actor.Email}");
        qualification.Save();
        return (response, null, null);
    }

    // 041 AC2/AC3: an expired document fails the gate absent policy grace; a replacement points
    // back at the prior row, which keeps its version + verification untouched.
    public (SupplierEvidence? Evidence, string? Code, string? Message) CreateEvidence(
        CurrentUser u, string supplierId, string? evidenceType, string? metadataJson, DateOnly? expiresOn, string? supersedesId)
    {
        if (string.IsNullOrWhiteSpace(evidenceType))
            return (null, "VALIDATION_ERROR", "Evidence type is required.");
        if (supersedesId is { })
        {
            var prior = qualification.FindEvidence(supersedesId);
            if (prior is null || prior.SupplierId != supplierId)
                return (null, "VALIDATION_ERROR", "Superseded evidence must belong to the same supplier.");
        }
        var evidence = new SupplierEvidence(numbers.Next("EVD"), ec.TenantId, ec.EntityId, supplierId,
            evidenceType.Trim(), metadataJson ?? "{}", expiresOn, supersedesId, u.Id, DateTimeOffset.UtcNow,
            u.Id, DateTimeOffset.UtcNow);
        qualification.AddEvidence(evidence);
        audit.Log(u, "create", "supplier_evidence", evidence.Id,
            $"Evidence {evidence.Id} ({evidenceType}) recorded for {supplierId}" +
            (expiresOn is { } e ? $", expires {e:yyyy-MM-dd}" : "") + (supersedesId is { } s ? $", supersedes {s}" : ""));
        qualification.Save();
        return (evidence, null, null);
    }

    public object Evidences(string supplierId) => qualification.EvidencesFor(supplierId).Select(e => new
    {
        e.Id, e.SupplierId, e.EvidenceType, e.MetadataJson, e.ExpiresOn, e.SupersedesId,
        e.VerifiedBy, e.VerifiedAt, e.CreatedBy, e.CreatedAt,
    }).ToList();

    public object Qualification(string supplierId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var evidences = qualification.EvidencesFor(supplierId);
        var superseded = evidences.Where(e => e.SupersedesId is not null).Select(e => e.SupersedesId!).ToHashSet();
        var expired = evidences
            .Where(e => e.ExpiresOn is { } exp && exp < today && !superseded.Contains(e.Id))
            .Select(e => new { e.Id, e.EvidenceType, e.ExpiresOn })
            .ToList();
        return new
        {
            supplierId, eligible = expired.Count == 0, expiredEvidences = expired,
            policyGrace = (bool?)null, checkedAt = DateTimeOffset.UtcNow,
        };
    }

    public List<ScreeningResult> ScreeningsFor(string supplierId) => qualification.ScreeningsFor(supplierId).Select(s => s).ToList();

    public (ScreeningResult? Screening, string? Code, string? Message) CreateScreening(
        CurrentUser u, string supplierId, string? source, string? sourceRef)
    {
        if (string.IsNullOrWhiteSpace(source))
            return (null, "VALIDATION_ERROR", "Screening source is required.");
        var outcome = source.Contains("UNAVAILABLE", StringComparison.Ordinal)
            ? ScreeningOutcome.Unknown
            : source.Contains("Watchlist", StringComparison.OrdinalIgnoreCase) || source.Contains("MATCH", StringComparison.OrdinalIgnoreCase)
                ? ScreeningOutcome.PossibleMatch
                : ScreeningOutcome.Clear;
        var status = outcome switch
        {
            ScreeningOutcome.PossibleMatch => "PendingDisposition", // queued for human disposition (043 AC2)
            ScreeningOutcome.Unknown => "Unknown",                  // outage → unknown (043 AC3)
            _ => "AutoCleared",
        };
        var screening = new ScreeningResult($"SCRN-{DateTimeOffset.UtcNow.Ticks}", ec.TenantId, ec.EntityId,
            supplierId, source, outcome, status, sourceRef, null, null, null, u.Id, DateTimeOffset.UtcNow);
        qualification.AddScreening(screening);
        audit.Log(u, "create", "screening_result", screening.Id, $"Screening {screening.Id} ({source}): {outcome} / {status}");
        qualification.Save();
        return (screening, null, null);
    }

    public (ScreeningResult? Screening, string? Code, string? Message) DisposeScreening(
        CurrentUser actor, string screeningId, string? disposition, string? reason)
    {
        var screening = qualification.FindScreening(screeningId);
        if (screening is null) return (null, "SCREENING_NOT_FOUND", "Screening not found.");
        if (!Enum.TryParse<ScreeningDisposition>(disposition, out var outcome) || disposition is null || !Enum.IsDefined(outcome))
            return (null, "VALIDATION_ERROR", "Disposition must be ConfirmedClear, FalsePositive or ConfirmedMatch.");
        if (screening.Status is not "PendingDisposition")
            return (null, "SCREENING_STATE_ERROR", "Only queued possible matches take a human disposition.");
        qualification.SetField(screening, nameof(ScreeningResult.Status), "Disposed");
        qualification.SetField(screening, nameof(ScreeningResult.Disposition), outcome.ToString());
        qualification.SetField(screening, nameof(ScreeningResult.DispositionBy), actor.Id);
        qualification.SetField(screening, nameof(ScreeningResult.DispositionAt), DateTimeOffset.UtcNow);
        audit.Log(actor, "dispose", "screening_result", screeningId, $"Screening {screeningId} → {outcome} by {actor.Email}: {reason}");
        qualification.Save();
        return (screening, null, null);
    }

    public (SupplierObservation? Observation, string? Code, string? Message) CreateObservation(
        CurrentUser u, string supplierId, string? metric, decimal value, string? period, string? source)
    {
        if (string.IsNullOrWhiteSpace(metric) || string.IsNullOrWhiteSpace(period))
            return (null, "VALIDATION_ERROR", "Metric and period are required (061 AC1).");
        var observation = new SupplierObservation(numbers.Next("OBS"), ec.TenantId, ec.EntityId, supplierId,
            metric.Trim(), value, period.Trim(), source ?? "manual", null, null, null, null, u.Id, DateTimeOffset.UtcNow);
        qualification.AddObservation(observation);
        audit.Log(u, "create", "supplier_observation", observation.Id, $"Observation {observation.Id}: {metric}={value} ({period})");
        qualification.Save();
        return (observation, null, null);
    }

    // 061: the dispute retains the original value and records the amendment with actor history
    public (SupplierObservation? Observation, string? Code, string? Message) DisputeObservation(
        CurrentUser actor, string observationId, string? reason, decimal? amendedValue)
    {
        var observation = qualification.FindObservation(observationId);
        if (observation is null) return (null, "OBSERVATION_NOT_FOUND", "Observation not found.");
        if (string.IsNullOrWhiteSpace(reason) || amendedValue is null)
            return (null, "VALIDATION_ERROR", "A dispute needs a reason and an amended value (061 AC2).");
        if (observation.DisputedBy is not null)
            return (null, "OBSERVATION_STATE_ERROR", "This observation is already disputed.");
        qualification.SetField(observation, nameof(SupplierObservation.DisputeReason), reason.Trim());
        qualification.SetField(observation, nameof(SupplierObservation.AmendedValue), amendedValue);
        qualification.SetField(observation, nameof(SupplierObservation.DisputedBy), actor.Id);
        qualification.SetField(observation, nameof(SupplierObservation.DisputedAt), DateTimeOffset.UtcNow);
        audit.Log(actor, "dispute", "supplier_observation", observationId,
            $"Observation {observationId} disputed: {observation.Value:0.##} → {amendedValue:0.##} by {actor.Email}");
        qualification.Save();
        return (observation, null, null);
    }

    public object RiskProfile(string supplierId)
    {
        var screenings = qualification.ScreeningsFor(supplierId);
        var observations = qualification.ObservationsFor(supplierId);
        var possible = screenings.Count(s => s.Outcome is ScreeningOutcome.PossibleMatch);
        var unknown = screenings.Count(s => s.Outcome is ScreeningOutcome.Unknown);
        var score = 25m * possible + 20m * unknown + (observations.Count == 0 ? 10m : 0m);
        var band = score >= 50m ? "High" : score >= 20m ? "Medium" : "Low";
        var disposition = band is "High" ? "ReviewRequired" : band is "Medium" ? "Monitor" : "None";
        var profile = new SupplierRiskProfile(supplierId, ec.TenantId, ec.EntityId, supplierId, score, band,
            JsonSerializer.Serialize(new { possibleMatches = possible, unknownOutcomes = unknown, observations = observations.Count }, JsonOpts),
            RiskRuleVersion, DateTimeOffset.UtcNow, disposition, Stale: false);
        qualification.UpsertRiskProfile(profile); // recompute-or-stale policy: recompute on read
        qualification.Save();
        return new
        {
            supplierId, score, band, disposition, ruleVersion = RiskRuleVersion, stale = false,
            sources = profile.SourcesJson, computedAt = profile.ComputedAt,
        };
    }
}
