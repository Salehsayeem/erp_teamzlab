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
        services.AddScoped<ISourcingRepository, SourcingRepository>();
        services.AddScoped<IContractsRepository, ContractsRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ICrmRepository, CrmRepository>();
        services.AddScoped<IOpportunityRepository, OpportunityRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ICollectionsRepository, CollectionsRepository>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IPriceBookRepository, PriceBookRepository>();
        services.AddScoped<IPriceResolutionService, PriceResolutionService>();
        services.AddScoped<IQuoteService, QuoteService>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<IManufacturingRepository, ManufacturingRepository>();
        services.AddScoped<IAssetsRepository, AssetsRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IFiscalPeriodRepository, FiscalPeriodRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITaxCodeRepository, TaxCodeRepository>();
        services.AddScoped<IFinanceSettingRepository, FinanceSettingRepository>();
        services.AddScoped<IPeopleRepository, PeopleRepository>();
        services.AddScoped<IWorkerService, WorkerService>();
        services.AddScoped<ITimeService, TimeService>();
        services.AddScoped<IPayrollService, PayrollService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ISourcingService, SourcingService>();
        services.AddScoped<IContractsService, ContractsService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<IBomService, BomService>();
        services.AddScoped<IProductionOrderService, ProductionOrderService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ISalesOrderService, SalesOrderService>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddScoped<ICrmService, CrmService>();
        services.AddScoped<IOpportunityService, OpportunityService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IJournalService, JournalService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IServiceCaseService, ServiceCaseService>();
        services.AddScoped<IPlatformService, PlatformService>();
        services.AddScoped<IFiscalPeriodService, FiscalPeriodService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITaxCodeService, TaxCodeService>();
        services.AddScoped<IFinanceAgingService, FinanceAgingService>();
        return services;
    }
}
