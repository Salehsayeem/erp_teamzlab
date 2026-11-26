using Erp.Api.Interfaces;

namespace Erp.Api.Data.Repositories;

public sealed class SupplierScorecardsRepository(ErpDbContext db, EntityContext ec) : ISupplierScorecardsRepository
{
    private IQueryable<SupplierScorecard> Scoped() =>
        db.SupplierScorecards.Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId);

    public SupplierScorecard? Find(string id) => Scoped().FirstOrDefault(s => s.Id == id);

    public List<SupplierScorecard> ForSupplier(string supplierId) =>
        Scoped().Where(s => s.SupplierId == supplierId).OrderBy(s => s.CreatedAt).ToList();

    public void Add(SupplierScorecard scorecard) => db.SupplierScorecards.Add(scorecard);

    public void AddAction(CorrectiveAction action) => db.CorrectiveActions.Add(action);

    public CorrectiveAction? FindAction(string id) =>
        db.CorrectiveActions.FirstOrDefault(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId && a.Id == id);

    public void SetField(object entity, string name, object value) =>
        db.Entry(entity).Property(name).CurrentValue = value;

    public int Save() => db.SaveChanges();
}
