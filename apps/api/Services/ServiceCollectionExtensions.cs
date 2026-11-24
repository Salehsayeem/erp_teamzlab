using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public static class ServiceCollectionExtensions
{
    // Composition root (task-2): scoped EF DbContext → scoped repositories → scoped services.
    // Everything data-touching shares one scoped ErpDbContext per request, so multi-step writes
    // commit in ONE SaveChanges; controller classes stay stateless.
    public static IServiceCollection AddErpServices(this IServiceCollection services)
    {
        services.AddScoped<EntityContext>();
        services.AddScoped<INumberSeries, NumberSeries>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IFiscalPeriodRepository, FiscalPeriodRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITaxCodeRepository, TaxCodeRepository>();
        services.AddScoped<IFinanceSettingRepository, FinanceSettingRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ISalesOrderService, SalesOrderService>();
        services.AddScoped<IJournalService, JournalService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPlatformService, PlatformService>();
        services.AddScoped<IFiscalPeriodService, FiscalPeriodService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITaxCodeService, TaxCodeService>();
        services.AddScoped<IFinanceAgingService, FinanceAgingService>();
        return services;
    }
}
