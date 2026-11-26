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
        // deferred screens (PM1–PM10)
        services.AddScoped<IPeopleOrgService, PeopleOrgService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<IJobCatalogService, JobCatalogService>();
        services.AddScoped<IWorkScheduleService, WorkScheduleService>();
        services.AddScoped<ILeaveService, LeaveService>();
        services.AddScoped<IPayrollInputsService, PayrollInputsService>();
        services.AddScoped<IPaymentFileService, PaymentFileService>();
        services.AddScoped<IPayrollCloseService, PayrollCloseService>();
        services.AddScoped<ICredentialService, CredentialService>();
        services.AddScoped<IWorkforceService, WorkforceService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ISourcingService, SourcingService>();
        services.AddScoped<IContractsService, ContractsService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        // deferred PROCUREMENT wave
        services.AddScoped<IProcurementCatalogRepository, ProcurementCatalogRepository>();
        services.AddScoped<IRequisitionRepository, RequisitionRepository>();
        services.AddScoped<IPurchaseOrderChangeRepository, PurchaseOrderChangeRepository>();
        services.AddScoped<IBlanketOrderRepository, BlanketOrderRepository>();
        services.AddScoped<IReceiptInspectionRepository, ReceiptInspectionRepository>();
        services.AddScoped<IProcurementCatalogService, ProcurementCatalogService>();
        services.AddScoped<IRequisitionService, RequisitionService>();
        services.AddScoped<IApprovalRouteService, ApprovalRouteService>();
        services.AddScoped<IBudgetChecker, BudgetCheckService>();
        services.AddScoped<IPurchaseOrderChangeService, PurchaseOrderChangeService>();
        services.AddScoped<IBlanketOrderService, BlanketOrderService>();
        services.AddScoped<IReceiptInspectionService, ReceiptInspectionService>();
        services.AddScoped<ISupplierQualificationRepository, SupplierQualificationRepository>();
        services.AddScoped<ISupplierScorecardsRepository, SupplierScorecardsRepository>();
        services.AddScoped<IProcurementAnalyticsRepository, ProcurementAnalyticsRepository>();
        services.AddScoped<ISupplierQualificationService, SupplierQualificationService>();
        services.AddScoped<ISupplierScorecardsService, SupplierScorecardsService>();
        services.AddScoped<IProcurementAnalyticsService, ProcurementAnalyticsService>();
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
