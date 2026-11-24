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
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<FixedAsset> Assets => Set<FixedAsset>();
    public DbSet<AssetEvent> AssetEvents => Set<AssetEvent>();
    public DbSet<DepreciationRun> DepreciationRuns => Set<DepreciationRun>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ServiceCase> ServiceCases => Set<ServiceCase>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();
    public DbSet<FinanceSetting> FinanceSettings => Set<FinanceSetting>();
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
        ConfigureInventory(mb);
        ConfigureManufacturing(mb);
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
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.PeriodId });
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
                     "bom", "mfo", "iss", "cpl", "scr" })
            mb.HasSequence<long>($"doc_{prefix}");
        mb.HasSequence<long>("doc_a", s => s.StartsAt(2));
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
            // R-PROC/R-FIN/R-SALES = union of the T7 (sales) and T6 (procurement) permission sets
            new { Id = "R-PROC", Name = "procurement_officer", Permissions = new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read" }, TenantId = t, LegalEntityId = le },
            new { Id = "R-FIN", Name = "finance_officer", Permissions = new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:approve" }, TenantId = t, LegalEntityId = le },
            new { Id = "R-SALES", Name = "sales_officer", Permissions = new[] { "customers:write", "so:write", "shipments:write",
                    "so:approve",
                    "leads:read", "leads:write", "crm:read", "crm:write", "opps:read", "opps:write",
                    "invoices:read", "invoices:write", "collections:read", "collections:write" }, TenantId = t, LegalEntityId = le },
            new { Id = "R-ASSETS", Name = "assets_officer", Permissions = new[] { "assets:read", "assets:write", "assets:approve", "projects:write", "projects:approve", "cases:write", "cases:read" }, TenantId = t, LegalEntityId = le },
            // task-6: independent evaluators (031 AC1) — read + score only, no buyer permissions
            new { Id = "R-EVAL", Name = "evaluator", Permissions = new[] { "sourcing:read", "sourcing:evaluate" }, TenantId = t, LegalEntityId = le },
            // MFG deep (R-MFG9): every MFG gate sits on R-MFG — approver ≠ actor on boms:approve,
            // mfg:release and mfg:close; journals post service-side with the period guard only
            new { Id = "R-MFG", Name = "manufacturing_officer",
                Permissions = new[] { "boms:read", "boms:write", "boms:approve", "mfg:read", "mfg:write", "mfg:release", "mfg:override", "mfg:close", "mfg:reopen" },
                TenantId = t, LegalEntityId = le });

        mb.Entity<User>().HasData(
            new { Id = "U-001", Email = "admin@teamzlab.dev", DisplayName = "Tenant Admin", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-002", Email = "procurve@teamzlab.dev", DisplayName = "Procure Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-003", Email = "finance@teamzlab.dev", DisplayName = "Finance Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-004", Email = "sales@teamzlab.dev", DisplayName = "Sales Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-005", Email = "assets@teamzlab.dev", DisplayName = "Assets Officer", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-006", Email = "evaluator@teamzlab.dev", DisplayName = "Independent Evaluator", Active = true, TenantId = t, LegalEntityId = le },
            new { Id = "U-008", Email = "mfg@teamzlab.dev", DisplayName = "Manufacturing Officer", Active = true, TenantId = t, LegalEntityId = le });

        mb.Entity<Membership>().HasData(
            new Membership("U-001", t, le, "R-ADMIN"),
            new Membership("U-002", t, le, "R-PROC"),
            new Membership("U-003", t, le, "R-FIN"),
            new Membership("U-004", t, le, "R-SALES"),
            new Membership("U-005", t, le, "R-ASSETS"),
            new Membership("U-006", t, le, "R-EVAL"),
            new Membership("U-008", t, le, "R-MFG"));

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
    }
}
