using Erp.Api;

namespace Erp.Api.Interfaces;

public interface IBlanketOrderRepository
{
    BlanketOrder? Find(string id);
    List<BlanketOrder> All();
    void Add(BlanketOrder order);
    List<BlanketRelease> ReleasesFor(string blanketOrderId);
    void AddRelease(BlanketRelease release);
    // records carry init-only properties — mutations go through the EF change tracker (house pattern)
    void SetField(object entity, string name, object value);
    int Save();
}
