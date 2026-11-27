using System.Text.RegularExpressions;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data;

// Task-2 §2: PostgreSQL store — one DbContext for every aggregate. Domain records map as EF
// entities (lines are owned collections in their own tables: po_lines, so_lines, count_lines,
// journal_lines). Doc numbering runs on PG sequences per prefix so ids keep the SUP-0007 format.
// Every table carries tenant_id + legal_entity_id (real or shadow columns) and every FK and
// tenant-scoped query column is indexed (CLAUDE.md).
public class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierGate> SupplierGates => Set<SupplierGate>();
    public DbSet<SourcingEvent> SourcingEvents => Set<SourcingEvent>();
    public DbSet<EventLot> EventLots => Set<EventLot>();
    public DbSet<EvalCriterion> EvalCriteria => Set<EvalCriterion>();
    public DbSet<EventInvitation> EventInvitations => Set<EventInvitation>();
    public DbSet<EventQa> EventQas => Set<EventQa>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<BidLine> BidLines => Set<BidLine>();
    public DbSet<EvaluatorAssignment> EvaluatorAssignments => Set<EvaluatorAssignment>();
    public DbSet<ScoreCard> ScoreCards => Set<ScoreCard>();
    public DbSet<Award> Awards => Set<Award>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractAmendment> ContractAmendments => Set<ContractAmendment>();
    public DbSet<ContractPriceLine> ContractPriceLines => Set<ContractPriceLine>();
    public DbSet<ContractObligation> ContractObligations => Set<ContractObligation>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    // deferred PROCUREMENT wave (requisitions/taxonomy/change-control/blanket/receipt-workspace/qualification)
    public DbSet<Requisition> Requisitions => Set<Requisition>();
    public DbSet<ApprovalRule> ApprovalRules => Set<ApprovalRule>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<RequisitionDecision> RequisitionDecisions => Set<RequisitionDecision>();
    public DbSet<BudgetReservation> BudgetReservations => Set<BudgetReservation>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
    public DbSet<ProcurementCategory> ProcurementCategories => Set<ProcurementCategory>();
    public DbSet<UnitOfMeasure> UnitOfMeasures => Set<UnitOfMeasure>();
    public DbSet<UomConversion> UomConversions => Set<UomConversion>();
    public DbSet<PurchaseOrderChange> PurchaseOrderChanges => Set<PurchaseOrderChange>();
    public DbSet<PurchaseOrderAcknowledgement> PurchaseOrderAcknowledgements => Set<PurchaseOrderAcknowledgement>();
    public DbSet<BlanketOrder> BlanketOrders => Set<BlanketOrder>();
    public DbSet<BlanketRelease> BlanketReleases => Set<BlanketRelease>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();
    public DbSet<AdvanceShipNotice> AdvanceShipNotices => Set<AdvanceShipNotice>();
    public DbSet<ReceiptInspection> ReceiptInspections => Set<ReceiptInspection>();
    public DbSet<SupplierReturn> SupplierReturns => Set<SupplierReturn>();
    public DbSet<SupplierClaim> SupplierClaims => Set<SupplierClaim>();
    public DbSet<SupplierQuestionnaire> SupplierQuestionnaires => Set<SupplierQuestionnaire>();
    public DbSet<QuestionnaireResponse> QuestionnaireResponses => Set<QuestionnaireResponse>();
    public DbSet<SupplierEvidence> SupplierEvidences => Set<SupplierEvidence>();
    public DbSet<SupplierRiskProfile> SupplierRiskProfiles => Set<SupplierRiskProfile>();
    public DbSet<ScreeningResult> ScreeningResults => Set<ScreeningResult>();
    public DbSet<SupplierObservation> SupplierObservations => Set<SupplierObservation>();
    public DbSet<SupplierScorecard> SupplierScorecards => Set<SupplierScorecard>();
    public DbSet<CorrectiveAction> CorrectiveActions => Set<CorrectiveAction>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Interaction> Interactions => Set<Interaction>();
    public DbSet<CrmCase> CrmCases => Set<CrmCase>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AdjustmentDocument> SalesAdjustments => Set<AdjustmentDocument>();
    public DbSet<CollectionReminder> CollectionReminders => Set<CollectionReminder>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    // deferred-screens wave (S1..S9): quotes, pricing, order lifecycle, forecast, e-invoice/
    // delivery, returns/refunds, receipt candidates, rebates
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteRevision> QuoteRevisions => Set<QuoteRevision>();
    public DbSet<QuoteApproval> QuoteApprovals => Set<QuoteApproval>();
    public DbSet<QuoteAcceptance> QuoteAcceptances => Set<QuoteAcceptance>();
    public DbSet<PriceBook> PriceBooks => Set<PriceBook>();
    public DbSet<CustomerContractPrice> CustomerContractPrices => Set<CustomerContractPrice>();
    public DbSet<CustomerOnboardingCheck> CustomerOnboardingChecks => Set<CustomerOnboardingCheck>();
    public DbSet<PartyMerge> PartyMerges => Set<PartyMerge>();
    public DbSet<SalesOrderAmendment> SalesOrderAmendments => Set<SalesOrderAmendment>();
    public DbSet<OrderHold> OrderHolds => Set<OrderHold>();
    public DbSet<OrderCancellation> OrderCancellations => Set<OrderCancellation>();
    public DbSet<RecurringSchedule> RecurringSchedules => Set<RecurringSchedule>();
    public DbSet<RecurringOccurrence> RecurringOccurrences => Set<RecurringOccurrence>();
    public DbSet<ForecastSubmission> ForecastSubmissions => Set<ForecastSubmission>();
    public DbSet<ForecastSnapshot> ForecastSnapshots => Set<ForecastSnapshot>();
    public DbSet<EinvoiceSubmission> EinvoiceSubmissions => Set<EinvoiceSubmission>();
    public DbSet<InvoiceDelivery> InvoiceDeliveries => Set<InvoiceDelivery>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<ReturnReceipt> ReturnReceipts => Set<ReturnReceipt>();
    public DbSet<RefundRequest> RefundRequests => Set<RefundRequest>();
    public DbSet<ReceiptCandidate> ReceiptCandidates => Set<ReceiptCandidate>();
    public DbSet<RebateAgreement> RebateAgreements => Set<RebateAgreement>();
    public DbSet<RebateAccrual> RebateAccruals => Set<RebateAccrual>();
    public DbSet<RebateClaim> RebateClaims => Set<RebateClaim>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockLocation> Locations => Set<StockLocation>();
    public DbSet<StockLevel> Stock => Set<StockLevel>();
    public DbSet<StockTransfer> Transfers => Set<StockTransfer>();
    public DbSet<StockCount> Counts => Set<StockCount>();
    public DbSet<StockAdjustment> Adjustments => Set<StockAdjustment>();
    public DbSet<BomRevision> BomRevisions => Set<BomRevision>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<OrderAmendment> OrderAmendments => Set<OrderAmendment>();
    public DbSet<MaterialIssue> MaterialIssues => Set<MaterialIssue>();
    public DbSet<CompletionReport> CompletionReports => Set<CompletionReport>();
    public DbSet<ScrapReport> ScrapReports => Set<ScrapReport>();
    // MFG deferred screens (MF1-MF10) — one DbSet per new aggregate (ManufacturingDeferred.cs)
    public DbSet<RoutingRevision> RoutingRevisions => Set<RoutingRevision>();
    public DbSet<RoutingOperation> RoutingOperations => Set<RoutingOperation>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<ResourceCalendar> ResourceCalendars => Set<ResourceCalendar>();
    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();
    public DbSet<OperationPlacement> OperationPlacements => Set<OperationPlacement>();
    public DbSet<ScheduleScenario> ScheduleScenarios => Set<ScheduleScenario>();
    public DbSet<ScheduleOverride> ScheduleOverrides => Set<ScheduleOverride>();
    public DbSet<ConflictReport> ConflictReports => Set<ConflictReport>();
    public DbSet<SchedulePublication> SchedulePublications => Set<SchedulePublication>();
    public DbSet<MrpRun> MrpRuns => Set<MrpRun>();
    public DbSet<MrpLine> MrpLines => Set<MrpLine>();
    public DbSet<ActionMessage> ActionMessages => Set<ActionMessage>();
    public DbSet<MrpPublication> MrpPublications => Set<MrpPublication>();
    public DbSet<LaborTimeEntry> LaborTimeEntries => Set<LaborTimeEntry>();
    public DbSet<CounterEvent> CounterEvents => Set<CounterEvent>();
    public DbSet<DowntimeEvent> DowntimeEvents => Set<DowntimeEvent>();
    public DbSet<WipTransfer> WipTransfers => Set<WipTransfer>();
    public DbSet<CorrectionRecord> CorrectionRecords => Set<CorrectionRecord>();
    public DbSet<SubcontractCustody> SubcontractCustodies => Set<SubcontractCustody>();
    public DbSet<SubcontractReceipt> SubcontractReceipts => Set<SubcontractReceipt>();
    public DbSet<MaintenanceAvailabilityEvent> MaintenanceAvailabilityEvents => Set<MaintenanceAvailabilityEvent>();
    public DbSet<MaintenanceImpact> MaintenanceImpacts => Set<MaintenanceImpact>();
    public DbSet<QualityPlan> QualityPlans => Set<QualityPlan>();
    public DbSet<QualityCharacteristic> QualityCharacteristics => Set<QualityCharacteristic>();
    public DbSet<SamplingPlan> SamplingPlans => Set<SamplingPlan>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionResult> InspectionResults => Set<InspectionResult>();
    public DbSet<QualityHold> QualityHolds => Set<QualityHold>();
    public DbSet<Nonconformance> Nonconformances => Set<Nonconformance>();
    public DbSet<NcObject> NcObjects => Set<NcObject>();
    public DbSet<ContainmentAction> ContainmentActions => Set<ContainmentAction>();
    public DbSet<CapaCase> CapaCases => Set<CapaCase>();
    public DbSet<CapaAction> CapaActions => Set<CapaAction>();
    public DbSet<CapaVerification> CapaVerifications => Set<CapaVerification>();
    public DbSet<TransformationEvent> TransformationEvents => Set<TransformationEvent>();
    public DbSet<ReworkOrder> ReworkOrders => Set<ReworkOrder>();
    public DbSet<VarianceLedger> VarianceLedger => Set<VarianceLedger>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<FixedAsset> Assets => Set<FixedAsset>();
    public DbSet<AssetEvent> AssetEvents => Set<AssetEvent>();
    public DbSet<DepreciationRun> DepreciationRuns => Set<DepreciationRun>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ServiceCase> ServiceCases => Set<ServiceCase>();
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<CompensationEvent> CompensationEvents => Set<CompensationEvent>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<TimeCorrection> TimeCorrections => Set<TimeCorrection>();
    public DbSet<PayGroup> PayGroups => Set<PayGroup>();
    public DbSet<WorkerPayGroup> WorkerPayGroups => Set<WorkerPayGroup>();
    public DbSet<EarningDeductionCode> EarningDeductionCodes => Set<EarningDeductionCode>();
    public DbSet<PayRun> PayRuns => Set<PayRun>();
    // deferred screens wave (PM1–PM10)
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<WorkLocation> WorkLocations => Set<WorkLocation>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionAssignment> PositionAssignments => Set<PositionAssignment>();
    public DbSet<OnboardingTemplate> OnboardingTemplates => Set<OnboardingTemplate>();
    public DbSet<OnboardingTask> OnboardingTasks => Set<OnboardingTask>();
    public DbSet<JobProfile> JobProfiles => Set<JobProfile>();
    public DbSet<JobProfileVersion> JobProfileVersions => Set<JobProfileVersion>();
    public DbSet<Competency> Competencies => Set<Competency>();
    public DbSet<GradeRange> GradeRanges => Set<GradeRange>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<ScheduleVersion> ScheduleVersions => Set<ScheduleVersion>();
    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveLedgerEntry> LeaveLedgerEntries => Set<LeaveLedgerEntry>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveAdjustment> LeaveAdjustments => Set<LeaveAdjustment>();
    public DbSet<AttendanceException> AttendanceExceptions => Set<AttendanceException>();
    public DbSet<TaxElection> TaxElections => Set<TaxElection>();
    public DbSet<ContributionRule> ContributionRules => Set<ContributionRule>();
    public DbSet<GarnishmentOrder> GarnishmentOrders => Set<GarnishmentOrder>();
    public DbSet<BenefitPlan> BenefitPlans => Set<BenefitPlan>();
    public DbSet<BenefitEnrollment> BenefitEnrollments => Set<BenefitEnrollment>();
    public DbSet<DeductionInstruction> DeductionInstructions => Set<DeductionInstruction>();
    public DbSet<PaymentFile> PaymentFiles => Set<PaymentFile>();
    public DbSet<PayrollAccountMapping> PayrollAccountMappings => Set<PayrollAccountMapping>();
    public DbSet<PayrollPeriodClose> PayrollPeriodCloses => Set<PayrollPeriodClose>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<WorkforcePlan> WorkforcePlans => Set<WorkforcePlan>();
    public DbSet<AllocationSet> AllocationSets => Set<AllocationSet>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();
    public DbSet<FinanceSetting> FinanceSettings => Set<FinanceSetting>();
    // FINANCE-deep P5 foundations (006/007/008/003/016): currency/FX master, dimensions, voids
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<DimensionSet> DimensionSets => Set<DimensionSet>();
    public DbSet<DimensionValue> DimensionValues => Set<DimensionValue>();
    public DbSet<NumberSeriesVoid> NumberSeriesVoids => Set<NumberSeriesVoid>();
    // FINANCE-deep P1 (004/062/063): entity fiscal calendars + close-checklist tasks
    public DbSet<FiscalCalendar> FiscalCalendars => Set<FiscalCalendar>();
    public DbSet<CloseTask> CloseTasks => Set<CloseTask>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        ConfigureProcurement(mb);
        ConfigureSales(mb);
        ConfigureSalesDeferred(mb);
        ConfigureInventory(mb);
        ConfigureManufacturing(mb);
        ConfigureManufacturingDeferred(mb);
        ConfigurePeople(mb);
        ConfigurePeopleDeferred(mb);
        ConfigureFinance(mb);
        ConfigureAssets(mb);
        ConfigureIdentity(mb);
        ConfigurePlatform(mb);
        ConfigureSequences(mb);
        SnakeCaseAllColumns(mb);
        Seed(mb);
    }

    private static void ConfigureProcurement(ModelBuilder mb)
    {
        mb.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
        });

        mb.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_orders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.SupplierId);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                // PoLine.Id stays a plain nullable column (clients never send line ids — responses
                // keep "id": null); the PK is a generated shadow line number (task-2 §2)
                l.ToTable("po_lines");
                l.WithOwner().HasForeignKey("PoId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("PoId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("PoId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<Receipt>(e =>
        {
            e.ToTable("receipts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PurchaseOrderId, x.ItemSku });
            e.HasIndex(x => x.PurchaseOrderId);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        // -------- deferred PROCUREMENT wave (tenant+entity on every table from day one, R14) --------
        mb.Entity<Requisition>(e =>
        {
            e.ToTable("requisitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => x.CreatedBy);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("requisition_lines");
                l.WithOwner().HasForeignKey("RequisitionId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("RequisitionId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("RequisitionId");
                l.HasIndex("TenantId", "LegalEntityId");
                l.HasIndex("CategoryId");
            });
        });
        mb.Entity<ApprovalRule>(e =>
        {
            e.ToTable("approval_rules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.IsActive });
        });
        mb.Entity<ApprovalStep>(e =>
        {
            e.ToTable("approval_steps");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.RequisitionId });
            e.HasIndex(x => x.RequisitionId);
        });
        mb.Entity<RequisitionDecision>(e =>
        {
            e.ToTable("requisition_decisions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.RequisitionId });
        });
        mb.Entity<BudgetReservation>(e =>
        {
            e.ToTable("budget_reservations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.TxnId }).IsUnique();
        });
        mb.Entity<ApprovalDelegation>(e =>
        {
            e.ToTable("approval_delegations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.DelegateId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.DelegatorId });
        });
        mb.Entity<ProcurementCategory>(e =>
        {
            e.ToTable("procurement_categories");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ParentId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });
        mb.Entity<UnitOfMeasure>(e =>
        {
            e.ToTable("unit_of_measures");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });
        mb.Entity<UomConversion>(e =>
        {
            e.ToTable("uom_conversions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.FromCode });
        });
        mb.Entity<PurchaseOrderChange>(e =>
        {
            e.ToTable("purchase_order_changes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PoId });
            e.HasIndex(x => x.PoId);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<PurchaseOrderAcknowledgement>(e =>
        {
            e.ToTable("purchase_order_acknowledgements");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PoId });
            e.HasIndex(x => x.PoId);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<BlanketOrder>(e =>
        {
            e.ToTable("blanket_orders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SupplierId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });
        mb.Entity<BlanketRelease>(e =>
        {
            e.ToTable("blanket_releases");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.BlanketOrderId });
            e.HasIndex(x => x.BlanketOrderId);
        });
        mb.Entity<Dispatch>(e =>
        {
            e.ToTable("dispatches");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PoId });
            e.HasIndex(x => x.PoId);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<AdvanceShipNotice>(e =>
        {
            e.ToTable("advance_ship_notices");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PoId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => x.PoId);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("advance_ship_notice_lines");
                l.WithOwner().HasForeignKey("AsnId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("AsnId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("AsnId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });
        mb.Entity<ReceiptInspection>(e =>
        {
            e.ToTable("receipt_inspections");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ReceiptId });
            e.HasIndex(x => x.ReceiptId);
            e.HasIndex(x => x.PoId);
            e.HasOne<Receipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<SupplierReturn>(e =>
        {
            e.ToTable("supplier_returns");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ReceiptId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PoId });
            e.HasIndex(x => x.ReceiptId);
            e.HasOne<Receipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<SupplierClaim>(e =>
        {
            e.ToTable("supplier_claims");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PoId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });
        mb.Entity<SupplierQuestionnaire>(e =>
        {
            e.ToTable("supplier_questionnaires");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Name });
        });
        mb.Entity<QuestionnaireResponse>(e =>
        {
            e.ToTable("questionnaire_responses");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SupplierId });
            e.HasIndex(x => x.SupplierId);
            e.HasIndex(x => x.QuestionnaireId);
        });
        mb.Entity<SupplierEvidence>(e =>
        {
            e.ToTable("supplier_evidences");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SupplierId });
            e.HasIndex(x => x.SupplierId);
        });
        mb.Entity<SupplierRiskProfile>(e =>
        {
            e.ToTable("risk_profiles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SupplierId }).IsUnique();
        });
        mb.Entity<ScreeningResult>(e =>
        {
            e.ToTable("screening_results");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SupplierId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => x.SupplierId);
        });
        mb.Entity<SupplierObservation>(e =>
        {
            e.ToTable("supplier_observations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SupplierId });
            e.HasIndex(x => x.SupplierId);
        });
        mb.Entity<SupplierScorecard>(e =>
        {
            e.ToTable("supplier_scorecards");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SupplierId });
            e.HasIndex(x => x.SupplierId);
        });
        mb.Entity<CorrectiveAction>(e =>
        {
            e.ToTable("corrective_actions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ScorecardId });
            e.HasIndex(x => x.ScorecardId);
        });

        // task-6 sourcing/contract tables — tenant+entity scope on every table from day one (R14
        // note): scoped lookups filter both columns; ids come from the shared doc_ sequences (or
        // deterministic natural keys for child rows), never per-entity sequences or composite PKs
        mb.Entity<SourcingEvent>(e =>
        {
            e.ToTable("sourcing_events");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EventType });
        });

        mb.Entity<EventLot>(e =>
        {
            e.ToTable("event_lots");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<EvalCriterion>(e =>
        {
            e.ToTable("eval_criteria");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<EventInvitation>(e =>
        {
            e.ToTable("event_invitations");
            e.HasKey(x => x.Id); // natural key {eventId}:{supplierId}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => x.SupplierId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<EventQa>(e =>
        {
            e.ToTable("event_qas");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Bid>(e =>
        {
            e.ToTable("bids");
            e.HasKey(x => x.Id); // Bid.Id IS the receipt id (028 AC1)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => x.SupplierId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<BidLine>(e =>
        {
            e.ToTable("bid_lines");
            e.HasKey(x => x.Id); // natural key {bidId}:{lotId}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.BidId);
            e.HasIndex(x => x.LotId);
            e.HasOne<Bid>().WithMany().HasForeignKey(x => x.BidId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EventLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<EvaluatorAssignment>(e =>
        {
            e.ToTable("evaluator_assignments");
            e.HasKey(x => x.Id); // natural key {eventId}:{userId}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ScoreCard>(e =>
        {
            e.ToTable("score_cards");
            e.HasKey(x => x.Id); // natural key {bidId}:{criterionId}:{evaluatorUserId}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => x.BidId);
            e.HasIndex(x => x.CriterionId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Bid>().WithMany().HasForeignKey(x => x.BidId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EvalCriterion>().WithMany().HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Award>(e =>
        {
            e.ToTable("awards");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => x.LotId);
            e.HasIndex(x => x.SupplierId);
            e.HasOne<SourcingEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EventLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Contract>(e =>
        {
            e.ToTable("contracts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => x.SupplierId);
            // FIX 6e: one contract per award (unique with NULLS DISTINCT — manual contracts may repeat)
            e.HasIndex(x => x.AwardId).IsUnique();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Award>().WithMany().HasForeignKey(x => x.AwardId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ContractAmendment>(e =>
        {
            e.ToTable("contract_amendments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ContractId);
            // FIX 6a: executed amendment versions are immutable AND unique per contract — the
            // backstop behind the amendment row lock (FixContractAmendmentGuards migration)
            e.HasIndex(x => new { x.ContractId, x.Version }).IsUnique();
            e.HasOne<Contract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ContractPriceLine>(e =>
        {
            e.ToTable("contract_price_lines");
            e.HasKey(x => x.Id); // natural key {contractId}:{itemSku}:{effectiveFrom}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ContractId);
            e.HasOne<Contract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ContractObligation>(e =>
        {
            e.ToTable("contract_obligations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ContractId);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.DueDate }); // overdue queue scans
            e.HasOne<Contract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<SupplierGate>(e =>
        {
            e.ToTable("supplier_gates");
            e.HasKey(x => x.Id); // natural key {supplierId}:{gateCode}:{effectiveFrom}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.SupplierId);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSales(ModelBuilder mb)
    {
        mb.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
        });

        mb.Entity<SalesOrder>(e =>
        {
            e.ToTable("sales_orders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CustomerId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                // see po_lines — SoLine.Id is a nullable column, the PK is a generated shadow line number
                l.ToTable("so_lines");
                l.WithOwner().HasForeignKey("SoId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("SoId", "LineNo");
                // shared-owned SoLine (so_amendments/recurring_schedules join below) resolves Id
                // as required — pin it back to nullable or existing rows (NULL ids) break the ALTER
                l.Property<string>("Id").IsRequired(false);
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("SoId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<Shipment>(e =>
        {
            e.ToTable("shipments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.SalesOrderId);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        // --- task-7 SALES deep: CRM, opportunities, invoice documents, collections ---
        // every new table carries tenant_id + legal_entity_id from day one (R14) and every FK
        // is indexed (CLAUDE.md)
        mb.Entity<Lead>(e =>
        {
            e.ToTable("leads");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status }); // list w/ status filter
        });

        mb.Entity<Interaction>(e =>
        {
            e.ToTable("interactions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PartyType, x.PartyId }); // party timeline
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OccurredAt }); // OccurredAt ordering (007)
        });

        mb.Entity<CrmCase>(e =>
        {
            e.ToTable("crm_cases"); // R-T7d: separate entity from the ASSETS service_cases table
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ResponseDueAt }); // overdue scans (008)
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.SalesOrderId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Opportunity>(e =>
        {
            e.ToTable("opportunities");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status }); // status/owner filter
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.LeadId);
            e.HasIndex(x => x.OrderId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("opportunity_lines");
                l.WithOwner().HasForeignKey("OpportunityId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("OpportunityId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("OpportunityId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<Invoice>(e =>
        {
            e.ToTable("invoices");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.CustomerId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            // 042 AC2: a shipment source event can bill at most once — unique index backs the
            // service-level ALREADY_BILLED check up under concurrency. Partial (fix round 1,
            // review 1b): a DISCARDED draft releases its source event for re-billing, so the
            // uniqueness no longer covers discarded rows.
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SourceEventId }).IsUnique()
                .HasFilter($"status <> {(int)InvoiceStatus.Discarded}");
            // review 1a: status is a concurrency token — every invoice UPDATE carries
            // WHERE status = <original>, so a stale second post updates ZERO rows and
            // DbUpdateConcurrencyException surfaces as 409 INVOICE_ALREADY_POSTED
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.SalesOrderId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("invoice_lines");
                l.WithOwner().HasForeignKey("InvoiceId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("InvoiceId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("InvoiceId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<AdjustmentDocument>(e =>
        {
            e.ToTable("sales_adjustments"); // "adjustments" is inventory's stock-adjustment table
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.InvoiceId);
            // review 1a (same family): a stale second approval updates zero rows instead of
            // writing a second reversing journal
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("sales_adjustment_lines");
                l.WithOwner().HasForeignKey("AdjustmentId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("AdjustmentId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("AdjustmentId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<CollectionReminder>(e =>
        {
            e.ToTable("collection_reminders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.InvoiceId });
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Dispute>(e =>
        {
            e.ToTable("disputes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.InvoiceId });
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // --- deferred-screens wave (S1..S9): quotes, pricing, order lifecycle, forecast,
    // e-invoice/delivery, returns/refunds, receipt candidates, rebates. Same day-one rules:
    // tenant+entity on every table, every FK indexed, money documents carry status
    // concurrency tokens. Self-referencing customer columns (hierarchy/merge) are indexed
    // without FK constraints — EF cannot bind two navigation-less self refs cleanly and the
    // rows are never hard-deleted (Restrict everywhere).
    private static void ConfigureSalesDeferred(ModelBuilder mb)
    {
        mb.Entity<Customer>(e =>
        {
            // 012/014: hierarchy parent + merge redirect on the existing customers table
            e.HasIndex(x => x.ParentCustomerId);
            e.HasIndex(x => x.MergedIntoId);
        });

        mb.Entity<Quote>(e =>
        {
            e.ToTable("quotes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.CustomerId });
            e.HasIndex(x => x.CustomerId);
            e.Property(x => x.Status).IsConcurrencyToken(); // double-accept race (030 AC2)
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<QuoteRevision>(e =>
        {
            e.ToTable("quote_revisions");
            e.HasKey(x => x.Id); // natural key "{quoteId}:R{n}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.QuoteId);
            e.Property(x => x.Status).IsConcurrencyToken(); // approval vs. acceptance race
            e.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("quote_lines");
                l.WithOwner().HasForeignKey("RevisionId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("RevisionId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("RevisionId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<QuoteApproval>(e =>
        {
            e.ToTable("quote_approvals");
            e.HasKey(x => x.Id); // natural key "{revisionId}:{approverId}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.RevisionId);
            e.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<QuoteAcceptance>(e =>
        {
            e.ToTable("quote_acceptances");
            e.HasKey(x => x.Id); // natural key "{revisionId}:{externalEventId ?? token}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.RevisionId);
            // 030 AC2: duplicate external acceptance event → idempotent replay (nulls distinct)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ExternalEventId }).IsUnique();
            e.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<PriceBook>(e =>
        {
            e.ToTable("price_books");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.OwnsMany(x => x.Entries, l =>
            {
                l.ToTable("price_book_entries");
                l.WithOwner().HasForeignKey("BookId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("BookId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("BookId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<CustomerContractPrice>(e =>
        {
            e.ToTable("customer_contract_prices");
            e.HasKey(x => x.Id); // natural key customer+sku+from+minQty
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.CustomerId, x.ItemSku });
            e.HasIndex(x => x.CustomerId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<CustomerOnboardingCheck>(e =>
        {
            e.ToTable("customer_onboarding_checks");
            e.HasKey(x => x.Id); // natural key "{customerId}:{checkCode}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CustomerId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<PartyMerge>(e =>
        {
            e.ToTable("party_merges");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.SurvivorId);
            e.HasIndex(x => x.MergedId);
        });

        mb.Entity<SalesOrderAmendment>(e =>
        {
            e.ToTable("so_amendments"); // order_amendments is MFG's table (name-collision lesson)
            e.HasKey(x => x.Id); // natural key "{soId}:A{n}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.SalesOrderId);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("so_amendment_lines");
                l.WithOwner().HasForeignKey("AmendmentId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("AmendmentId", "LineNo");
                l.Property<string>("Id").IsRequired(false);
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("AmendmentId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<OrderHold>(e =>
        {
            e.ToTable("order_holds");
            e.HasKey(x => x.Id); // natural key "{soId}:H{n}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SalesOrderId, x.Status });
            e.HasIndex(x => x.SalesOrderId);
            e.Property(x => x.Status).IsConcurrencyToken(); // double-release race
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<OrderCancellation>(e =>
        {
            e.ToTable("order_cancellations");
            e.HasKey(x => x.Id); // natural key "{soId}:C{n}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.SalesOrderId);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<RecurringSchedule>(e =>
        {
            e.ToTable("recurring_schedules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CustomerId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("recurring_schedule_lines");
                l.WithOwner().HasForeignKey("ScheduleId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("ScheduleId", "LineNo");
                l.Property<string>("Id").IsRequired(false);
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("ScheduleId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<RecurringOccurrence>(e =>
        {
            e.ToTable("recurring_occurrences");
            e.HasKey(x => x.Id); // natural key "{scheduleId}:{periodKey}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            // 040 AC1: ≤1 occurrence per (schedule, period) — the unique index backs the
            // idempotent generation under concurrency
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ScheduleId, x.PeriodKey }).IsUnique();
            e.HasIndex(x => x.ScheduleId);
            e.HasIndex(x => x.OrderId);
            e.HasOne<RecurringSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ForecastSubmission>(e =>
        {
            e.ToTable("forecast_submissions");
            e.HasKey(x => x.Id); // natural key "{ownerId}:{period}:{category}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Period });
        });

        mb.Entity<ForecastSnapshot>(e =>
        {
            e.ToTable("forecast_snapshots");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Period });
        });

        mb.Entity<EinvoiceSubmission>(e =>
        {
            e.ToTable("einvoice_submissions");
            e.HasKey(x => x.Id); // natural key "{invoiceId}:EIV:{n}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<InvoiceDelivery>(e =>
        {
            e.ToTable("invoice_deliveries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.InvoiceId, x.Attempt });
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<SalesReturn>(e =>
        {
            e.ToTable("sales_returns");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.SalesOrderId);
            e.HasIndex(x => x.CustomerId);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("sales_return_lines");
                l.WithOwner().HasForeignKey("ReturnId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("ReturnId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("ReturnId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<ReturnReceipt>(e =>
        {
            e.ToTable("return_receipts");
            e.HasKey(x => x.Id); // natural key "{returnId}:R{n}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ReturnId);
            e.HasOne<SalesReturn>().WithMany().HasForeignKey(x => x.ReturnId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<RefundRequest>(e =>
        {
            e.ToTable("refund_requests");
            e.HasKey(x => x.Id); // natural key "{adjustmentId}:REF" — one refund per credit
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.AdjustmentId);
            e.HasIndex(x => x.InvoiceId);
            e.Property(x => x.Status).IsConcurrencyToken(); // double-approval race (062)
            e.HasOne<AdjustmentDocument>().WithMany().HasForeignKey(x => x.AdjustmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ReceiptCandidate>(e =>
        {
            e.ToTable("receipt_candidates");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<RebateAgreement>(e =>
        {
            e.ToTable("rebate_agreements");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CustomerId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<RebateAccrual>(e =>
        {
            e.ToTable("rebate_accruals");
            e.HasKey(x => x.Id); // natural key "{agreementId}:{period}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.AgreementId);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasOne<RebateAgreement>().WithMany().HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<RebateClaim>(e =>
        {
            e.ToTable("rebate_claims");
            e.HasKey(x => x.Id); // natural key "{agreementId}:{period}:C{n}"
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.AgreementId);
            e.Property(x => x.Status).IsConcurrencyToken(); // double-settlement race (063)
            e.HasOne<RebateAgreement>().WithMany().HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureInventory(ModelBuilder mb)
    {
        mb.Entity<Warehouse>(e =>
        {
            e.ToTable("warehouses");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<StockLocation>(e =>
        {
            e.ToTable("locations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.WarehouseId);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.WarehouseId, x.Code }).IsUnique();
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<StockLevel>(e =>
        {
            e.ToTable("stock_levels");
            e.HasKey(x => new { x.TenantId, x.LegalEntityId, x.WarehouseId, x.LocationId, x.ItemSku });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ItemSku });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.LocationId });
        });

        mb.Entity<StockTransfer>(e =>
        {
            e.ToTable("transfers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.FromLocationId);
            e.HasIndex(x => x.ToLocationId);
            e.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<StockCount>(e =>
        {
            e.ToTable("counts");
            e.HasKey(x => x.Id);
            // journal-race hardening: the Open→Closed flip is a token-guarded UPDATE — a stale
            // second closer matches zero rows and writes no second variance journal/stock move
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.LocationId);
            e.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Items, l =>
            {
                l.ToTable("count_lines");
                l.WithOwner().HasForeignKey("CountId");
                l.Property<int>("Id").ValueGeneratedOnAdd();
                l.HasKey("CountId", "Id");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("CountId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<StockAdjustment>(e =>
        {
            e.ToTable("adjustments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CountId);
            e.HasIndex(x => x.JournalId);
            e.HasOne<StockCount>().WithMany().HasForeignKey(x => x.CountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Journal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // --- MFG deep (ERP-MFG-007/008/010/013/014/015/022/041/042/043/067/069) — tenant+entity
    // scope on every table from day one (R14), every FK indexed (CLAUDE.md). production_orders
    // carries a unique order_no per tenant/entity and status as a concurrency token (T7 review 1a:
    // a stale second transition updates ZERO rows instead of double-posting). MFG tables reference
    // bom_revisions twice (planned + released) — both nullable FKs, Restrict deletes.
    private static void ConfigureManufacturing(ModelBuilder mb)
    {
        mb.Entity<BomRevision>(e =>
        {
            e.ToTable("bom_revisions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OutputSku }); // effective-resolution scans
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OutputSku, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status }); // list w/ status filter
        });

        mb.Entity<BomLine>(e =>
        {
            e.ToTable("bom_lines");
            e.HasKey(x => x.Id); // natural key {bomRevisionId}:{componentSku}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.BomRevisionId);
            e.HasOne<BomRevision>().WithMany().HasForeignKey(x => x.BomRevisionId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ProductionOrder>(e =>
        {
            e.ToTable("production_orders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OutputSku });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderNo }).IsUnique();
            // status is a concurrency token — every guarded transition carries WHERE status = <original>
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasOne<BomRevision>().WithMany().HasForeignKey(x => x.PlannedBomRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BomRevision>().WithMany().HasForeignKey(x => x.ReleasedBomRevisionId).OnDelete(DeleteBehavior.Restrict);
            // MF1 (024 AC3): released orders freeze the routing revision + sequence — resolved at
            // create (planning basis) and frozen at release, same discipline as the BOM columns
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PlannedRoutingRevisionId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ReleasedRoutingRevisionId });
            e.HasOne<RoutingRevision>().WithMany().HasForeignKey(x => x.PlannedRoutingRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RoutingRevision>().WithMany().HasForeignKey(x => x.ReleasedRoutingRevisionId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<OrderAmendment>(e =>
        {
            e.ToTable("order_amendments");
            e.HasKey(x => x.Id); // natural key {orderId}:v{n}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.OrderId);
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<MaterialIssue>(e =>
        {
            e.ToTable("material_issues");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ComponentSku }); // per-sku consumption scans
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.LinkedIssueId }); // returnable-qty lookups
            e.HasIndex(x => x.JournalId);
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Journal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<CompletionReport>(e =>
        {
            e.ToTable("completion_reports");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => x.JournalId);
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Journal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ScrapReport>(e =>
        {
            e.ToTable("scrap_reports");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => x.JournalId);
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Journal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // --- MFG deferred screens (MF1-MF10, ERP-MFG-024..070) — tenant+entity scope on every table
    // from day one (R14), every FK indexed, ids from the shared doc_ sequences (partition
    // prefixes rot/wkc/srn/scn/cfl/mrp/lbt/cme/dwn/wtr/mcr/sub/mav/qpl/qin/qhd/ncr/cpa/txp/rwk/vln).
    // Concurrency tokens on every guarded state flip (holds, NC closure, CAPA closure, labor
    // approval, inspection finalization, rework) — a stale second actor updates ZERO rows.
    private static void ConfigureManufacturingDeferred(ModelBuilder mb)
    {
        // ---- MF1: routings / work centers / calendars ----
        mb.Entity<RoutingRevision>(e =>
        {
            e.ToTable("routing_revisions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OutputSku, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<RoutingOperation>(e =>
        {
            e.ToTable("routing_operations");
            e.HasKey(x => x.Id); // natural key {routingRevisionId}:{seq}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.RoutingRevisionId);
            e.HasIndex(x => new { x.RoutingRevisionId, x.Seq }).IsUnique();
            e.HasIndex(x => x.WorkCenterId);
            e.HasOne<RoutingRevision>().WithMany().HasForeignKey(x => x.RoutingRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<WorkCenter>(e =>
        {
            e.ToTable("work_centers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<ResourceCalendar>(e =>
        {
            e.ToTable("resource_calendars");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.WorkCenterId);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.WorkCenterId, x.WindowStart });
            // maintenance materialization idempotency: one window per source event
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SourceEventId }).IsUnique()
                .HasFilter("source_event_id IS NOT NULL");
            e.HasOne<WorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- MF2: scheduling ----
        mb.Entity<ScheduleRun>(e =>
        {
            e.ToTable("schedule_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<OperationPlacement>(e =>
        {
            e.ToTable("operation_placements");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ScheduleRunId);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasOne<ScheduleRun>().WithMany().HasForeignKey(x => x.ScheduleRunId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ScheduleScenario>(e =>
        {
            e.ToTable("schedule_scenarios");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.BaseRunId);
            e.HasOne<ScheduleRun>().WithMany().HasForeignKey(x => x.BaseRunId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ScheduleOverride>(e =>
        {
            e.ToTable("schedule_overrides");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ScenarioId);
            e.HasOne<ScheduleScenario>().WithMany().HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ConflictReport>(e =>
        {
            e.ToTable("conflict_reports");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ScheduleRunId);
            e.HasOne<ScheduleRun>().WithMany().HasForeignKey(x => x.ScheduleRunId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<SchedulePublication>(e =>
        {
            e.ToTable("schedule_publications");
            e.HasKey(x => x.Id); // natural key = the run id — published events idempotent per run (030)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ScheduleRunId }).IsUnique();
            e.HasOne<ScheduleRun>().WithMany().HasForeignKey(x => x.ScheduleRunId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- MF4: MRP ----
        mb.Entity<MrpRun>(e =>
        {
            e.ToTable("mrp_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<MrpLine>(e =>
        {
            e.ToTable("mrp_lines");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.RunId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.RunId, x.LineNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ItemSku }); // netting scans
            // conversion idempotency backstop: a planned order converts exactly once
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ConvertedOrderId }).IsUnique()
                .HasFilter("converted_order_id IS NOT NULL");
            e.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ActionMessage>(e =>
        {
            e.ToTable("action_messages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.State });
            e.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<MrpPublication>(e =>
        {
            e.ToTable("mrp_publications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.RunId }).IsUnique(); // idempotent on run id (040)
            e.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- MF5: shop-floor reporting extensions ----
        mb.Entity<LaborTimeEntry>(e =>
        {
            e.ToTable("labor_time_entries");
            e.HasKey(x => x.Id);
            // 044 AC3: approved entries are immutable — a stale second approval writes nothing
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.WorkerEmploymentId, x.Status }); // approval queue
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<CounterEvent>(e =>
        {
            e.ToTable("counter_events");
            e.HasKey(x => x.Id);
            // 045 AC1: duplicate source event ids are ignored idempotently — unique backstop
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SourceEventId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<DowntimeEvent>(e =>
        {
            e.ToTable("downtime_events");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.WorkCenterId });
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<WipTransfer>(e =>
        {
            e.ToTable("wip_transfers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<CorrectionRecord>(e =>
        {
            e.ToTable("correction_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.TargetType, x.TargetId });
        });

        // ---- MF6: subcontract ----
        mb.Entity<SubcontractCustody>(e =>
        {
            e.ToTable("subcontract_custodies");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.InventoryMovementId }); // accepted-movement lookups
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<SubcontractReceipt>(e =>
        {
            e.ToTable("subcontract_receipts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- MF7: maintenance ----
        mb.Entity<MaintenanceAvailabilityEvent>(e =>
        {
            e.ToTable("maintenance_availability_events");
            e.HasKey(x => x.Id);
            // 053 AC1: idempotent ingestion keyed on the source event id
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SourceEventId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ResourceCode });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<MaintenanceImpact>(e =>
        {
            e.ToTable("maintenance_impacts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OutageEventId });
            e.HasOne<MaintenanceAvailabilityEvent>().WithMany().HasForeignKey(x => x.OutageEventId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- MF8: quality plans / inspections ----
        mb.Entity<QualityPlan>(e =>
        {
            e.ToTable("quality_plans");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ItemSku, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<QualityCharacteristic>(e =>
        {
            e.ToTable("quality_characteristics");
            e.HasKey(x => x.Id); // natural key {planId}:{code}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.PlanId);
            e.HasIndex(x => new { x.PlanId, x.Code }).IsUnique();
            e.HasOne<QualityPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<SamplingPlan>(e =>
        {
            e.ToTable("sampling_plans");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.QualityPlanId);
            e.HasOne<QualityPlan>().WithMany().HasForeignKey(x => x.QualityPlanId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Inspection>(e =>
        {
            e.ToTable("inspections");
            e.HasKey(x => x.Id);
            // 058 AC2: finalized inspections are immutable — the flip is token-guarded
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.QualityPlanId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<InspectionResult>(e =>
        {
            e.ToTable("inspection_results");
            e.HasKey(x => x.Id); // natural key {inspectionId}:{characteristicCode}:a{amendmentNo}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.InspectionId);
            e.HasIndex(x => new { x.InspectionId, x.CharacteristicCode, x.AmendmentNo }).IsUnique();
            e.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- MF9: holds / NCR / CAPA ----
        mb.Entity<QualityHold>(e =>
        {
            e.ToTable("quality_holds");
            e.HasKey(x => x.Id);
            // 059 AC2: the disposition flip is token-guarded; availability scans filter open holds
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.ItemSku, x.Status }); // availability subtraction
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
        });

        mb.Entity<Nonconformance>(e =>
        {
            e.ToTable("nonconformances");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<NcObject>(e =>
        {
            e.ToTable("nc_objects");
            e.HasKey(x => x.Id); // natural key {nonconformanceId}:{objectRef}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.NonconformanceId);
            e.HasOne<Nonconformance>().WithMany().HasForeignKey(x => x.NonconformanceId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ContainmentAction>(e =>
        {
            e.ToTable("containment_actions");
            e.HasKey(x => x.Id);
            // 061 AC2: a stale second containment release writes nothing
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.NonconformanceId);
            e.HasOne<Nonconformance>().WithMany().HasForeignKey(x => x.NonconformanceId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<CapaCase>(e =>
        {
            e.ToTable("capa_cases");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<CapaAction>(e =>
        {
            e.ToTable("capa_actions");
            e.HasKey(x => x.Id); // natural key {capaCaseId}:a{n}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CapaCaseId);
            e.HasOne<CapaCase>().WithMany().HasForeignKey(x => x.CapaCaseId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<CapaVerification>(e =>
        {
            e.ToTable("capa_verifications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.CapaCaseId);
            e.HasOne<CapaCase>().WithMany().HasForeignKey(x => x.CapaCaseId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- MF10: genealogy / rework / variance ledger ----
        mb.Entity<TransformationEvent>(e =>
        {
            e.ToTable("transformation_events");
            e.HasKey(x => x.Id);
            // 064: immutable — one event per completion, emitted forward-only (Q-MF4)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.CompletionId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OutputSku, x.LotSerial }); // genealogy scans
            e.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ReworkOrder>(e =>
        {
            e.ToTable("rework_orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SourceOrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
        });

        mb.Entity<VarianceLedger>(e =>
        {
            e.ToTable("variance_ledger");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrderId, x.Disposition }); // close-gate scans
        });
    }


    // PEOPLE deep (workers/time/payroll, rulings R-PEO1..11) — tenant+entity scope on every
    // table from day one (R14 gate), every FK indexed, ids from the shared doc_ sequences.
    private static void ConfigurePeople(ModelBuilder mb)
    {
        mb.Entity<Employment>(e =>
        {
            e.ToTable("employments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status }); // register status filter
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.WorkerNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.NationalId }); // rehire duplicate scan (004)
            // review-1a mechanism: a stale second lifecycle transition updates zero rows
            e.Property(x => x.Status).IsConcurrencyToken();
        });

        mb.Entity<CompensationEvent>(e =>
        {
            e.ToTable("compensation_events");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.EffectiveFrom }); // as-of resolution (024)
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<TimeEntry>(e =>
        {
            e.ToTable("time_entries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.WorkDate, x.Status }); // run input pull (034)
            // 026 AC2: replay-safety backstop — one payable row per idempotency key
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.IdemKey }).IsUnique();
            e.HasIndex(x => x.EmploymentId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<TimeCorrection>(e =>
        {
            e.ToTable("time_corrections");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.TimeEntryId);
            // review-1a mechanism: a stale second decision updates zero rows, not double-apply
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasOne<TimeEntry>().WithMany().HasForeignKey(x => x.TimeEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<PayGroup>(e =>
        {
            e.ToTable("pay_groups");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<WorkerPayGroup>(e =>
        {
            e.ToTable("worker_pay_groups");
            e.HasKey(x => x.Id); // natural key {employmentId}:{payGroupId}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.EmploymentId);
            e.HasIndex(x => x.PayGroupId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PayGroup>().WithMany().HasForeignKey(x => x.PayGroupId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<EarningDeductionCode>(e =>
        {
            e.ToTable("earning_deduction_codes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Kind });
        });

        mb.Entity<PayRun>(e =>
        {
            e.ToTable("pay_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            // 049: the run key IS this unique index — one live run per group+period; REVERSED rows
            // are excluded (partial index, invoice source-event pattern) because a reversal run is
            // a NEW id sharing the original's group+period via reversal_of_run_id
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PayGroupId, x.PeriodId }).IsUnique()
                .HasFilter($"status <> {(int)PayRunStatus.Reversed}");
            // review-1a mechanism: a stale second approval/post/reversal updates ZERO rows instead
            // of double-posting journals
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => x.PeriodId);
            e.HasIndex(x => x.ReversalOfRunId);
            e.HasOne<PayGroup>().WithMany().HasForeignKey(x => x.PayGroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PayRun>().WithMany().HasForeignKey(x => x.ReversalOfRunId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                // pay_run_lines is an owned table — shadow line-number PK, no client ids (see po_lines)
                l.ToTable("pay_run_lines");
                l.WithOwner().HasForeignKey("PayRunId");
                l.Property<int>("LineNo").ValueGeneratedOnAdd();
                l.HasKey("PayRunId", "LineNo");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("PayRunId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });
    }

    // deferred screens (PM1–PM10) — tenant+entity scope on every table from day one, every FK and
    // frequent query column indexed. Unique indexes are the concurrency/idempotency backstops the
    // services rely on (onboarding generation, accrual replay, attendance dedupe, one file per run).
    private static void ConfigurePeopleDeferred(ModelBuilder mb)
    {
        mb.Entity<OrgUnit>(e =>
        {
            e.ToTable("org_units");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }); // as-of hierarchy scans
            e.HasIndex(x => x.ParentOrgUnitId);
        });

        mb.Entity<WorkLocation>(e =>
        {
            e.ToTable("work_locations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<Position>(e =>
        {
            e.ToTable("positions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.OrgUnitId }); // hierarchy listings
            e.HasIndex(x => x.OrgUnitId);
        });

        mb.Entity<PositionAssignment>(e =>
        {
            e.ToTable("position_assignments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PositionId, x.Status }); // headcount scans
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId }); // worker's assignments
            e.HasIndex(x => x.PositionId);
            e.HasIndex(x => x.EmploymentId);
            e.HasOne<Position>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            // a stale second exception decision updates zero rows (review-1a mechanism)
            e.Property(x => x.Status).IsConcurrencyToken();
        });

        mb.Entity<OnboardingTemplate>(e =>
        {
            e.ToTable("onboarding_templates");
            e.HasKey(x => x.Id); // natural key {categoryCode}:{taskKey}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.CategoryCode, x.TaskKey }).IsUnique();
        });

        mb.Entity<OnboardingTask>(e =>
        {
            e.ToTable("onboarding_tasks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.TaskKey }).IsUnique(); // generation idempotency
            e.HasIndex(x => x.EmploymentId);
            e.HasIndex(x => x.TemplateId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OnboardingTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).IsConcurrencyToken();
        });

        mb.Entity<JobProfile>(e =>
        {
            e.ToTable("job_profiles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<JobProfileVersion>(e =>
        {
            e.ToTable("job_profile_versions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.ProfileId, x.Version }).IsUnique(); // 022: historic refs keep their version
            e.HasIndex(x => x.ProfileId);
            e.HasOne<JobProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Competency>(e =>
        {
            e.ToTable("competencies");
            e.HasKey(x => x.Id); // natural key {profileVersionId}:{name}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ProfileVersionId);
            e.HasOne<JobProfileVersion>().WithMany().HasForeignKey(x => x.ProfileVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<GradeRange>(e =>
        {
            e.ToTable("grade_ranges");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.GradeCode, x.EffectiveFrom }); // as-of policy lookups (023)
        });

        mb.Entity<WorkSchedule>(e =>
        {
            e.ToTable("work_schedules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<ScheduleVersion>(e =>
        {
            e.ToTable("schedule_versions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.ScheduleId, x.EffectiveFrom }); // as-of version resolution (025)
            e.HasIndex(x => x.ScheduleId);
            e.HasOne<WorkSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<HolidayCalendar>(e =>
        {
            e.ToTable("holiday_calendars");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
            e.OwnsMany(x => x.Dates, d =>
            {
                d.ToTable("holiday_dates");
                d.WithOwner().HasForeignKey("CalendarId");
                d.HasKey("CalendarId", "Date");
                d.Property<string>("TenantId");
                d.Property<string>("LegalEntityId");
                d.HasIndex("CalendarId");
                d.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<LeaveType>(e =>
        {
            e.ToTable("leave_types");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<LeaveLedgerEntry>(e =>
        {
            e.ToTable("leave_ledger_entries");
            e.HasKey(x => x.Id);
            // 030 AC1: (tenant, entity, employment, type, period, sourceKey) unique IS the accrual
            // replay idempotency backstop — a re-run of the same period+source cannot double-post
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.LeaveTypeId, x.Period, x.SourceKey }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.LeaveTypeId, x.EntryDate }); // as-of balances
            e.HasIndex(x => x.EmploymentId);
            e.HasIndex(x => x.LeaveTypeId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<LeaveRequest>(e =>
        {
            e.ToTable("leave_requests");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.Status }); // queue scans
            e.HasIndex(x => x.EmploymentId);
            e.HasIndex(x => x.LeaveTypeId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
            // a stale second decision/withdrawal updates zero rows
            e.Property(x => x.Status).IsConcurrencyToken();
        });

        mb.Entity<LeaveAdjustment>(e =>
        {
            e.ToTable("leave_adjustments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId });
            e.HasIndex(x => x.EmploymentId);
            e.HasIndex(x => x.LeaveTypeId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).IsConcurrencyToken();
        });

        mb.Entity<AttendanceException>(e =>
        {
            e.ToTable("attendance_exceptions");
            e.HasKey(x => x.Id); // natural key {employmentId}:{workDate}:{kind} — IS the dedupe (033)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.WorkDate });
            e.HasIndex(x => x.EmploymentId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<TaxElection>(e =>
        {
            e.ToTable("tax_elections");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.EffectiveFrom }); // as-of selection (041)
            e.HasIndex(x => x.EmploymentId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ContributionRule>(e =>
        {
            e.ToTable("contribution_rules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }); // versions share Code
            e.HasIndex(x => x.PayCodeId);
        });

        mb.Entity<GarnishmentOrder>(e =>
        {
            e.ToTable("garnishment_orders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.Status }); // conflict + release scans
            e.HasIndex(x => x.EmploymentId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).IsConcurrencyToken();
        });

        mb.Entity<BenefitPlan>(e =>
        {
            e.ToTable("benefit_plans");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
            e.HasIndex(x => x.PayCodeId);
        });

        mb.Entity<BenefitEnrollment>(e =>
        {
            e.ToTable("benefit_enrollments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.Status });
            e.HasIndex(x => x.EmploymentId);
            e.HasIndex(x => x.PlanId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BenefitPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<DeductionInstruction>(e =>
        {
            e.ToTable("deduction_instructions");
            e.HasKey(x => x.Id); // natural key {enrollmentId}:{period}:v{version}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EnrollmentId, x.Period }); // reconciliation scans (044)
            e.HasIndex(x => x.EnrollmentId);
            e.HasOne<BenefitEnrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<PaymentFile>(e =>
        {
            e.ToTable("payment_files");
            e.HasKey(x => x.Id);
            // one live payment file per run — the idempotent-create backstop (050)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.RunId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => x.RunId);
            e.HasOne<PayRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.OwnsMany(x => x.Items, i =>
            {
                i.ToTable("payment_file_items");
                i.WithOwner().HasForeignKey("PaymentFileId");
                i.HasKey("PaymentFileId", "EmploymentId"); // one instruction per worker per file
                i.Property<string>("TenantId");
                i.Property<string>("LegalEntityId");
                i.HasIndex("PaymentFileId");
                i.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<PayrollAccountMapping>(e =>
        {
            e.ToTable("payroll_account_mappings");
            e.HasKey(x => x.Id); // natural key map:{element}:{effectiveFrom:yyyyMMdd}
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Element, x.EffectiveFrom }); // as-of posting resolution (053 AC2)
        });

        mb.Entity<PayrollPeriodClose>(e =>
        {
            e.ToTable("payroll_period_closes");
            e.HasKey(x => x.Id); // Id IS the periodId — resumable without duplicate execution (054)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).IsConcurrencyToken();
        });

        mb.Entity<Credential>(e =>
        {
            e.ToTable("credentials");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.EmploymentId, x.Code }); // assignment gate lookups (058)
            e.HasIndex(x => x.EmploymentId);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<WorkforcePlan>(e =>
        {
            e.ToTable("workforce_plans");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("workforce_plan_lines");
                l.WithOwner().HasForeignKey("WorkforcePlanId");
                l.HasKey("WorkforcePlanId", "PositionCode");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("WorkforcePlanId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<AllocationSet>(e =>
        {
            e.ToTable("allocation_sets");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("allocation_lines");
                l.WithOwner().HasForeignKey("AllocationSetId");
                l.HasKey("AllocationSetId", "CostCenter");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("AllocationSetId");
                l.HasIndex("TenantId", "LegalEntityId");
            });
        });
    }

    private static void ConfigureFinance(ModelBuilder mb)
    {
        mb.Entity<Journal>(e =>
        {
            e.ToTable("journals");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ReversalOfId);
            // money-hardening (same family as invoices/sales_adjustments review 1a): status is a
            // concurrency token — every journal UPDATE carries WHERE status = <original>, so a
            // stale second approval of one Pending journal updates ZERO rows and
            // DbUpdateConcurrencyException surfaces as 409 JOURNAL_ALREADY_POSTED (no duplicate
            // control lines, no double approval trail)
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasOne<Journal>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("journal_lines");
                l.WithOwner().HasForeignKey("JournalId");
                l.Property<int>("Id").ValueGeneratedOnAdd();
                l.HasKey("JournalId", "Id");
                l.Property<string>("TenantId");
                l.Property<string>("LegalEntityId");
                l.HasIndex("JournalId");
                l.HasIndex("TenantId", "LegalEntityId");
                // P5 (003): line dimension assignments ride as a jsonb bag — read back as a
                // dictionary, never queried server-side (validation loads the rows it needs)
                l.Property(x => x.Dimensions).HasColumnType("jsonb").HasConversion(
                    d => d == null ? null : System.Text.Json.JsonSerializer.Serialize(d, (System.Text.Json.JsonSerializerOptions?)null),
                    s => string.IsNullOrEmpty(s) ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s, (System.Text.Json.JsonSerializerOptions?)null));
            });
        });

        // task-5 finance configuration tables — tenant+entity scope on every lookup
        mb.Entity<FiscalPeriod>(e =>
        {
            e.ToTable("fiscal_periods");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Name }).IsUnique();
        });

        mb.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            // CoA basics: one live account per code this slice (no edit endpoint yet; Version column
            // is the versioning hook — FINANCE-001 full revision ranges deferred)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<TaxCode>(e =>
        {
            e.ToTable("tax_codes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }); // revisions share Code
        });

        mb.Entity<FinanceSetting>(e =>
        {
            e.ToTable("finance_settings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Key }).IsUnique();
        });

        // -------- FINANCE-deep P5 foundations (tenant+entity on every table from day one) --------
        mb.Entity<Currency>(e =>
        {
            e.ToTable("currencies");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<FxRate>(e =>
        {
            e.ToTable("fx_rates");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            // 008: the resolver's lookup shape — pair + date + type over approved rows
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.BaseCurrency, x.QuoteCurrency, x.RateDate, x.RateType, x.Status });
        });

        mb.Entity<DimensionSet>(e =>
        {
            e.ToTable("dimension_sets");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Code }).IsUnique();
        });

        mb.Entity<DimensionValue>(e =>
        {
            e.ToTable("dimension_values");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SetCode });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.SetCode, x.Value }).IsUnique();
        });

        mb.Entity<NumberSeriesVoid>(e =>
        {
            e.ToTable("number_series_voids");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Prefix, x.Number }).IsUnique();
        });

        // -------- FINANCE-deep P1 (004 calendars / 062 close checklists) --------
        mb.Entity<FiscalCalendar>(e =>
        {
            e.ToTable("fiscal_calendars");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            // one layout per (entity, year, pattern) — CALENDAR_EXISTS pre-check backstop
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Year, x.Pattern }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.StartDate }); // coverage-chain scans
            e.OwnsMany(x => x.Periods, p =>
            {
                p.ToTable("fiscal_calendar_periods");
                p.WithOwner().HasForeignKey("CalendarId");
                p.Property<int>("LineNo").ValueGeneratedOnAdd();
                p.HasKey("CalendarId", "LineNo");
                p.Property<string>("TenantId");
                p.Property<string>("LegalEntityId");
                p.HasIndex("CalendarId");
                p.HasIndex("TenantId", "LegalEntityId");
            });
        });

        mb.Entity<CloseTask>(e =>
        {
            e.ToTable("close_tasks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PeriodId }); // gate scans (063)
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status }); // dashboard buckets (062)
            // P1 review 7 (finance.md status+token rule): the pending→terminal flip is a token —
            // a stale Complete-vs-waive/NA write matches ZERO rows and DbUpdateConcurrencyException
            // surfaces as 409 CLOSE_TASK_STATE_ERROR, never a silent overwrite of close evidence
            e.Property(x => x.Status).IsConcurrencyToken();
        });
    }

    private static void ConfigureAssets(ModelBuilder mb)
    {
        // tag/serial uniqueness is app-level over ACTIVE assets only (041 AC1 — a disposed asset
        // frees its tag for the re-tagging workflow), so the index is deliberately non-unique
        mb.Entity<FixedAsset>(e =>
        {
            e.ToTable("fixed_assets");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Tag });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Serial });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Status });
            e.HasIndex(x => x.LocationId);
        });

        mb.Entity<AssetEvent>(e =>
        {
            e.ToTable("asset_events");
            e.HasKey(x => x.Id);
            // journal-race hardening: the pending→approved flip is JournalId ""→journal id — a
            // token so a stale second approval matches zero rows and posts no second journal
            e.Property(x => x.JournalId).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.AssetId);
            e.HasIndex(x => new { x.AssetId, x.EventType });
            e.HasOne<FixedAsset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<DepreciationRun>(e =>
        {
            e.ToTable("depreciation_runs");
            // composite PK (R-T4l): the run id is the client's Idempotency-Key — without the
            // tenant/entity columns in the key, a second entity reusing "2026-11" hits 23505
            e.HasKey(x => new { x.Id, x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            // journal-race hardening: one run per period is a DB invariant now — the app-level
            // already-posted check is read-then-write and two concurrent runs both pass it
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PeriodId }).IsUnique();
            e.HasOne<Journal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ManagerId);
        });

        mb.Entity<ProjectTask>(e =>
        {
            e.ToTable("project_tasks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.ProjectId);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ServiceCase>(e =>
        {
            e.ToTable("service_cases");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId });
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.DueAt }); // queue/breach scans
            e.HasIndex(x => x.RequesterId);
        });
    }

    private static void ConfigureIdentity(ModelBuilder mb)
    {
        // users and roles keep their record shape — tenant scope is shadow columns (task-2 §2:
        // every table carries tenant_id + legal_entity_id)
        mb.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email);
            e.Property<string>("TenantId");
            e.Property<string>("LegalEntityId");
            e.HasIndex("TenantId", "LegalEntityId");
        });

        mb.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasKey(x => x.Id);
            e.Property<string>("TenantId");
            e.Property<string>("LegalEntityId");
            e.HasIndex("TenantId", "LegalEntityId");
        });

        mb.Entity<Membership>(e =>
        {
            e.ToTable("memberships");
            e.HasKey(x => new { x.UserId, x.TenantId, x.LegalEntityId });
            e.HasIndex(x => x.RoleId);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePlatform(ModelBuilder mb)
    {
        // audit/outbox carry legal_entity_id as a shadow column (the records hold TenantId only)
        mb.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.HasKey(x => x.Id);
            e.Property<string>("LegalEntityId"); // shadow column — the record holds TenantId only
            e.HasIndex("TenantId", "LegalEntityId");
        });

        mb.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id);
            e.Property<string>("LegalEntityId");
            e.HasIndex("TenantId", "LegalEntityId");
        });

        mb.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency");
            e.HasKey(x => new { x.TenantId, x.LegalEntityId, x.Key }); // unique (tenant, entity, key) — task-2 §2
        });
    }

    private static void ConfigureSequences(ModelBuilder mb)
    {
        // doc numbering: one sequence per prefix (task-2 §2) — A starts at 2 because A-0001 is
        // the seeded opening audit event; all other ids start at 1. T7 adds the SALES deep
        // prefixes (lead/int/cc/opp/inv/crd/dbt/rem/dsp — no collisions with existing prefixes);
        // NumberSeries.Next THROWS on a missing sequence, so sequences ship with the code that
        // uses them (AddSalesDeepSeed carries the CreateSequence calls)
        foreach (var prefix in new[] { "sup", "po", "rc", "cus", "so", "shp", "wh", "loc", "trf", "cnt", "adj", "j", "e",
                     "fa", "fae", "prj", "tsk", "case", "dep",
                     "lead", "int", "cc", "opp", "inv", "crd", "dbt", "rem", "dsp",
                     // task-6 procurement deep prefixes (brief R-T6 list) — ship with the code that
                     // calls them in AddProcurementDeepSeed, since NumberSeries.Next throws if missing
                     "ev", "lot", "qa", "bid", "awd", "ctr", "amd", "obl",
                     // MFG deep prefixes (BOM/MFO/ISS/CPL/SCR) — ship with AddManufacturingDeepSeed
                     "bom", "mfo", "iss", "cpl", "scr",
                     // MFG deferred screens (MF1-MF10) — routings/work-centers (rot, wkc),
                     // scheduling (srn runs, scn scenarios, cfl conflicts), MRP (mrp), shop-floor
                     // (lbt labor, cme counters, dwn downtime, wtr transfers, mcr corrections),
                     // subcontract (sub), maintenance (mav), quality (qpl plans, qin inspections,
                     // qhd holds, ncr nonconformances, capa cases), genealogy (txp events, rwk
                     // rework, vln variances). doc_sch went to PEOPLE schedules — MFG uses srn.
                     "rot", "wkc", "srn", "scn", "cfl", "mrp", "lbt", "cme", "dwn", "wtr", "mcr",
                     "sub", "mav", "qpl", "qin", "qhd", "ncr", "cpa", "txp", "rwk", "vln",
                     // PEOPLE deep prefixes (AddPeopleDeepSeed). doc_per is reserved: R-PEO3 collapsed
                     // the person entity into the worker register, so nothing consumes it today — the
                     // sequence stays so a later person split keeps its id-space
                     "per", "wkr", "cmp", "tme", "tmc", "prn",
                     // deferred-screens PEOPLE prefixes (brief §5 partition table) — ship with
                     // AddPeopleDeferredScreens, since NumberSeries.Next throws on a missing sequence
                     "org", "pos", "obt", "job", "grd", "sch", "lty", "lag", "lrq", "lle", "atx",
                     "txe", "cgr", "gro", "bpl", "enr", "pay", "ppc", "cre", "wfp", "als",
                     // deferred PROCUREMENT wave prefixes — doc_dsp/doc_scr from the brief's list are
                     // OMITTED: they already exist (sales disputes / MFG scrap), so dispatches and
                     // scorecards use deterministic natural ids instead
                     "req", "rql", "cat", "uom", "bko", "blr", "asn", "ins", "rtn", "clm", "qnr", "evd", "obs",
                     // deferred SALES wave prefixes (AddSalesDeferredScreens): quotes, price
                     // books, recurring schedules, deliveries, RMAs, receipt candidates, rebates
                     "qte", "pbl", "rcs", "dlv", "rma", "rcp", "rbt",
                     // FINANCE-deep P5 (AddFinanceDeepP5): FX rates. StartsAt 3 because FXR-0001/
                     // FXR-0002 are seeded rows — the same A-0001 convention.
                     "fxr",
                     // FINANCE-deep P1 (AddFinanceDeepP1): fiscal calendars + close tasks. Both
                     // start at 2 because FCAL-0001 (draft FY2027 4-4-5 demo) and CTK-0001
                     // (mandatory reconciliation task on the demo next-month period) are seeded
                     // rows — the same A-0001 convention.
                     "fcal", "ctk" })
            mb.HasSequence<long>($"doc_{prefix}");
        mb.HasSequence<long>("doc_a", s => s.StartsAt(2));
        mb.HasSequence<long>("doc_fxr", s => s.StartsAt(3));
        mb.HasSequence<long>("doc_fcal", s => s.StartsAt(2));
        mb.HasSequence<long>("doc_ctk", s => s.StartsAt(2));
    }

    private static void SnakeCaseAllColumns(ModelBuilder mb)
    {
        // physical columns are snake_case (tenant_id, legal_entity_id, …); table names are set
        // explicitly per aggregate above, so only column names are renamed here
        foreach (var entity in mb.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnake(property.Name));
    }

    private static string ToSnake(string name) =>
        Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();

    // Task-2 §6: migration-seeded reference data — ids byte-identical to the Task-1 in-memory seed
    private static void Seed(ModelBuilder mb)
    {
        var t = EntityContext.SeedTenantId;
        var le = EntityContext.SeedEntityId;

        mb.Entity<Role>().HasData(
            new { Id = "R-ADMIN", Name = "tenant_admin", Permissions = new[] { "*" }, TenantId = t, LegalEntityId = le },
            // R-PROC/R-FIN/R-SALES = union of the T7 (sales) and T6 (procurement) permission sets;
            // R-FIN also carries the PEOPLE additions (payroll:approve gates run approval AND
            // posting, R-PEO9) — the row is the FULL surviving superset (merge lesson: UpdateData
            // replaces the whole permissions column, so AddPeopleDeepSeed must ship the union of
            // AddFinanceSeed + AddSalesDeepSeed + AddProcurementDeepSeed + payroll, never a delta)
            // deferred PROCUREMENT wave: the rows below are the FULL surviving union of the prior
            // seed + both deferred waves' additions (merge lesson: UpdateData replaces the whole
            // column, never a delta). R-PROC + requisitions/taxonomy/inspection/analytics; R-FIN +
            // PROC approval/review/override/analytics AND the PEOPLE additions (leave:adjust 032,
            // payroll:release 050, payroll:close 054, people:analytics:read 061). R-PROC2 = second
            // procurement officer for maker-checker tests (Q-S1); R-QC = quality authority for
            // inspections/dispositions (Q-P7, MFG consumes it later and must never re-seed it).
            new { Id = "R-PROC", Name = "procurement_officer", Permissions = new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read", "requisitions:write", "requisitions:read", "catalog:write", "receipts:inspect", "analytics:read" }, TenantId = t, LegalEntityId = le },
            new { Id = "R-FIN", Name = "finance_officer", Permissions = new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write",
                    // deferred-screens SALES union (S7/S9/S8 refunds/rebates/ar-recon — money
                    // leaves the company only through R-FIN, 062/063; forecast read 018)
                    "refunds:approve", "rebates:settle", "forecasts:read",
                    // deferred-screens PEOPLE additions (brief §3): leave:adjust (032 elevated manual
                    // ledger), payroll:release (payment-file/garnishment release ≠ calculation
                    // approval, 050), payroll:close (period close + waivers, 054),
                    // people:analytics:read (061 — R-HR and R-FIN both read, suppression enforced
                    // server-side); then deferred PROCUREMENT additions: requisitions approve/read/
                    // override, suppliers:review, analytics:read
                    "leave:adjust", "payroll:release", "payroll:close", "people:analytics:read",
                    "requisitions:approve", "requisitions:read", "requisitions:override", "suppliers:review", "analytics:read",
                    // FINANCE-deep wave: the FULL §3 superset union ships NOW so later phases
                    // (P1 periods/close, P2 tax publish, P3 AP, P4 AR/GL) never re-union this row
                    // (merge lesson: UpdateData replaces the whole permissions column, never a
                    // delta). fx:read/fx:write = currency/FX admin (006/008) + number-series admin
                    // (016); close:write/close:approve (062/063); tax:publish (042);
                    // ap:write/approve/override (019–026); ar:write (028–030); gl:write (018).
                    "fx:read", "fx:write", "close:write", "close:approve", "tax:publish",
                    "ap:write", "ap:approve", "ap:override", "ar:write", "gl:write" }, TenantId = t, LegalEntityId = le },
            // FINANCE-deep Q-F6: the maker-only finance role (mirrors R-PROC2) — journals:write,
            // tax read/write, ap read/write, ar:write, periods:read; NO approve/publish/close/fx —
            // every money gate stays with R-FIN proper so U-015 can draft but never release
            new { Id = "R-FIN2", Name = "finance_maker", Permissions = new[] { "journals:write", "periods:read", "tax:read", "tax:write", "ap:read", "ap:write", "ar:write" }, TenantId = t, LegalEntityId = le },
            new { Id = "R-SALES", Name = "sales_officer", Permissions = new[] { "customers:write", "so:write", "shipments:write",
                    "so:approve",
                    "leads:read", "leads:write", "crm:read", "crm:write", "opps:read", "opps:write",
                    "invoices:read", "invoices:write", "collections:read", "collections:write",
                    // deferred-screens SALES union (S1/S2/S4/S5/S7/S8/S9 — quotes, pricing,
                    // recurring, forecast, returns, receipt candidates, rebates; Q-S1/Q-S2)
                    "quotes:write", "quotes:approve", "pricebooks:write", "recurring:write",
                    "forecasts:read", "forecasts:write", "returns:write",
                    "receipts-candidates:write", "rebates:write" }, TenantId = t, LegalEntityId = le },
            new { Id = "R-ASSETS", Name = "assets_officer", Permissions = new[] { "assets:read", "assets:write", "assets:approve", "projects:write", "projects:approve", "cases:write", "cases:read" }, TenantId = t, LegalEntityId = le },
            // task-6: independent evaluators (031 AC1) — read + score only, no buyer permissions
            new { Id = "R-EVAL", Name = "evaluator", Permissions = new[] { "sourcing:read", "sourcing:evaluate" }, TenantId = t, LegalEntityId = le },
            // MFG deep (R-MFG9): every MFG gate sits on R-MFG — approver ≠ actor on boms:approve,
            // mfg:release and mfg:close; journals post service-side with the period guard only.
            // MFG deferred screens extend the row with the FULL surviving superset (merge lesson:
            // UpdateData replaces the whole permissions column): scheduling (mfg:schedule), MRP
            // (mfg:mrp), shop floor (mfg:shopfloor), rework approval (mfg:rework) and quality plan
            // authoring (quality:plan — 055 SoD triple; inspect/dispose stay on R-QC, seeded by
            // PROCUREMENT, never here).
            new { Id = "R-MFG", Name = "manufacturing_officer",
                Permissions = new[] { "boms:read", "boms:write", "boms:approve", "mfg:read", "mfg:write", "mfg:release", "mfg:override", "mfg:close", "mfg:reopen", "mfg:schedule", "mfg:mrp", "mfg:shopfloor", "mfg:rework", "quality:plan" },
                TenantId = t, LegalEntityId = le },
            // PEOPLE deep (R-PEO9): R-HR is the maker — register/time/payroll:calculate plus the
            // correction-checker permission (time:approve, approver ≠ requester, 027). payroll:approve
            // stays with R-FIN, so the run's calculator and approver are different people by role.
            new { Id = "R-HR", Name = "hr_officer", Permissions = new[] { "workers:read", "workers:write", "time:read", "time:write", "time:approve", "payroll:read", "payroll:calculate",
                    // deferred-screens additions (brief §3): HR maker + checker domains — org/positions,
                    // onboarding, job catalog, schedules, leave (write + approve ≠ requester),
                    // credentials, pay inputs, workforce plans + analytics (server-side suppression)
                    "org:write", "onboarding:write", "compmaster:write", "schedules:write", "leave:write",
                    "leave:approve", "credentials:write", "payinputs:write", "workforce:write", "people:analytics:read" }, TenantId = t, LegalEntityId = le },
            // deferred PROCUREMENT wave (Q-S1/Q-P7): full-union copies — R-PROC2 mirrors R-PROC so
            // maker-checker has two procurement officers; R-QC is the quality authority (MFG consumes)
            new { Id = "R-PROC2", Name = "procurement_officer_2", Permissions = new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read", "requisitions:write", "requisitions:read", "catalog:write", "receipts:inspect", "analytics:read" }, TenantId = t, LegalEntityId = le },
            new { Id = "R-QC", Name = "quality_officer", Permissions = new[] { "receipts:read", "receipts:inspect", "receipts:dispose", "suppliers:review", "analytics:read",
                    // union-merge (MFG deferred wave): MF8 quality endpoints gate quality:inspect /
                    // quality:dispose and their GET routes gate mfg:read — MFG's wave fixture carried
                    // these on R-QC; folded into the seed so the PROC-seeded officer can inspect
                    "quality:inspect", "quality:dispose", "mfg:read" }, TenantId = t, LegalEntityId = le });

        mb.Entity<User>().HasData(
            new { Id = "U-001", Email = "admin@teamzlab.dev", DisplayName = "Tenant Admin", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-002", Email = "procurve@teamzlab.dev", DisplayName = "Procure Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-003", Email = "finance@teamzlab.dev", DisplayName = "Finance Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-004", Email = "sales@teamzlab.dev", DisplayName = "Sales Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-005", Email = "assets@teamzlab.dev", DisplayName = "Assets Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-006", Email = "evaluator@teamzlab.dev", DisplayName = "Independent Evaluator", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-008", Email = "mfg@teamzlab.dev", DisplayName = "Manufacturing Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-007", Email = "hr@teamzlab.dev", DisplayName = "HR Officer", Active = true, TenantId = t, LegalEntityId = le },
            // Q-PM5: the second HR user — maker-checker on HR approval paths (onboarding waivers,
            // leave approvals ≠ requester, credential waivers) is untestable with one user per role
            new { Id = "U-010", Email = "hr-checker@teamzlab.dev", DisplayName = "HR Checker", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-009", Email = "procurve2@teamzlab.dev", DisplayName = "Procure Officer 2", Active = true, TenantId = t, LegalEntityId = le },
            // union-merge renumber: PROC's branch seeded the quality officer as U-010, but the
            // landed PEOPLE wave already took U-010 for the HR checker — the QC user ships as
            // U-011 (ProcurementDeferredTests.Qc matches)
            new { Id = "U-011", Email = "quality@teamzlab.dev", DisplayName = "Quality Officer", Active = true, TenantId = t, LegalEntityId = le },
            // union-merge renumber: MFG's branch seeded the planner as U-011, but the PROC merge
            // took U-011 for the quality officer — the planner ships as U-012
            // (ManufacturingDeferredTests.Planner matches). Q-PM5: a second MFG user so
            // maker-checker is exercisable INSIDE R-MFG (schedule publish override, rework
            // approval ≠ completion actor, labor approval ≠ reporter)
            new { Id = "U-012", Email = "mfg.planner@teamzlab.dev", DisplayName = "Manufacturing Planner", Active = true, TenantId = t, LegalEntityId = le },
            // union-merge renumber: SALES's branch seeded the sales manager as U-009, but the
            // PROC merge took U-009 for the second procurement officer — the manager ships as
            // U-014 (SalesDeferredTests.SalesManager matches). U-013 stays seed-free: the MFG
            // WaveD quality fixture inserts its own checker (Qc2) there via direct SQL at
            // test time, and a seeded U-013 would break that fixture's bootstrap
            // Q-S1: second sales user — quote/amendment maker-checker needs a sales-capable
            // approver that is not U-004 (instance-level approver ≠ issuer, 023/034)
            new { Id = "U-014", Email = "sales-manager@teamzlab.dev", DisplayName = "Sales Manager", Active = true, TenantId = t, LegalEntityId = le },
            // FINANCE-deep Q-F6: second finance user for maker-checker (mirrors U-009/U-010).
            // U-013 stays RESERVED (MFG WaveD fixture), U-014 taken (SALES manager) — next free id
            new { Id = "U-015", Email = "fin-checker@teamzlab.dev", DisplayName = "Finance Checker", Active = true, TenantId = t, LegalEntityId = le });

        mb.Entity<Membership>().HasData(
            new Membership("U-001", t, le, "R-ADMIN"),
            new Membership("U-002", t, le, "R-PROC"),
            new Membership("U-003", t, le, "R-FIN"),
            new Membership("U-004", t, le, "R-SALES"),
            new Membership("U-005", t, le, "R-ASSETS"),
            new Membership("U-006", t, le, "R-EVAL"),
            new Membership("U-008", t, le, "R-MFG"),
            new Membership("U-007", t, le, "R-HR"),
            new Membership("U-010", t, le, "R-HR"),
            new Membership("U-009", t, le, "R-PROC2"),
            new Membership("U-011", t, le, "R-QC"),
            new Membership("U-012", t, le, "R-MFG"),
            new Membership("U-014", t, le, "R-SALES"),
            new Membership("U-015", t, le, "R-FIN2"));

        mb.Entity<AuditEvent>().HasData(new
        {
            Id = "A-0001",
            TenantId = t,
            LegalEntityId = le,
            ActorUserId = "system",
            Action = "seed",
            EntityType = "tenant",
            EntityId = t,
            At = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Summary = "Seeded demo tenant, entity, users, roles",
        });

        mb.Entity<Warehouse>().HasData(new Warehouse("WH-001", t, le, "WH-001", "Main Warehouse"));
        mb.Entity<StockLocation>().HasData(
            new StockLocation("LOC-A1", t, le, "WH-001", "A1", "Rack A1"),
            new StockLocation("LOC-B1", t, le, "WH-001", "B1", "Rack B1"));

        // PEOPLE deep seed: PG-001 pay group (035 trim — the only group in the pilot) and the two
        // payroll liability accounts. ACC-6100 (salary expense) already exists in AddFinanceSeed
        // and is REUSED — no second salary expense code (ruling). The gross→net journal posts
        // Dr 6100 / Cr 2310 deductions / Cr 2300 net.
        mb.Entity<PayGroup>().HasData(
            new PayGroup("PG-001", t, le, "PG-001", "Monthly Staff", "Monthly", true));

        mb.Entity<Account>().HasData(
            new Account("ACC-2300", t, le, "2300", "Salaries Payable", AccountType.Liability, NormalBalance.Credit,
                true, 1, "system", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new Account("ACC-2310", t, le, "2310", "Payroll Deductions Payable", AccountType.Liability, NormalBalance.Credit,
                true, 1, "system", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        // R-PEO2: the overtime multiplier is a finance setting (data, not a constant); default 1.5
        mb.Entity<FinanceSetting>().HasData(
            new FinanceSetting("FS-payroll-ot", t, le, "payroll.ot_multiplier", "1.5"));

        // deferred-screens SALES (data, not constants): quote approval threshold (023 AC1),
        // discount stacking cap (025 AC2), forecast stage weights (018 AC1 weighted bucket)
        mb.Entity<FinanceSetting>().HasData(
            new FinanceSetting("FS-quotes-threshold", t, le, "quotes.approval_threshold", "10000"),
            new FinanceSetting("FS-discount-cap", t, le, "sales.discount_cap_pct", "15"),
            new FinanceSetting("FS-stage-weights", t, le, "sales.stage_weights",
                "{\"Prospecting\":0.1,\"Qualification\":0.2,\"Discovery\":0.3,\"Proposal\":0.5,\"Negotiation\":0.75,\"Closed\":0.9}"));

        // deferred PROCUREMENT wave: the one active requisition approval rule route resolution falls
        // back to (Q-P2 fixed maker-checker — the row's version is snapshotted onto approvals, no
        // matrix engine); budget-check stub mode lives in finance_settings, default Pass
        mb.Entity<ApprovalRule>().HasData(
            new ApprovalRule("AR-REQ-DEFAULT", t, le, "requisition_default", 1, true, "R-FIN", null, "system",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        mb.Entity<FinanceSetting>().HasData(
            new FinanceSetting("FS-budget-mode", t, le, "budget.check_mode", "Pass"));

        // MF5 (046 AC1): the effective downtime reason profile is tenant data — the shop-floor
        // downtime validator fails closed when the row is missing
        mb.Entity<FinanceSetting>().HasData(
            new FinanceSetting("FS-mfg-downtime", t, le, "mfg.downtime_reasons", "SETUP,MACHINE_DOWN,TOOL_CHANGE,NO_OPERATOR,MATERIAL_SHORT,QUALITY_STOP"));

        // -------- FINANCE-deep P5 foundations (006/007/008/003) --------
        // currency master: the 0/2/3 settlement-scale family (006 AC2) — BDT is the entity's
        // functional currency, USD the demo foreign pair, JPY the 0-decimal edge
        mb.Entity<Currency>().HasData(
            new Currency("CUR-BDT", t, le, "BDT", "Bangladeshi Taka", 2, true),
            new Currency("CUR-USD", t, le, "USD", "US Dollar", 2, true),
            new Currency("CUR-JPY", t, le, "JPY", "Japanese Yen", 0, true));

        // 008: one approved USD→BDT rate WITH full provenance (the resolver's happy fixture) and
        // one pending JPY→BDT rate so the approval path has something to act on. doc_fxr starts
        // at 3 so API-created ids never collide with these rows.
        mb.Entity<FxRate>().HasData(
            new FxRate("FXR-0001", t, le, "USD", "BDT", new DateOnly(2026, 1, 1), FxRateType.Spot, 122.5000m, 4,
                "seed:bn-bank-indicator", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                FxRateStatus.Approved, "system", "system", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new FxRate("FXR-0002", t, le, "JPY", "BDT", new DateOnly(2026, 1, 1), FxRateType.Spot, 0.8200m, 4,
                "seed:bn-bank-indicator", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                FxRateStatus.Pending, "system"));

        // 003 demo dimension set. RULING: Required ships FALSE — a REQUIRED seeded set would put
        // a dimension gate under every existing journal create (all prior waves' tests + flows);
        // P5 tests create their own required set, later phases may flip this row via the API.
        mb.Entity<DimensionSet>().HasData(
            new DimensionSet("DS-CostCenter", t, le, "CostCenter", "Cost Center", false, true));

        // permitted values with their own intervals: CC-001 open-ended active; CC-002 INACTIVE but
        // still inside its 2026 interval — after the P5 fix round this is a REJECTION fixture for
        // NEW postings (Active+interval required at create; 003 AC2's inactive-in-interval clause
        // lives on for POSTED history only, which is never re-validated)
        mb.Entity<DimensionValue>().HasData(
            new DimensionValue("DV-CostCenter-CC-001", t, le, "CostCenter", "CC-001", new DateOnly(2026, 1, 1), null, true),
            new DimensionValue("DV-CostCenter-CC-002", t, le, "CostCenter", "CC-002", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false));

        // 007 (ratified ruling, fix round): the functional currency is a single-row upsert — a
        // change overwrites the row with its EffectiveFrom, so journal drafts dated BEFORE that
        // date are rejected (there is no previous value left to resolve against) and drafts
        // on/after it use the new value. Posted journals are never restated — their functional
        // amounts are stored on the lines; the change trail lives in audit_events.
        mb.Entity<FinanceSetting>().HasData(
            new FinanceSetting("FS-functional-currency", t, le, "finance.functional_currency", "BDT", new DateOnly(2026, 1, 1)));

        // deferred PROCUREMENT follow-up (click-through fu-2): the taxonomy a requisition
        // submission consumes on a brand-new database — the EA unit the web requisition form
        // defaults to (base, integer precision) and one active category. Explicit ids outside
        // the doc_cat/doc_uom sequence spaces (CAT-0001/UOM-0001 style) so API-created rows
        // can never collide with these.
        mb.Entity<ProcurementCategory>().HasData(
            new ProcurementCategory("CAT-SEED-001", t, le, null, "General Office Supplies", CategoryStatus.Active,
                new DateOnly(2026, 1, 1), null, null, "system", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        mb.Entity<UnitOfMeasure>().HasData(
            new UnitOfMeasure("UOM-SEED-EA", t, le, "EA", "Each", UomDimension.Count, 0, true, "system",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
