using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IReceiptInspectionRepository
{
    Receipt? FindReceipt(string receiptId);
    List<Receipt> AllReceipts();
    List<Receipt> ReceiptsForPo(string poId);
    void AddDispatch(Dispatch dispatch);
    List<Dispatch> DispatchesFor(string poId);
    void AddAsn(AdvanceShipNotice asn);
    AdvanceShipNotice? FindAsn(string id);
    List<AdvanceShipNotice> Asns(AsnStatus? status);
    void AddInspection(ReceiptInspection inspection);
    ReceiptInspection? FindInspection(string id);
    List<ReceiptInspection> InspectionsForPo(string poId);
    void AddReturn(SupplierReturn ret);
    List<SupplierReturn> ReturnsForPo(string poId);
    void AddClaim(SupplierClaim claim);
    SupplierClaim? FindClaim(string id);
    void SetField(object entity, string name, object value);
    int Save();
}
