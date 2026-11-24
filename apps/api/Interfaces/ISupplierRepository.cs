namespace Erp.Api.Interfaces;

public interface ISupplierRepository
{
    List<Supplier> All();
    bool Exists(string supplierId);
    Supplier? Find(string supplierId);
    void Add(Supplier supplier);
    void SetSupplierStatus(object supplier, SupplierLifecycleStatus status, string? statusReason);
    List<SupplierGate> GatesFor(string supplierId);
    SupplierGate? FindGate(string supplierId, string gateCode, DateOnly effectiveFrom);
    void AddGate(SupplierGate gate);
    void SetGateException(object gate, string exceptionBy, DateTimeOffset exceptionUntil);
    int Save();
}
