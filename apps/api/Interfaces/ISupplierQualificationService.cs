using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// P5 supplier qualification (040–044, 061): versioned questionnaires with reviewer returns,
// expiry-gated evidence, provenance-carrying risk profiles, screening stub with human
// disposition queue, and performance observations with disputes.
public interface ISupplierQualificationService
{
    (SupplierQuestionnaire? Questionnaire, string? Code, string? Message) CreateQuestionnaire(CurrentUser u, string? name, IReadOnlyList<QuestionnaireQuestionInput>? questions);
    (QuestionnaireResponse? Response, string? Code, string? Message) CreateResponse(CurrentUser u, string supplierId, string? questionnaireId, string? answersJson);
    (QuestionnaireResponse? Response, string? Code, string? Message) ReviewResponse(CurrentUser actor, string responseId, string? reviewDecision, string? fieldCommentsJson);
    (SupplierEvidence? Evidence, string? Code, string? Message) CreateEvidence(CurrentUser u, string supplierId, string? evidenceType, string? metadataJson, DateOnly? expiresOn, string? supersedesId);
    object Evidences(string supplierId);
    object Qualification(string supplierId);
    (ScreeningResult? Screening, string? Code, string? Message) CreateScreening(CurrentUser u, string supplierId, string? source, string? sourceRef);
    List<ScreeningResult> ScreeningsFor(string supplierId);
    (ScreeningResult? Screening, string? Code, string? Message) DisposeScreening(CurrentUser actor, string screeningId, string? disposition, string? reason);
    (SupplierObservation? Observation, string? Code, string? Message) CreateObservation(CurrentUser u, string supplierId, string? metric, decimal value, string? period, string? source);
    (SupplierObservation? Observation, string? Code, string? Message) DisputeObservation(CurrentUser actor, string observationId, string? reason, decimal? amendedValue);
    object RiskProfile(string supplierId);
}
