using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public static class ServiceCollectionExtensions
{
    // Composition root pin: the in-memory store plus every service interface → implementation
    // pair as singletons (mock mode — persistence lands in the next task behind these seams).
    public static IServiceCollection AddErpServices(this IServiceCollection services)
    {
        services.AddSingleton(Db.I);
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISupplierService, SupplierService>();
        services.AddSingleton<IPurchaseOrderService, PurchaseOrderService>();
        services.AddSingleton<IReceiptService, ReceiptService>();
        services.AddSingleton<ICustomerService, CustomerService>();
        services.AddSingleton<ISalesOrderService, SalesOrderService>();
        services.AddSingleton<IJournalService, JournalService>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<IPlatformService, PlatformService>();
        return services;
    }
}
