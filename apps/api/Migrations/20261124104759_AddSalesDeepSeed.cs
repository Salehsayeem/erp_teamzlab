using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124104759_AddSalesDeepSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_cc");

        migrationBuilder.CreateSequence(
            name: "doc_crd");

        migrationBuilder.CreateSequence(
            name: "doc_dbt");

        migrationBuilder.CreateSequence(
            name: "doc_dsp");

        migrationBuilder.CreateSequence(
            name: "doc_int");

        migrationBuilder.CreateSequence(
            name: "doc_inv");

        migrationBuilder.CreateSequence(
            name: "doc_lead");

        migrationBuilder.CreateSequence(
            name: "doc_opp");

        migrationBuilder.CreateSequence(
            name: "doc_rem");

        migrationBuilder.AddColumn<decimal>(
            name: "credit_limit",
            table: "customers",
            type: "numeric",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "payment_term_net_days",
            table: "customers",
            type: "integer",
            nullable: false,
            defaultValue: 30); // NET30 is the documented house default (053)

        migrationBuilder.AddColumn<bool>(
            name: "reminders_suppressed",
            table: "customers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "crm_cases",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: true),
                category = table.Column<string>(type: "text", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                owner_id = table.Column<string>(type: "text", nullable: true),
                response_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                resolution = table.Column<string>(type: "text", nullable: true),
                sales_order_id = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_cases", x => x.id);
                table.ForeignKey(
                    name: "FK_crm_cases_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_cases_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "interactions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                party_type = table.Column<int>(type: "integer", nullable: false),
                party_id = table.Column<string>(type: "text", nullable: false),
                channel = table.Column<string>(type: "text", nullable: false),
                subject = table.Column<string>(type: "text", nullable: false),
                notes = table.Column<string>(type: "text", nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_interactions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "invoices",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                sales_order_id = table.Column<string>(type: "text", nullable: false),
                source_event_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: true),
                payment_term_code = table.Column<string>(type: "text", nullable: false),
                sub_total = table.Column<decimal>(type: "numeric", nullable: false),
                tax_total = table.Column<decimal>(type: "numeric", nullable: false),
                total = table.Column<decimal>(type: "numeric", nullable: false),
                journal_id = table.Column<string>(type: "text", nullable: true),
                posted_by = table.Column<string>(type: "text", nullable: true),
                posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoices", x => x.id);
                table.ForeignKey(
                    name: "FK_invoices_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_invoices_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "leads",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                company = table.Column<string>(type: "text", nullable: true),
                contact_name = table.Column<string>(type: "text", nullable: true),
                email = table.Column<string>(type: "text", nullable: true),
                phone = table.Column<string>(type: "text", nullable: true),
                source = table.Column<string>(type: "text", nullable: false),
                source_ref = table.Column<string>(type: "text", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                reason_code = table.Column<string>(type: "text", nullable: true),
                owner_id = table.Column<string>(type: "text", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                tax_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_leads", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "collection_reminders",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                invoice_id = table.Column<string>(type: "text", nullable: false),
                channel = table.Column<string>(type: "text", nullable: false),
                template_version = table.Column<string>(type: "text", nullable: true),
                recipient = table.Column<string>(type: "text", nullable: false),
                reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_collection_reminders", x => x.id);
                table.ForeignKey(
                    name: "FK_collection_reminders_invoices_invoice_id",
                    column: x => x.invoice_id,
                    principalTable: "invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "disputes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                invoice_id = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                promise_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                outcome = table.Column<string>(type: "text", nullable: true),
                owner_id = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_disputes", x => x.id);
                table.ForeignKey(
                    name: "FK_disputes_invoices_invoice_id",
                    column: x => x.invoice_id,
                    principalTable: "invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "invoice_lines",
            columns: table => new
            {
                invoice_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                line_net = table.Column<decimal>(type: "numeric", nullable: false),
                tax_code_id = table.Column<string>(type: "text", nullable: true),
                tax_code_version = table.Column<int>(type: "integer", nullable: true),
                tax_amount = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoice_lines", x => new { x.invoice_id, x.line_no });
                table.ForeignKey(
                    name: "FK_invoice_lines_invoices_invoice_id",
                    column: x => x.invoice_id,
                    principalTable: "invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sales_adjustments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                invoice_id = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                sub_total = table.Column<decimal>(type: "numeric", nullable: false),
                tax_total = table.Column<decimal>(type: "numeric", nullable: false),
                total = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                journal_id = table.Column<string>(type: "text", nullable: true),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_adjustments", x => x.id);
                table.ForeignKey(
                    name: "FK_sales_adjustments_invoices_invoice_id",
                    column: x => x.invoice_id,
                    principalTable: "invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "opportunities",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: true),
                lead_id = table.Column<string>(type: "text", nullable: true),
                title = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                stage = table.Column<string>(type: "text", nullable: false),
                expected_close_date = table.Column<DateOnly>(type: "date", nullable: true),
                owner_id = table.Column<string>(type: "text", nullable: false),
                loss_reason = table.Column<string>(type: "text", nullable: true),
                competitor = table.Column<string>(type: "text", nullable: true),
                order_id = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_opportunities", x => x.id);
                table.ForeignKey(
                    name: "FK_opportunities_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_opportunities_leads_lead_id",
                    column: x => x.lead_id,
                    principalTable: "leads",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_opportunities_sales_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_adjustment_lines",
            columns: table => new
            {
                adjustment_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                line_net = table.Column<decimal>(type: "numeric", nullable: false),
                tax_code_id = table.Column<string>(type: "text", nullable: true),
                tax_code_version = table.Column<int>(type: "integer", nullable: true),
                tax_amount = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_adjustment_lines", x => new { x.adjustment_id, x.line_no });
                table.ForeignKey(
                    name: "FK_sales_adjustment_lines_sales_adjustments_adjustment_id",
                    column: x => x.adjustment_id,
                    principalTable: "sales_adjustments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "opportunity_lines",
            columns: table => new
            {
                opportunity_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                target_price = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_opportunity_lines", x => new { x.opportunity_id, x.line_no });
                table.ForeignKey(
                    name: "FK_opportunity_lines_opportunities_opportunity_id",
                    column: x => x.opportunity_id,
                    principalTable: "opportunities",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "so:approve", "invoices:approve", "credit:read", "credit:override" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-SALES",
            column: "permissions",
            value: new[] { "customers:write", "so:write", "shipments:write", "so:approve", "leads:read", "leads:write", "crm:read", "crm:write", "opps:read", "opps:write", "invoices:read", "invoices:write", "collections:read", "collections:write" });

        migrationBuilder.CreateIndex(
            name: "IX_collection_reminders_invoice_id",
            table: "collection_reminders",
            column: "invoice_id");

        migrationBuilder.CreateIndex(
            name: "IX_collection_reminders_tenant_id_legal_entity_id",
            table: "collection_reminders",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_collection_reminders_tenant_id_legal_entity_id_invoice_id",
            table: "collection_reminders",
            columns: new[] { "tenant_id", "legal_entity_id", "invoice_id" });

        migrationBuilder.CreateIndex(
            name: "IX_crm_cases_customer_id",
            table: "crm_cases",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_crm_cases_sales_order_id",
            table: "crm_cases",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_crm_cases_tenant_id_legal_entity_id",
            table: "crm_cases",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_crm_cases_tenant_id_legal_entity_id_response_due_at",
            table: "crm_cases",
            columns: new[] { "tenant_id", "legal_entity_id", "response_due_at" });

        migrationBuilder.CreateIndex(
            name: "IX_disputes_invoice_id",
            table: "disputes",
            column: "invoice_id");

        migrationBuilder.CreateIndex(
            name: "IX_disputes_tenant_id_legal_entity_id",
            table: "disputes",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_disputes_tenant_id_legal_entity_id_invoice_id",
            table: "disputes",
            columns: new[] { "tenant_id", "legal_entity_id", "invoice_id" });

        migrationBuilder.CreateIndex(
            name: "IX_interactions_tenant_id_legal_entity_id",
            table: "interactions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_interactions_tenant_id_legal_entity_id_occurred_at",
            table: "interactions",
            columns: new[] { "tenant_id", "legal_entity_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "IX_interactions_tenant_id_legal_entity_id_party_type_party_id",
            table: "interactions",
            columns: new[] { "tenant_id", "legal_entity_id", "party_type", "party_id" });

        migrationBuilder.CreateIndex(
            name: "IX_invoice_lines_invoice_id",
            table: "invoice_lines",
            column: "invoice_id");

        migrationBuilder.CreateIndex(
            name: "IX_invoice_lines_tenant_id_legal_entity_id",
            table: "invoice_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_invoices_customer_id",
            table: "invoices",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_invoices_sales_order_id",
            table: "invoices",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_invoices_tenant_id_legal_entity_id",
            table: "invoices",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_invoices_tenant_id_legal_entity_id_customer_id",
            table: "invoices",
            columns: new[] { "tenant_id", "legal_entity_id", "customer_id" });

        migrationBuilder.CreateIndex(
            name: "IX_invoices_tenant_id_legal_entity_id_source_event_id",
            table: "invoices",
            columns: new[] { "tenant_id", "legal_entity_id", "source_event_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_invoices_tenant_id_legal_entity_id_status",
            table: "invoices",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_leads_tenant_id_legal_entity_id",
            table: "leads",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_leads_tenant_id_legal_entity_id_status",
            table: "leads",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_customer_id",
            table: "opportunities",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_lead_id",
            table: "opportunities",
            column: "lead_id");

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_order_id",
            table: "opportunities",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_tenant_id_legal_entity_id",
            table: "opportunities",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_opportunities_tenant_id_legal_entity_id_status",
            table: "opportunities",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_opportunity_lines_opportunity_id",
            table: "opportunity_lines",
            column: "opportunity_id");

        migrationBuilder.CreateIndex(
            name: "IX_opportunity_lines_tenant_id_legal_entity_id",
            table: "opportunity_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_adjustment_lines_adjustment_id",
            table: "sales_adjustment_lines",
            column: "adjustment_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_adjustment_lines_tenant_id_legal_entity_id",
            table: "sales_adjustment_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_adjustments_invoice_id",
            table: "sales_adjustments",
            column: "invoice_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_adjustments_tenant_id_legal_entity_id",
            table: "sales_adjustments",
            columns: new[] { "tenant_id", "legal_entity_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "collection_reminders");

        migrationBuilder.DropTable(
            name: "crm_cases");

        migrationBuilder.DropTable(
            name: "disputes");

        migrationBuilder.DropTable(
            name: "interactions");

        migrationBuilder.DropTable(
            name: "invoice_lines");

        migrationBuilder.DropTable(
            name: "opportunity_lines");

        migrationBuilder.DropTable(
            name: "sales_adjustment_lines");

        migrationBuilder.DropTable(
            name: "opportunities");

        migrationBuilder.DropTable(
            name: "sales_adjustments");

        migrationBuilder.DropTable(
            name: "leads");

        migrationBuilder.DropTable(
            name: "invoices");

        migrationBuilder.DropColumn(
            name: "credit_limit",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "payment_term_net_days",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "reminders_suppressed",
            table: "customers");

        migrationBuilder.DropSequence(
            name: "doc_cc");

        migrationBuilder.DropSequence(
            name: "doc_crd");

        migrationBuilder.DropSequence(
            name: "doc_dbt");

        migrationBuilder.DropSequence(
            name: "doc_dsp");

        migrationBuilder.DropSequence(
            name: "doc_int");

        migrationBuilder.DropSequence(
            name: "doc_inv");

        migrationBuilder.DropSequence(
            name: "doc_lead");

        migrationBuilder.DropSequence(
            name: "doc_opp");

        migrationBuilder.DropSequence(
            name: "doc_rem");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-SALES",
            column: "permissions",
            value: new[] { "customers:write", "so:write", "shipments:write" });
    }
}
