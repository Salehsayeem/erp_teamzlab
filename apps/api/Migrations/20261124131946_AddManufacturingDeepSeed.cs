using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124131946_AddManufacturingDeepSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_bom");

        migrationBuilder.CreateSequence(
            name: "doc_cpl");

        migrationBuilder.CreateSequence(
            name: "doc_iss");

        migrationBuilder.CreateSequence(
            name: "doc_mfo");

        migrationBuilder.CreateSequence(
            name: "doc_scr");

        migrationBuilder.CreateTable(
            name: "bom_revisions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                output_sku = table.Column<string>(type: "text", nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                output_qty_per = table.Column<decimal>(type: "numeric", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bom_revisions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "bom_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                bom_revision_id = table.Column<string>(type: "text", nullable: false),
                component_sku = table.Column<string>(type: "text", nullable: false),
                qty_per = table.Column<decimal>(type: "numeric", nullable: false),
                unit = table.Column<string>(type: "text", nullable: false),
                issue_policy = table.Column<int>(type: "integer", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bom_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_bom_lines_bom_revisions_bom_revision_id",
                    column: x => x.bom_revision_id,
                    principalTable: "bom_revisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "production_orders",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_no = table.Column<string>(type: "text", nullable: false),
                output_sku = table.Column<string>(type: "text", nullable: false),
                unit = table.Column<string>(type: "text", nullable: false),
                demanded_qty = table.Column<decimal>(type: "numeric", nullable: false),
                planned_qty = table.Column<decimal>(type: "numeric", nullable: false),
                released_qty = table.Column<decimal>(type: "numeric", nullable: false),
                completed_qty = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                over_tolerance_pct = table.Column<decimal>(type: "numeric", nullable: false),
                planned_bom_revision_id = table.Column<string>(type: "text", nullable: true),
                released_bom_revision_id = table.Column<string>(type: "text", nullable: true),
                cancel_reason = table.Column<string>(type: "text", nullable: true),
                closed_by = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reopened_by = table.Column<string>(type: "text", nullable: true),
                reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_production_orders", x => x.id);
                table.ForeignKey(
                    name: "FK_production_orders_bom_revisions_planned_bom_revision_id",
                    column: x => x.planned_bom_revision_id,
                    principalTable: "bom_revisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_production_orders_bom_revisions_released_bom_revision_id",
                    column: x => x.released_bom_revision_id,
                    principalTable: "bom_revisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "completion_reports",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                note = table.Column<string>(type: "text", nullable: true),
                journal_id = table.Column<string>(type: "text", nullable: true),
                reported_by = table.Column<string>(type: "text", nullable: false),
                reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_completion_reports", x => x.id);
                table.ForeignKey(
                    name: "FK_completion_reports_journals_journal_id",
                    column: x => x.journal_id,
                    principalTable: "journals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_completion_reports_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "material_issues",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                component_sku = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric", nullable: false),
                line_value = table.Column<decimal>(type: "numeric", nullable: false),
                linked_issue_id = table.Column<string>(type: "text", nullable: true),
                journal_id = table.Column<string>(type: "text", nullable: true),
                reported_by = table.Column<string>(type: "text", nullable: false),
                reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                linked_completion_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_material_issues", x => x.id);
                table.ForeignKey(
                    name: "FK_material_issues_journals_journal_id",
                    column: x => x.journal_id,
                    principalTable: "journals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_material_issues_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "order_amendments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                field = table.Column<string>(type: "text", nullable: false),
                old_value = table.Column<string>(type: "text", nullable: false),
                new_value = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                changed_by = table.Column<string>(type: "text", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_amendments", x => x.id);
                table.ForeignKey(
                    name: "FK_order_amendments_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "scrap_reports",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric", nullable: false),
                reason_code = table.Column<string>(type: "text", nullable: false),
                disposition = table.Column<int>(type: "integer", nullable: false),
                journal_id = table.Column<string>(type: "text", nullable: true),
                reported_by = table.Column<string>(type: "text", nullable: false),
                reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scrap_reports", x => x.id);
                table.ForeignKey(
                    name: "FK_scrap_reports_journals_journal_id",
                    column: x => x.journal_id,
                    principalTable: "journals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_scrap_reports_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "legal_entity_id", "name", "permissions", "tenant_id" },
            values: new object[] { "R-MFG", "LE-001", "manufacturing_officer", new[] { "boms:read", "boms:write", "boms:approve", "mfg:read", "mfg:write", "mfg:release", "mfg:override", "mfg:close", "mfg:reopen" }, "T-001" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-008", true, "Manufacturing Officer", "mfg@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-008", "R-MFG" });

        // MFG chart-of-accounts additions (brief: 1420 + 5200) — attribute-carried like the
        // task-5 CoA seed (accounts are not model HasData). Both debit-normal, direct-postable;
        // 1400 Inventory is REUSED unchanged; 5100 COGS stays sales-owned.
        migrationBuilder.InsertData(
            table: "accounts",
            columns: new[] { "id", "tenant_id", "legal_entity_id", "code", "name", "type", "normal_balance", "direct_posting_allowed", "version", "created_by", "created_at" },
            values: new object[,]
            {
                { "ACC-1420", "T-001", "LE-001", "1420", "Work in Process Inventory", 0, 0, true, 1, "system", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                { "ACC-5200", "T-001", "LE-001", "5200", "Production Scrap and Yield Loss", 4, 0, true, 1, "system", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            });

        migrationBuilder.CreateIndex(
            name: "IX_bom_lines_bom_revision_id",
            table: "bom_lines",
            column: "bom_revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_bom_lines_tenant_id_legal_entity_id",
            table: "bom_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_bom_revisions_tenant_id_legal_entity_id",
            table: "bom_revisions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_bom_revisions_tenant_id_legal_entity_id_output_sku",
            table: "bom_revisions",
            columns: new[] { "tenant_id", "legal_entity_id", "output_sku" });

        migrationBuilder.CreateIndex(
            name: "IX_bom_revisions_tenant_id_legal_entity_id_output_sku_revision",
            table: "bom_revisions",
            columns: new[] { "tenant_id", "legal_entity_id", "output_sku", "revision" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_bom_revisions_tenant_id_legal_entity_id_status",
            table: "bom_revisions",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_completion_reports_journal_id",
            table: "completion_reports",
            column: "journal_id");

        migrationBuilder.CreateIndex(
            name: "IX_completion_reports_order_id",
            table: "completion_reports",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_completion_reports_tenant_id_legal_entity_id",
            table: "completion_reports",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_completion_reports_tenant_id_legal_entity_id_order_id",
            table: "completion_reports",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_material_issues_journal_id",
            table: "material_issues",
            column: "journal_id");

        migrationBuilder.CreateIndex(
            name: "IX_material_issues_order_id",
            table: "material_issues",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_material_issues_tenant_id_legal_entity_id",
            table: "material_issues",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_material_issues_tenant_id_legal_entity_id_component_sku",
            table: "material_issues",
            columns: new[] { "tenant_id", "legal_entity_id", "component_sku" });

        migrationBuilder.CreateIndex(
            name: "IX_material_issues_tenant_id_legal_entity_id_linked_issue_id",
            table: "material_issues",
            columns: new[] { "tenant_id", "legal_entity_id", "linked_issue_id" });

        migrationBuilder.CreateIndex(
            name: "IX_material_issues_tenant_id_legal_entity_id_order_id",
            table: "material_issues",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_order_amendments_order_id",
            table: "order_amendments",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_order_amendments_tenant_id_legal_entity_id",
            table: "order_amendments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_planned_bom_revision_id",
            table: "production_orders",
            column: "planned_bom_revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_released_bom_revision_id",
            table: "production_orders",
            column: "released_bom_revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id",
            table: "production_orders",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id_order_no",
            table: "production_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "order_no" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id_output_sku",
            table: "production_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "output_sku" });

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id_status",
            table: "production_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_scrap_reports_journal_id",
            table: "scrap_reports",
            column: "journal_id");

        migrationBuilder.CreateIndex(
            name: "IX_scrap_reports_order_id",
            table: "scrap_reports",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_scrap_reports_tenant_id_legal_entity_id",
            table: "scrap_reports",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_scrap_reports_tenant_id_legal_entity_id_order_id",
            table: "scrap_reports",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "bom_lines");

        migrationBuilder.DropTable(
            name: "completion_reports");

        migrationBuilder.DropTable(
            name: "material_issues");

        migrationBuilder.DropTable(
            name: "order_amendments");

        migrationBuilder.DropTable(
            name: "scrap_reports");

        migrationBuilder.DropTable(
            name: "production_orders");

        migrationBuilder.DropTable(
            name: "bom_revisions");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-008" });

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-MFG");

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-008");

        migrationBuilder.DeleteData(
            table: "accounts",
            keyColumn: "id",
            keyValue: "ACC-1420");

        migrationBuilder.DeleteData(
            table: "accounts",
            keyColumn: "id",
            keyValue: "ACC-5200");

        migrationBuilder.DropSequence(
            name: "doc_bom");

        migrationBuilder.DropSequence(
            name: "doc_cpl");

        migrationBuilder.DropSequence(
            name: "doc_iss");

        migrationBuilder.DropSequence(
            name: "doc_mfo");

        migrationBuilder.DropSequence(
            name: "doc_scr");
    }
}
