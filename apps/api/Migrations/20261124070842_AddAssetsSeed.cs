using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124070842_AddAssetsSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_case");

        migrationBuilder.CreateSequence(
            name: "doc_dep");

        migrationBuilder.CreateSequence(
            name: "doc_fa");

        migrationBuilder.CreateSequence(
            name: "doc_fae");

        migrationBuilder.CreateSequence(
            name: "doc_prj");

        migrationBuilder.CreateSequence(
            name: "doc_tsk");

        migrationBuilder.CreateTable(
            name: "depreciation_runs",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                period_id = table.Column<string>(type: "text", nullable: false),
                total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                journal_id = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_depreciation_runs", x => x.id);
                table.ForeignKey(
                    name: "FK_depreciation_runs_journals_journal_id",
                    column: x => x.journal_id,
                    principalTable: "journals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "fixed_assets",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                tag = table.Column<string>(type: "text", nullable: false),
                serial = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                category = table.Column<string>(type: "text", nullable: false),
                cost = table.Column<decimal>(type: "numeric", nullable: false),
                accumulated_depreciation = table.Column<decimal>(type: "numeric", nullable: false),
                location_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                source_project_id = table.Column<string>(type: "text", nullable: true),
                in_service_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fixed_assets", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "projects",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                budget = table.Column<decimal>(type: "numeric", nullable: false),
                manager_id = table.Column<string>(type: "text", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                customer_id = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_projects", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "service_cases",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                channel = table.Column<string>(type: "text", nullable: false),
                requester_id = table.Column<string>(type: "text", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                assigned_to = table.Column<string>(type: "text", nullable: true),
                due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                resolution_code = table.Column<string>(type: "text", nullable: true),
                breach_state = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_service_cases", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "asset_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                asset_id = table.Column<string>(type: "text", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                journal_id = table.Column<string>(type: "text", nullable: false),
                actor_id = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                from_location_id = table.Column<string>(type: "text", nullable: true),
                to_location_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_asset_events", x => x.id);
                table.ForeignKey(
                    name: "FK_asset_events_fixed_assets_asset_id",
                    column: x => x.asset_id,
                    principalTable: "fixed_assets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "project_tasks",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                project_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                hours_estimate = table.Column<decimal>(type: "numeric", nullable: false),
                hours_actual = table.Column<decimal>(type: "numeric", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_tasks", x => x.id);
                table.ForeignKey(
                    name: "FK_project_tasks_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "ap:read", "po:approve", "periods:read" });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "legal_entity_id", "name", "permissions", "tenant_id" },
            values: new object[] { "R-ASSETS", "LE-001", "assets_officer", new[] { "assets:read", "assets:write", "assets:approve", "projects:write", "projects:approve", "cases:write", "cases:read" }, "T-001" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-005", true, "Assets Officer", "assets@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-005", "R-ASSETS" });

        migrationBuilder.CreateIndex(
            name: "IX_asset_events_asset_id",
            table: "asset_events",
            column: "asset_id");

        migrationBuilder.CreateIndex(
            name: "IX_asset_events_asset_id_event_type",
            table: "asset_events",
            columns: new[] { "asset_id", "event_type" });

        migrationBuilder.CreateIndex(
            name: "IX_asset_events_tenant_id_legal_entity_id",
            table: "asset_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_depreciation_runs_journal_id",
            table: "depreciation_runs",
            column: "journal_id");

        migrationBuilder.CreateIndex(
            name: "IX_depreciation_runs_tenant_id_legal_entity_id",
            table: "depreciation_runs",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_depreciation_runs_tenant_id_legal_entity_id_period_id",
            table: "depreciation_runs",
            columns: new[] { "tenant_id", "legal_entity_id", "period_id" });

        migrationBuilder.CreateIndex(
            name: "IX_fixed_assets_location_id",
            table: "fixed_assets",
            column: "location_id");

        migrationBuilder.CreateIndex(
            name: "IX_fixed_assets_tenant_id_legal_entity_id",
            table: "fixed_assets",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_fixed_assets_tenant_id_legal_entity_id_serial",
            table: "fixed_assets",
            columns: new[] { "tenant_id", "legal_entity_id", "serial" });

        migrationBuilder.CreateIndex(
            name: "IX_fixed_assets_tenant_id_legal_entity_id_status",
            table: "fixed_assets",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_fixed_assets_tenant_id_legal_entity_id_tag",
            table: "fixed_assets",
            columns: new[] { "tenant_id", "legal_entity_id", "tag" });

        migrationBuilder.CreateIndex(
            name: "IX_project_tasks_project_id",
            table: "project_tasks",
            column: "project_id");

        migrationBuilder.CreateIndex(
            name: "IX_project_tasks_tenant_id_legal_entity_id",
            table: "project_tasks",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_projects_manager_id",
            table: "projects",
            column: "manager_id");

        migrationBuilder.CreateIndex(
            name: "IX_projects_tenant_id_legal_entity_id",
            table: "projects",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_service_cases_requester_id",
            table: "service_cases",
            column: "requester_id");

        migrationBuilder.CreateIndex(
            name: "IX_service_cases_tenant_id_legal_entity_id",
            table: "service_cases",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_service_cases_tenant_id_legal_entity_id_due_at",
            table: "service_cases",
            columns: new[] { "tenant_id", "legal_entity_id", "due_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "asset_events");

        migrationBuilder.DropTable(
            name: "depreciation_runs");

        migrationBuilder.DropTable(
            name: "project_tasks");

        migrationBuilder.DropTable(
            name: "service_cases");

        migrationBuilder.DropTable(
            name: "fixed_assets");

        migrationBuilder.DropTable(
            name: "projects");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-005" });

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-ASSETS");

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-005");

        migrationBuilder.DropSequence(
            name: "doc_case");

        migrationBuilder.DropSequence(
            name: "doc_dep");

        migrationBuilder.DropSequence(
            name: "doc_fa");

        migrationBuilder.DropSequence(
            name: "doc_fae");

        migrationBuilder.DropSequence(
            name: "doc_prj");

        migrationBuilder.DropSequence(
            name: "doc_tsk");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "ap:read", "po:approve" });
    }
}
