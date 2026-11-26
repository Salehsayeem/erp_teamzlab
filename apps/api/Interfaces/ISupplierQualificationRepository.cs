using Erp.Api;

namespace Erp.Api.Interfaces;

public interface ISupplierQualificationRepository
{
    SupplierQuestionnaire? FindQuestionnaire(string id);
    void AddQuestionnaire(SupplierQuestionnaire questionnaire);
    void AddResponse(QuestionnaireResponse response);
    QuestionnaireResponse? FindResponse(string id);
    void AddEvidence(SupplierEvidence evidence);
    SupplierEvidence? FindEvidence(string id);
    List<SupplierEvidence> EvidencesFor(string supplierId);
    void AddScreening(ScreeningResult screening);
    ScreeningResult? FindScreening(string id);
    List<ScreeningResult> ScreeningsFor(string supplierId);
    void AddObservation(SupplierObservation observation);
    SupplierObservation? FindObservation(string id);
    List<SupplierObservation> ObservationsFor(string supplierId);
    SupplierRiskProfile? FindRiskProfile(string supplierId);
    void UpsertRiskProfile(SupplierRiskProfile profile);
    void SetField(object entity, string name, object value);
    int Save();
}
