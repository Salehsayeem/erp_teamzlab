using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261125072734_AddSalesDeferredScreens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_dlv");

        migrationBuilder.CreateSequence(
            name: "doc_pbl");

        migrationBuilder.CreateSequence(
            name: "doc_qte");

        migrationBuilder.CreateSequence(
            name: "doc_rbt");

        migrationBuilder.CreateSequence(
            name: "doc_rcp");

        migrationBuilder.CreateSequence(
            name: "doc_rcs");

        migrationBuilder.CreateSequence(
            name: "doc_rma");

        migrationBuilder.AddColumn<DateOnly>(
            name: "expected_date",
            table: "so_lines",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "source_quote_revision_id",
            table: "sales_orders",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "merged_into_id",
            table: "customers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "parent_customer_id",
            table: "customers",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "customer_contract_prices",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                min_qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_contract_prices", x => x.id);
                table.ForeignKey(
                    name: "FK_customer_contract_prices_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "customer_onboarding_checks",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                check_code = table.Column<string>(type: "text", nullable: false),
                mandatory = table.Column<bool>(type: "boolean", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                met_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                met_by = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_onboarding_checks", x => x.id);
                table.ForeignKey(
                    name: "FK_customer_onboarding_checks_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "einvoice_submissions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                invoice_id = table.Column<string>(type: "text", nullable: false),
                jurisdiction = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<string>(type: "text", nullable: false),
                correlation_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                errors_json = table.Column<string>(type: "text", nullable: true),
                submitted_by = table.Column<string>(type: "text", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_einvoice_submissions", x => x.id);
                table.ForeignKey(
                    name: "FK_einvoice_submissions_invoices_invoice_id",
                    column: x => x.invoice_id,
                    principalTable: "invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "forecast_snapshots",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                period = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                payload_json = table.Column<string>(type: "text", nullable: false),
                generated_by = table.Column<string>(type: "text", nullable: false),
                generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_forecast_snapshots", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "forecast_submissions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                owner_id = table.Column<string>(type: "text", nullable: false),
                period = table.Column<string>(type: "text", nullable: false),
                category = table.Column<int>(type: "integer", nullable: false),
                override_value = table.Column<decimal>(type: "numeric", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false),
                submitted_by = table.Column<string>(type: "text", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_forecast_submissions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "invoice_deliveries",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                invoice_id = table.Column<string>(type: "text", nullable: false),
                channel = table.Column<string>(type: "text", nullable: false),
                recipient = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                attempt = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "text", nullable: true),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                bounced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoice_deliveries", x => x.id);
                table.ForeignKey(
                    name: "FK_invoice_deliveries_invoices_invoice_id",
                    column: x => x.invoice_id,
                    principalTable: "invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "order_cancellations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                sales_order_id = table.Column<string>(type: "text", nullable: false),
                scope = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                outcome_json = table.Column<string>(type: "text", nullable: false),
                cancelled_by = table.Column<string>(type: "text", nullable: false),
                cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_cancellations", x => x.id);
                table.ForeignKey(
                    name: "FK_order_cancellations_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "order_holds",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                sales_order_id = table.Column<string>(type: "text", nullable: false),
                hold_type = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                set_by = table.Column<string>(type: "text", nullable: false),
                set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                cleared_by = table.Column<string>(type: "text", nullable: true),
                cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_holds", x => x.id);
                table.ForeignKey(
                    name: "FK_order_holds_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "party_merges",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                survivor_id = table.Column<string>(type: "text", nullable: false),
                merged_id = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                reassignments_json = table.Column<string>(type: "text", nullable: true),
                requested_by = table.Column<string>(type: "text", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                executed_by = table.Column<string>(type: "text", nullable: true),
                executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_party_merges", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "price_books",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                precedence = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_price_books", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "quotes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                terms = table.Column<string>(type: "text", nullable: true),
                expires_at = table.Column<DateOnly>(type: "date", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                current_revision = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quotes", x => x.id);
                table.ForeignKey(
                    name: "FK_quotes_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "rebate_agreements",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                basis = table.Column<string>(type: "text", nullable: false),
                rate_pct = table.Column<decimal>(type: "numeric", nullable: false),
                accrual_period = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rebate_agreements", x => x.id);
                table.ForeignKey(
                    name: "FK_rebate_agreements_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "receipt_candidates",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: true),
                bank_ref = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                value_date = table.Column<DateOnly>(type: "date", nullable: false),
                channel = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                payer_ref = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_receipt_candidates", x => x.id);
                table.ForeignKey(
                    name: "FK_receipt_candidates_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "recurring_schedules",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                frequency = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                next_run_date = table.Column<DateOnly>(type: "date", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_recurring_schedules", x => x.id);
                table.ForeignKey(
                    name: "FK_recurring_schedules_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "refund_requests",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                adjustment_id = table.Column<string>(type: "text", nullable: false),
                invoice_id = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                requested_by = table.Column<string>(type: "text", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refund_requests", x => x.id);
                table.ForeignKey(
                    name: "FK_refund_requests_invoices_invoice_id",
                    column: x => x.invoice_id,
                    principalTable: "invoices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_refund_requests_sales_adjustments_adjustment_id",
                    column: x => x.adjustment_id,
                    principalTable: "sales_adjustments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_returns",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                sales_order_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_returns", x => x.id);
                table.ForeignKey(
                    name: "FK_sales_returns_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_returns_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "so_amendments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                sales_order_id = table.Column<string>(type: "text", nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                rationale = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_so_amendments", x => x.id);
                table.ForeignKey(
                    name: "FK_so_amendments_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "price_book_entries",
            columns: table => new
            {
                book_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                item_sku = table.Column<string>(type: "text", nullable: false),
                min_qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                rule = table.Column<string>(type: "text", nullable: true),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_price_book_entries", x => new { x.book_id, x.line_no });
                table.ForeignKey(
                    name: "FK_price_book_entries_price_books_book_id",
                    column: x => x.book_id,
                    principalTable: "price_books",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "quote_revisions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                quote_id = table.Column<string>(type: "text", nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                sub_total = table.Column<decimal>(type: "numeric", nullable: false),
                discount_total = table.Column<decimal>(type: "numeric", nullable: false),
                tax_total = table.Column<decimal>(type: "numeric", nullable: false),
                total = table.Column<decimal>(type: "numeric", nullable: false),
                price_evidence_json = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quote_revisions", x => x.id);
                table.ForeignKey(
                    name: "FK_quote_revisions_quotes_quote_id",
                    column: x => x.quote_id,
                    principalTable: "quotes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "rebate_accruals",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                agreement_id = table.Column<string>(type: "text", nullable: false),
                period = table.Column<string>(type: "text", nullable: false),
                accrued_amount = table.Column<decimal>(type: "numeric", nullable: false),
                approved_amount = table.Column<decimal>(type: "numeric", nullable: true),
                eligible_txn_json = table.Column<string>(type: "text", nullable: false),
                agreement_version = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                calculated_by = table.Column<string>(type: "text", nullable: false),
                calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rebate_accruals", x => x.id);
                table.ForeignKey(
                    name: "FK_rebate_accruals_rebate_agreements_agreement_id",
                    column: x => x.agreement_id,
                    principalTable: "rebate_agreements",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "rebate_claims",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                agreement_id = table.Column<string>(type: "text", nullable: false),
                period = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                agreement_version = table.Column<int>(type: "integer", nullable: false),
                eligible_txn_json = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                requested_by = table.Column<string>(type: "text", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rebate_claims", x => x.id);
                table.ForeignKey(
                    name: "FK_rebate_claims_rebate_agreements_agreement_id",
                    column: x => x.agreement_id,
                    principalTable: "rebate_agreements",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "recurring_occurrences",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                schedule_id = table.Column<string>(type: "text", nullable: false),
                period_key = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                generated_by = table.Column<string>(type: "text", nullable: false),
                generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_recurring_occurrences", x => x.id);
                table.ForeignKey(
                    name: "FK_recurring_occurrences_recurring_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "recurring_schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_recurring_occurrences_sales_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "recurring_schedule_lines",
            columns: table => new
            {
                schedule_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                id = table.Column<string>(type: "text", nullable: true),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_recurring_schedule_lines", x => new { x.schedule_id, x.line_no });
                table.ForeignKey(
                    name: "FK_recurring_schedule_lines_recurring_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "recurring_schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "return_receipts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                return_id = table.Column<string>(type: "text", nullable: false),
                received_qty = table.Column<decimal>(type: "numeric", nullable: false),
                received_by = table.Column<string>(type: "text", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_return_receipts", x => x.id);
                table.ForeignKey(
                    name: "FK_return_receipts_sales_returns_return_id",
                    column: x => x.return_id,
                    principalTable: "sales_returns",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_return_lines",
            columns: table => new
            {
                return_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                condition = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_return_lines", x => new { x.return_id, x.line_no });
                table.ForeignKey(
                    name: "FK_sales_return_lines_sales_returns_return_id",
                    column: x => x.return_id,
                    principalTable: "sales_returns",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "so_amendment_lines",
            columns: table => new
            {
                amendment_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                id = table.Column<string>(type: "text", nullable: true),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_so_amendment_lines", x => new { x.amendment_id, x.line_no });
                table.ForeignKey(
                    name: "FK_so_amendment_lines_so_amendments_amendment_id",
                    column: x => x.amendment_id,
                    principalTable: "so_amendments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "quote_acceptances",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                quote_id = table.Column<string>(type: "text", nullable: false),
                revision_id = table.Column<string>(type: "text", nullable: false),
                method = table.Column<string>(type: "text", nullable: false),
                external_event_id = table.Column<string>(type: "text", nullable: true),
                evidence_ref = table.Column<string>(type: "text", nullable: true),
                accepted_by = table.Column<string>(type: "text", nullable: false),
                accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quote_acceptances", x => x.id);
                table.ForeignKey(
                    name: "FK_quote_acceptances_quote_revisions_revision_id",
                    column: x => x.revision_id,
                    principalTable: "quote_revisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "quote_approvals",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                revision_id = table.Column<string>(type: "text", nullable: false),
                approver_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quote_approvals", x => x.id);
                table.ForeignKey(
                    name: "FK_quote_approvals_quote_revisions_revision_id",
                    column: x => x.revision_id,
                    principalTable: "quote_revisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "quote_lines",
            columns: table => new
            {
                revision_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                discount_pct = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quote_lines", x => new { x.revision_id, x.line_no });
                table.ForeignKey(
                    name: "FK_quote_lines_quote_revisions_revision_id",
                    column: x => x.revision_id,
                    principalTable: "quote_revisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "finance_settings",
            columns: new[] { "id", "key", "legal_entity_id", "tenant_id", "value" },
            values: new object[,]
            {
                { "FS-discount-cap", "sales.discount_cap_pct", "LE-001", "T-001", "15" },
                { "FS-quotes-threshold", "quotes.approval_threshold", "LE-001", "T-001", "10000" },
                { "FS-stage-weights", "sales.stage_weights", "LE-001", "T-001", "{\"Prospecting\":0.1,\"Qualification\":0.2,\"Discovery\":0.3,\"Proposal\":0.5,\"Negotiation\":0.75,\"Closed\":0.9}" }
            });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            // union-merge end-state: carries the FULL union (base + SALES refunds/rebates/
            // forecast-read + PEOPLE leave/payroll-release/payroll-close/analytics + PROC
            // requisitions/review/analytics) — this migration runs after 072428 in the merged
            // chain, so a partial array here would clobber PROC's R-FIN additions
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write", "refunds:approve", "rebates:settle", "forecasts:read", "leave:adjust", "payroll:release", "payroll:close", "people:analytics:read", "requisitions:approve", "requisitions:read", "requisitions:override", "suppliers:review", "analytics:read" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-SALES",
            column: "permissions",
            value: new[] { "customers:write", "so:write", "shipments:write", "so:approve", "leads:read", "leads:write", "crm:read", "crm:write", "opps:read", "opps:write", "invoices:read", "invoices:write", "collections:read", "collections:write", "quotes:write", "quotes:approve", "pricebooks:write", "recurring:write", "forecasts:read", "forecasts:write", "returns:write", "receipts-candidates:write", "rebates:write" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-014", true, "Sales Manager", "sales-manager@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-014", "R-SALES" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_merged_into_id",
            table: "customers",
            column: "merged_into_id");

        migrationBuilder.CreateIndex(
            name: "IX_customers_parent_customer_id",
            table: "customers",
            column: "parent_customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_contract_prices_customer_id",
            table: "customer_contract_prices",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_contract_prices_tenant_id_legal_entity_id",
            table: "customer_contract_prices",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_contract_prices_tenant_id_legal_entity_id_customer~",
            table: "customer_contract_prices",
            columns: new[] { "tenant_id", "legal_entity_id", "customer_id", "item_sku" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_onboarding_checks_customer_id",
            table: "customer_onboarding_checks",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_onboarding_checks_tenant_id_legal_entity_id",
            table: "customer_onboarding_checks",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_einvoice_submissions_invoice_id",
            table: "einvoice_submissions",
            column: "invoice_id");

        migrationBuilder.CreateIndex(
            name: "IX_einvoice_submissions_tenant_id_legal_entity_id",
            table: "einvoice_submissions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_forecast_snapshots_tenant_id_legal_entity_id",
            table: "forecast_snapshots",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_forecast_snapshots_tenant_id_legal_entity_id_period",
            table: "forecast_snapshots",
            columns: new[] { "tenant_id", "legal_entity_id", "period" });

        migrationBuilder.CreateIndex(
            name: "IX_forecast_submissions_tenant_id_legal_entity_id",
            table: "forecast_submissions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_forecast_submissions_tenant_id_legal_entity_id_period",
            table: "forecast_submissions",
            columns: new[] { "tenant_id", "legal_entity_id", "period" });

        migrationBuilder.CreateIndex(
            name: "IX_invoice_deliveries_invoice_id",
            table: "invoice_deliveries",
            column: "invoice_id");

        migrationBuilder.CreateIndex(
            name: "IX_invoice_deliveries_tenant_id_legal_entity_id",
            table: "invoice_deliveries",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_invoice_deliveries_tenant_id_legal_entity_id_invoice_id_att~",
            table: "invoice_deliveries",
            columns: new[] { "tenant_id", "legal_entity_id", "invoice_id", "attempt" });

        migrationBuilder.CreateIndex(
            name: "IX_order_cancellations_sales_order_id",
            table: "order_cancellations",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_order_cancellations_tenant_id_legal_entity_id",
            table: "order_cancellations",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_order_holds_sales_order_id",
            table: "order_holds",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_order_holds_tenant_id_legal_entity_id",
            table: "order_holds",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_order_holds_tenant_id_legal_entity_id_sales_order_id_status",
            table: "order_holds",
            columns: new[] { "tenant_id", "legal_entity_id", "sales_order_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_party_merges_merged_id",
            table: "party_merges",
            column: "merged_id");

        migrationBuilder.CreateIndex(
            name: "IX_party_merges_survivor_id",
            table: "party_merges",
            column: "survivor_id");

        migrationBuilder.CreateIndex(
            name: "IX_party_merges_tenant_id_legal_entity_id",
            table: "party_merges",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_price_book_entries_book_id",
            table: "price_book_entries",
            column: "book_id");

        migrationBuilder.CreateIndex(
            name: "IX_price_book_entries_tenant_id_legal_entity_id",
            table: "price_book_entries",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_price_books_tenant_id_legal_entity_id",
            table: "price_books",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quote_acceptances_revision_id",
            table: "quote_acceptances",
            column: "revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_quote_acceptances_tenant_id_legal_entity_id",
            table: "quote_acceptances",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quote_acceptances_tenant_id_legal_entity_id_external_event_~",
            table: "quote_acceptances",
            columns: new[] { "tenant_id", "legal_entity_id", "external_event_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_quote_approvals_revision_id",
            table: "quote_approvals",
            column: "revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_quote_approvals_tenant_id_legal_entity_id",
            table: "quote_approvals",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quote_lines_revision_id",
            table: "quote_lines",
            column: "revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_quote_lines_tenant_id_legal_entity_id",
            table: "quote_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quote_revisions_quote_id",
            table: "quote_revisions",
            column: "quote_id");

        migrationBuilder.CreateIndex(
            name: "IX_quote_revisions_tenant_id_legal_entity_id",
            table: "quote_revisions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quotes_customer_id",
            table: "quotes",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_quotes_tenant_id_legal_entity_id",
            table: "quotes",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quotes_tenant_id_legal_entity_id_customer_id",
            table: "quotes",
            columns: new[] { "tenant_id", "legal_entity_id", "customer_id" });

        migrationBuilder.CreateIndex(
            name: "IX_rebate_accruals_agreement_id",
            table: "rebate_accruals",
            column: "agreement_id");

        migrationBuilder.CreateIndex(
            name: "IX_rebate_accruals_tenant_id_legal_entity_id",
            table: "rebate_accruals",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_rebate_agreements_customer_id",
            table: "rebate_agreements",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_rebate_agreements_tenant_id_legal_entity_id",
            table: "rebate_agreements",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_rebate_claims_agreement_id",
            table: "rebate_claims",
            column: "agreement_id");

        migrationBuilder.CreateIndex(
            name: "IX_rebate_claims_tenant_id_legal_entity_id",
            table: "rebate_claims",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_receipt_candidates_customer_id",
            table: "receipt_candidates",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_receipt_candidates_tenant_id_legal_entity_id",
            table: "receipt_candidates",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_receipt_candidates_tenant_id_legal_entity_id_status",
            table: "receipt_candidates",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_recurring_occurrences_order_id",
            table: "recurring_occurrences",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_recurring_occurrences_schedule_id",
            table: "recurring_occurrences",
            column: "schedule_id");

        migrationBuilder.CreateIndex(
            name: "IX_recurring_occurrences_tenant_id_legal_entity_id",
            table: "recurring_occurrences",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_recurring_occurrences_tenant_id_legal_entity_id_schedule_id~",
            table: "recurring_occurrences",
            columns: new[] { "tenant_id", "legal_entity_id", "schedule_id", "period_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_recurring_schedule_lines_schedule_id",
            table: "recurring_schedule_lines",
            column: "schedule_id");

        migrationBuilder.CreateIndex(
            name: "IX_recurring_schedule_lines_tenant_id_legal_entity_id",
            table: "recurring_schedule_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_recurring_schedules_customer_id",
            table: "recurring_schedules",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_recurring_schedules_tenant_id_legal_entity_id",
            table: "recurring_schedules",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_refund_requests_adjustment_id",
            table: "refund_requests",
            column: "adjustment_id");

        migrationBuilder.CreateIndex(
            name: "IX_refund_requests_invoice_id",
            table: "refund_requests",
            column: "invoice_id");

        migrationBuilder.CreateIndex(
            name: "IX_refund_requests_tenant_id_legal_entity_id",
            table: "refund_requests",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_return_receipts_return_id",
            table: "return_receipts",
            column: "return_id");

        migrationBuilder.CreateIndex(
            name: "IX_return_receipts_tenant_id_legal_entity_id",
            table: "return_receipts",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_return_lines_return_id",
            table: "sales_return_lines",
            column: "return_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_return_lines_tenant_id_legal_entity_id",
            table: "sales_return_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_returns_customer_id",
            table: "sales_returns",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_returns_sales_order_id",
            table: "sales_returns",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_returns_tenant_id_legal_entity_id",
            table: "sales_returns",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_so_amendment_lines_amendment_id",
            table: "so_amendment_lines",
            column: "amendment_id");

        migrationBuilder.CreateIndex(
            name: "IX_so_amendment_lines_tenant_id_legal_entity_id",
            table: "so_amendment_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_so_amendments_sales_order_id",
            table: "so_amendments",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_so_amendments_tenant_id_legal_entity_id",
            table: "so_amendments",
            columns: new[] { "tenant_id", "legal_entity_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "customer_contract_prices");

        migrationBuilder.DropTable(
            name: "customer_onboarding_checks");

        migrationBuilder.DropTable(
            name: "einvoice_submissions");

        migrationBuilder.DropTable(
            name: "forecast_snapshots");

        migrationBuilder.DropTable(
            name: "forecast_submissions");

        migrationBuilder.DropTable(
            name: "invoice_deliveries");

        migrationBuilder.DropTable(
            name: "order_cancellations");

        migrationBuilder.DropTable(
            name: "order_holds");

        migrationBuilder.DropTable(
            name: "party_merges");

        migrationBuilder.DropTable(
            name: "price_book_entries");

        migrationBuilder.DropTable(
            name: "quote_acceptances");

        migrationBuilder.DropTable(
            name: "quote_approvals");

        migrationBuilder.DropTable(
            name: "quote_lines");

        migrationBuilder.DropTable(
            name: "rebate_accruals");

        migrationBuilder.DropTable(
            name: "rebate_claims");

        migrationBuilder.DropTable(
            name: "receipt_candidates");

        migrationBuilder.DropTable(
            name: "recurring_occurrences");

        migrationBuilder.DropTable(
            name: "recurring_schedule_lines");

        migrationBuilder.DropTable(
            name: "refund_requests");

        migrationBuilder.DropTable(
            name: "return_receipts");

        migrationBuilder.DropTable(
            name: "sales_return_lines");

        migrationBuilder.DropTable(
            name: "so_amendment_lines");

        migrationBuilder.DropTable(
            name: "price_books");

        migrationBuilder.DropTable(
            name: "quote_revisions");

        migrationBuilder.DropTable(
            name: "rebate_agreements");

        migrationBuilder.DropTable(
            name: "recurring_schedules");

        migrationBuilder.DropTable(
            name: "sales_returns");

        migrationBuilder.DropTable(
            name: "so_amendments");

        migrationBuilder.DropTable(
            name: "quotes");

        migrationBuilder.DropIndex(
            name: "IX_customers_merged_into_id",
            table: "customers");

        migrationBuilder.DropIndex(
            name: "IX_customers_parent_customer_id",
            table: "customers");

        migrationBuilder.DeleteData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-discount-cap");

        migrationBuilder.DeleteData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-quotes-threshold");

        migrationBuilder.DeleteData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-stage-weights");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-014" });

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-014");

        migrationBuilder.DropColumn(
            name: "expected_date",
            table: "so_lines");

        migrationBuilder.DropColumn(
            name: "source_quote_revision_id",
            table: "sales_orders");

        migrationBuilder.DropColumn(
            name: "merged_into_id",
            table: "customers");

        migrationBuilder.DropColumn(
            name: "parent_customer_id",
            table: "customers");

        migrationBuilder.DropSequence(
            name: "doc_dlv");

        migrationBuilder.DropSequence(
            name: "doc_pbl");

        migrationBuilder.DropSequence(
            name: "doc_qte");

        migrationBuilder.DropSequence(
            name: "doc_rbt");

        migrationBuilder.DropSequence(
            name: "doc_rcp");

        migrationBuilder.DropSequence(
            name: "doc_rcs");

        migrationBuilder.DropSequence(
            name: "doc_rma");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-SALES",
            column: "permissions",
            value: new[] { "customers:write", "so:write", "shipments:write", "so:approve", "leads:read", "leads:write", "crm:read", "crm:write", "opps:read", "opps:write", "invoices:read", "invoices:write", "collections:read", "collections:write" });
    }
}
