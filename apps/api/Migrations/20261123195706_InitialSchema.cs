using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261123195706_InitialSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_a",
            startValue: 2L);

        migrationBuilder.CreateSequence(
            name: "doc_adj");

        migrationBuilder.CreateSequence(
            name: "doc_cnt");

        migrationBuilder.CreateSequence(
            name: "doc_cus");

        migrationBuilder.CreateSequence(
            name: "doc_e");

        migrationBuilder.CreateSequence(
            name: "doc_j");

        migrationBuilder.CreateSequence(
            name: "doc_loc");

        migrationBuilder.CreateSequence(
            name: "doc_po");

        migrationBuilder.CreateSequence(
            name: "doc_rc");

        migrationBuilder.CreateSequence(
            name: "doc_shp");

        migrationBuilder.CreateSequence(
            name: "doc_so");

        migrationBuilder.CreateSequence(
            name: "doc_sup");

        migrationBuilder.CreateSequence(
            name: "doc_trf");

        migrationBuilder.CreateSequence(
            name: "doc_wh");

        migrationBuilder.CreateTable(
            name: "audit_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                actor_user_id = table.Column<string>(type: "text", nullable: false),
                action = table.Column<string>(type: "text", nullable: false),
                entity_type = table.Column<string>(type: "text", nullable: false),
                entity_id = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                summary = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_events", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "customers",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                tax_id = table.Column<string>(type: "text", nullable: false),
                email = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customers", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "idempotency",
            columns: table => new
            {
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                key = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                body = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency", x => new { x.tenant_id, x.legal_entity_id, x.key });
            });

        migrationBuilder.CreateTable(
            name: "journals",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                memo = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                reversal_of_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_journals", x => x.id);
                table.ForeignKey(
                    name: "FK_journals_journals_reversal_of_id",
                    column: x => x.reversal_of_id,
                    principalTable: "journals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                actor_user_id = table.Column<string>(type: "text", nullable: false),
                payload_json = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "roles",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                permissions = table.Column<string[]>(type: "text[]", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "stock_levels",
            columns: table => new
            {
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                warehouse_id = table.Column<string>(type: "text", nullable: false),
                location_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty_on_hand = table.Column<decimal>(type: "numeric", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_levels", x => new { x.tenant_id, x.legal_entity_id, x.warehouse_id, x.location_id, x.item_sku });
            });

        migrationBuilder.CreateTable(
            name: "suppliers",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                tax_id = table.Column<string>(type: "text", nullable: false),
                email = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_suppliers", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                email = table.Column<string>(type: "text", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "warehouses",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_warehouses", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "sales_orders",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_orders", x => x.id);
                table.ForeignKey(
                    name: "FK_sales_orders_customers_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "journal_lines",
            columns: table => new
            {
                journal_id = table.Column<string>(type: "text", nullable: false),
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                account_code = table.Column<string>(type: "text", nullable: false),
                account_name = table.Column<string>(type: "text", nullable: false),
                debit = table.Column<decimal>(type: "numeric", nullable: false),
                credit = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_journal_lines", x => new { x.journal_id, x.id });
                table.ForeignKey(
                    name: "FK_journal_lines_journals_journal_id",
                    column: x => x.journal_id,
                    principalTable: "journals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "purchase_orders",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_orders", x => x.id);
                table.ForeignKey(
                    name: "FK_purchase_orders_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "memberships",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                role_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_memberships", x => new { x.user_id, x.tenant_id, x.legal_entity_id });
                table.ForeignKey(
                    name: "FK_memberships_roles_role_id",
                    column: x => x.role_id,
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_memberships_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "locations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                warehouse_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_locations", x => x.id);
                table.ForeignKey(
                    name: "FK_locations_warehouses_warehouse_id",
                    column: x => x.warehouse_id,
                    principalTable: "warehouses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "shipments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                sales_order_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                shipped_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_shipments", x => x.id);
                table.ForeignKey(
                    name: "FK_shipments_sales_orders_sales_order_id",
                    column: x => x.sales_order_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "so_lines",
            columns: table => new
            {
                so_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                id = table.Column<string>(type: "text", nullable: true),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_so_lines", x => new { x.so_id, x.line_no });
                table.ForeignKey(
                    name: "FK_so_lines_sales_orders_so_id",
                    column: x => x.so_id,
                    principalTable: "sales_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "po_lines",
            columns: table => new
            {
                po_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                id = table.Column<string>(type: "text", nullable: true),
                item_sku = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_po_lines", x => new { x.po_id, x.line_no });
                table.ForeignKey(
                    name: "FK_po_lines_purchase_orders_po_id",
                    column: x => x.po_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "receipts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                purchase_order_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                received_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_receipts", x => x.id);
                table.ForeignKey(
                    name: "FK_receipts_purchase_orders_purchase_order_id",
                    column: x => x.purchase_order_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "counts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                location_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                closed_by = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_counts", x => x.id);
                table.ForeignKey(
                    name: "FK_counts_locations_location_id",
                    column: x => x.location_id,
                    principalTable: "locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "transfers",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                from_location_id = table.Column<string>(type: "text", nullable: false),
                to_location_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transfers", x => x.id);
                table.ForeignKey(
                    name: "FK_transfers_locations_from_location_id",
                    column: x => x.from_location_id,
                    principalTable: "locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_transfers_locations_to_location_id",
                    column: x => x.to_location_id,
                    principalTable: "locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "adjustments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                count_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                location_id = table.Column<string>(type: "text", nullable: false),
                qty_variance = table.Column<decimal>(type: "numeric", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                journal_id = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_adjustments", x => x.id);
                table.ForeignKey(
                    name: "FK_adjustments_counts_count_id",
                    column: x => x.count_id,
                    principalTable: "counts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_adjustments_journals_journal_id",
                    column: x => x.journal_id,
                    principalTable: "journals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "count_lines",
            columns: table => new
            {
                count_id = table.Column<string>(type: "text", nullable: false),
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                item_sku = table.Column<string>(type: "text", nullable: false),
                counted_qty = table.Column<decimal>(type: "numeric", nullable: false),
                system_qty = table.Column<decimal>(type: "numeric", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_count_lines", x => new { x.count_id, x.id });
                table.ForeignKey(
                    name: "FK_count_lines_counts_count_id",
                    column: x => x.count_id,
                    principalTable: "counts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "audit_events",
            columns: new[] { "id", "action", "actor_user_id", "at", "entity_id", "entity_type", "legal_entity_id", "summary", "tenant_id" },
            values: new object[] { "A-0001", "seed", "system", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "T-001", "tenant", "LE-001", "Seeded demo tenant, entity, users, roles", "T-001" });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "legal_entity_id", "name", "permissions", "tenant_id" },
            values: new object[,]
            {
                { "R-ADMIN", "LE-001", "tenant_admin", new[] { "*" }, "T-001" },
                { "R-FIN", "LE-001", "finance_officer", new[] { "journals:read", "journals:approve", "ap:read", "po:approve" }, "T-001" },
                { "R-PROC", "LE-001", "procurement_officer", new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer" }, "T-001" },
                { "R-SALES", "LE-001", "sales_officer", new[] { "customers:write", "so:write", "shipments:write" }, "T-001" }
            });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[,]
            {
                { "U-001", true, "Tenant Admin", "admin@teamzlab.dev", "LE-001", "T-001" },
                { "U-002", true, "Procure Officer", "procurve@teamzlab.dev", "LE-001", "T-001" },
                { "U-003", true, "Finance Officer", "finance@teamzlab.dev", "LE-001", "T-001" },
                { "U-004", true, "Sales Officer", "sales@teamzlab.dev", "LE-001", "T-001" }
            });

        migrationBuilder.InsertData(
            table: "warehouses",
            columns: new[] { "id", "code", "legal_entity_id", "name", "tenant_id" },
            values: new object[] { "WH-001", "WH-001", "LE-001", "Main Warehouse", "T-001" });

        migrationBuilder.InsertData(
            table: "locations",
            columns: new[] { "id", "code", "legal_entity_id", "name", "tenant_id", "warehouse_id" },
            values: new object[,]
            {
                { "LOC-A1", "A1", "LE-001", "Rack A1", "T-001", "WH-001" },
                { "LOC-B1", "B1", "LE-001", "Rack B1", "T-001", "WH-001" }
            });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[,]
            {
                { "LE-001", "T-001", "U-001", "R-ADMIN" },
                { "LE-001", "T-001", "U-002", "R-PROC" },
                { "LE-001", "T-001", "U-003", "R-FIN" },
                { "LE-001", "T-001", "U-004", "R-SALES" }
            });

        migrationBuilder.CreateIndex(
            name: "IX_adjustments_count_id",
            table: "adjustments",
            column: "count_id");

        migrationBuilder.CreateIndex(
            name: "IX_adjustments_journal_id",
            table: "adjustments",
            column: "journal_id");

        migrationBuilder.CreateIndex(
            name: "IX_adjustments_tenant_id_legal_entity_id",
            table: "adjustments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_tenant_id_legal_entity_id",
            table: "audit_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_count_lines_count_id",
            table: "count_lines",
            column: "count_id");

        migrationBuilder.CreateIndex(
            name: "IX_count_lines_tenant_id_legal_entity_id",
            table: "count_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_counts_location_id",
            table: "counts",
            column: "location_id");

        migrationBuilder.CreateIndex(
            name: "IX_counts_tenant_id_legal_entity_id",
            table: "counts",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_tenant_id_legal_entity_id",
            table: "customers",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_journal_lines_journal_id",
            table: "journal_lines",
            column: "journal_id");

        migrationBuilder.CreateIndex(
            name: "IX_journal_lines_tenant_id_legal_entity_id",
            table: "journal_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_journals_reversal_of_id",
            table: "journals",
            column: "reversal_of_id");

        migrationBuilder.CreateIndex(
            name: "IX_journals_tenant_id_legal_entity_id",
            table: "journals",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_locations_tenant_id_legal_entity_id",
            table: "locations",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_locations_tenant_id_legal_entity_id_warehouse_id_code",
            table: "locations",
            columns: new[] { "tenant_id", "legal_entity_id", "warehouse_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_locations_warehouse_id",
            table: "locations",
            column: "warehouse_id");

        migrationBuilder.CreateIndex(
            name: "IX_memberships_role_id",
            table: "memberships",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_tenant_id_legal_entity_id",
            table: "outbox_messages",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_po_lines_po_id",
            table: "po_lines",
            column: "po_id");

        migrationBuilder.CreateIndex(
            name: "IX_po_lines_tenant_id_legal_entity_id",
            table: "po_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_purchase_orders_supplier_id",
            table: "purchase_orders",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_purchase_orders_tenant_id_legal_entity_id",
            table: "purchase_orders",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_receipts_purchase_order_id",
            table: "receipts",
            column: "purchase_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_receipts_tenant_id_legal_entity_id",
            table: "receipts",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_receipts_tenant_id_legal_entity_id_purchase_order_id_item_s~",
            table: "receipts",
            columns: new[] { "tenant_id", "legal_entity_id", "purchase_order_id", "item_sku" });

        migrationBuilder.CreateIndex(
            name: "IX_roles_tenant_id_legal_entity_id",
            table: "roles",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_orders_customer_id",
            table: "sales_orders",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_orders_tenant_id_legal_entity_id",
            table: "sales_orders",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_shipments_sales_order_id",
            table: "shipments",
            column: "sales_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_shipments_tenant_id_legal_entity_id",
            table: "shipments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_so_lines_so_id",
            table: "so_lines",
            column: "so_id");

        migrationBuilder.CreateIndex(
            name: "IX_so_lines_tenant_id_legal_entity_id",
            table: "so_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_stock_levels_tenant_id_legal_entity_id_item_sku",
            table: "stock_levels",
            columns: new[] { "tenant_id", "legal_entity_id", "item_sku" });

        migrationBuilder.CreateIndex(
            name: "IX_stock_levels_tenant_id_legal_entity_id_location_id",
            table: "stock_levels",
            columns: new[] { "tenant_id", "legal_entity_id", "location_id" });

        migrationBuilder.CreateIndex(
            name: "IX_suppliers_tenant_id_legal_entity_id",
            table: "suppliers",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_transfers_from_location_id",
            table: "transfers",
            column: "from_location_id");

        migrationBuilder.CreateIndex(
            name: "IX_transfers_tenant_id_legal_entity_id",
            table: "transfers",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_transfers_to_location_id",
            table: "transfers",
            column: "to_location_id");

        migrationBuilder.CreateIndex(
            name: "IX_users_email",
            table: "users",
            column: "email");

        migrationBuilder.CreateIndex(
            name: "IX_users_tenant_id_legal_entity_id",
            table: "users",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_warehouses_tenant_id_legal_entity_id",
            table: "warehouses",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_warehouses_tenant_id_legal_entity_id_code",
            table: "warehouses",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "adjustments");

        migrationBuilder.DropTable(
            name: "audit_events");

        migrationBuilder.DropTable(
            name: "count_lines");

        migrationBuilder.DropTable(
            name: "idempotency");

        migrationBuilder.DropTable(
            name: "journal_lines");

        migrationBuilder.DropTable(
            name: "memberships");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "po_lines");

        migrationBuilder.DropTable(
            name: "receipts");

        migrationBuilder.DropTable(
            name: "shipments");

        migrationBuilder.DropTable(
            name: "so_lines");

        migrationBuilder.DropTable(
            name: "stock_levels");

        migrationBuilder.DropTable(
            name: "transfers");

        migrationBuilder.DropTable(
            name: "counts");

        migrationBuilder.DropTable(
            name: "journals");

        migrationBuilder.DropTable(
            name: "roles");

        migrationBuilder.DropTable(
            name: "users");

        migrationBuilder.DropTable(
            name: "purchase_orders");

        migrationBuilder.DropTable(
            name: "sales_orders");

        migrationBuilder.DropTable(
            name: "locations");

        migrationBuilder.DropTable(
            name: "suppliers");

        migrationBuilder.DropTable(
            name: "customers");

        migrationBuilder.DropTable(
            name: "warehouses");

        migrationBuilder.DropSequence(
            name: "doc_a");

        migrationBuilder.DropSequence(
            name: "doc_adj");

        migrationBuilder.DropSequence(
            name: "doc_cnt");

        migrationBuilder.DropSequence(
            name: "doc_cus");

        migrationBuilder.DropSequence(
            name: "doc_e");

        migrationBuilder.DropSequence(
            name: "doc_j");

        migrationBuilder.DropSequence(
            name: "doc_loc");

        migrationBuilder.DropSequence(
            name: "doc_po");

        migrationBuilder.DropSequence(
            name: "doc_rc");

        migrationBuilder.DropSequence(
            name: "doc_shp");

        migrationBuilder.DropSequence(
            name: "doc_so");

        migrationBuilder.DropSequence(
            name: "doc_sup");

        migrationBuilder.DropSequence(
            name: "doc_trf");

        migrationBuilder.DropSequence(
            name: "doc_wh");
    }
}
