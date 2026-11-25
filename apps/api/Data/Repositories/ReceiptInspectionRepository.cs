using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// P4 receipt-workspace reads/writes — tenant+entity scoped on every query (R14).
public sealed class ReceiptInspectionRepository(ErpDbContext db, EntityContext ec) : IReceiptInspectionRepository
{
    private IQueryable<Receipt> ReceiptsScoped() =>
        db.Receipts.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    private IQueryable<ReceiptInspection> InspectionsScoped() =>
        db.ReceiptInspections.Where(i => i.TenantId == ec.TenantId && i.LegalEntityId == ec.EntityId);

    private IQueryable<SupplierReturn> ReturnsScoped() =>
        db.SupplierReturns.Where(r => r.TenantId == ec.TenantId && r.LegalEntityId == ec.EntityId);

    public Receipt? FindReceipt(string receiptId) => ReceiptsScoped().FirstOrDefault(r => r.Id == receiptId);
    public List<Receipt> AllReceipts() => ReceiptsScoped().OrderBy(r => r.Id).ToList();
    public List<Receipt> ReceiptsForPo(string poId) => ReceiptsScoped().Where(r => r.PurchaseOrderId == poId).ToList();

    public void AddDispatch(Dispatch dispatch) => db.Dispatches.Add(dispatch);

    public List<Dispatch> DispatchesFor(string poId) =>
        db.Dispatches.Where(d => d.TenantId == ec.TenantId && d.LegalEntityId == ec.EntityId && d.PoId == poId)
            .OrderBy(d => d.SentAt).ToList();

    public void AddAsn(AdvanceShipNotice asn)
    {
        db.AdvanceShipNotices.Add(asn);
        foreach (var line in asn.Lines) SetLineTenant(line);
    }

    public AdvanceShipNotice? FindAsn(string id) =>
        db.AdvanceShipNotices.Include(a => a.Lines).FirstOrDefault(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId && a.Id == id);

    public List<AdvanceShipNotice> Asns(AsnStatus? status)
    {
        var query = db.AdvanceShipNotices.Include(a => a.Lines)
            .Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId).AsQueryable();
        if (status is { } s) query = query.Where(a => a.Status == s);
        return query.OrderBy(a => a.Id).ToList();
    }

    public void AddInspection(ReceiptInspection inspection) => db.ReceiptInspections.Add(inspection);

    public ReceiptInspection? FindInspection(string id) => InspectionsScoped().FirstOrDefault(i => i.Id == id);

    public List<ReceiptInspection> InspectionsForPo(string poId) => InspectionsScoped().Where(i => i.PoId == poId).ToList();

    public List<ReceiptInspection> InspectionsForReceipt(string receiptId) =>
        InspectionsScoped().Where(i => i.ReceiptId == receiptId).OrderBy(i => i.Id).ToList();

    public void AddReturn(SupplierReturn ret) => db.SupplierReturns.Add(ret);

    public List<SupplierReturn> ReturnsForPo(string poId) => ReturnsScoped().Where(r => r.PoId == poId).ToList();

    public List<SupplierReturn> ReturnsForReceipt(string receiptId) =>
        ReturnsScoped().Where(r => r.ReceiptId == receiptId).OrderBy(r => r.Id).ToList();

    public void AddClaim(SupplierClaim claim) => db.SupplierClaims.Add(claim);

    public SupplierClaim? FindClaim(string id) =>
        db.SupplierClaims.FirstOrDefault(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId && c.Id == id);

    public void SetField(object entity, string name, object value) =>
        db.Entry(entity).Property(name).CurrentValue = value;

    public int Save() => db.SaveChanges();

    private void SetLineTenant(object line)
    {
        db.Entry(line).Property("TenantId").CurrentValue = ec.TenantId;
        db.Entry(line).Property("LegalEntityId").CurrentValue = ec.EntityId;
    }
}
