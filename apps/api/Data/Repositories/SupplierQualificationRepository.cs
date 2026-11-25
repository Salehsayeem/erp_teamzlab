using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// P5 qualification reads/writes — tenant+entity scoped on every query (R14).
public sealed class SupplierQualificationRepository(ErpDbContext db, EntityContext ec) : ISupplierQualificationRepository
{
    private IQueryable<SupplierEvidence> EvidencesScoped() =>
        db.SupplierEvidences.Where(e => e.TenantId == ec.TenantId && e.LegalEntityId == ec.EntityId);

    private IQueryable<ScreeningResult> ScreeningsScoped() =>
        db.ScreeningResults.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    private IQueryable<SupplierObservation> ObservationsScoped() =>
        db.SupplierObservations.Where(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId);

    public SupplierQuestionnaire? FindQuestionnaire(string id) =>
        db.SupplierQuestionnaires.FirstOrDefault(q => q.TenantId == ec.TenantId && q.LegalEntityId == ec.EntityId && q.Id == id);

    public void AddQuestionnaire(SupplierQuestionnaire questionnaire) => db.SupplierQuestionnaires.Add(questionnaire);

    public void AddResponse(QuestionnaireResponse response) => db.QuestionnaireResponses.Add(response);

    public QuestionnaireResponse? FindResponse(string id) =>
        db.QuestionnaireResponses.FirstOrDefault(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId && r.Id == id);

    public void AddEvidence(SupplierEvidence evidence) => db.SupplierEvidences.Add(evidence);

    public SupplierEvidence? FindEvidence(string id) => EvidencesScoped().FirstOrDefault(e => e.Id == id);

    public List<SupplierEvidence> EvidencesFor(string supplierId) =>
        EvidencesScoped().Where(e => e.SupplierId == supplierId).OrderBy(e => e.CreatedAt).ToList();

    public void AddScreening(ScreeningResult screening) => db.ScreeningResults.Add(screening);

    public ScreeningResult? FindScreening(string id) => ScreeningsScoped().FirstOrDefault(s => s.Id == id);

    public List<ScreeningResult> ScreeningsFor(string supplierId) =>
        ScreeningsScoped().Where(s => s.SupplierId == supplierId).OrderBy(s => s.CreatedAt).ToList();

    public void AddObservation(SupplierObservation observation) => db.SupplierObservations.Add(observation);

    public SupplierObservation? FindObservation(string id) => ObservationsScoped().FirstOrDefault(o => o.Id == id);

    public List<SupplierObservation> ObservationsFor(string supplierId) =>
        ObservationsScoped().Where(o => o.SupplierId == supplierId).OrderBy(o => o.CreatedAt).ToList();

    public SupplierRiskProfile? FindRiskProfile(string supplierId) =>
        db.SupplierRiskProfiles.FirstOrDefault(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId && p.SupplierId == supplierId);

    public void UpsertRiskProfile(SupplierRiskProfile profile)
    {
        var existing = FindRiskProfile(profile.SupplierId);
        if (existing is null) db.SupplierRiskProfiles.Add(profile);
        else foreach (var prop in typeof(SupplierRiskProfile).GetProperties())
                if (prop.Name is not ("Id" or "TenantId" or "LegalEntityId" or "SupplierId"))
                    db.Entry(existing).Property(prop.Name).CurrentValue = prop.GetValue(profile)!;
    }

    public void SetField(object entity, string name, object value) =>
        db.Entry(entity).Property(name).CurrentValue = value;

    public int Save() => db.SaveChanges();
}
