using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124132602_AddPeopleDeepSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_cmp");

        migrationBuilder.CreateSequence(
            name: "doc_per");

        migrationBuilder.CreateSequence(
            name: "doc_prn");

        migrationBuilder.CreateSequence(
            name: "doc_tmc");

        migrationBuilder.CreateSequence(
            name: "doc_tme");

        migrationBuilder.CreateSequence(
            name: "doc_wkr");

        migrationBuilder.CreateTable(
            name: "earning_deduction_codes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                calc_basis = table.Column<string>(type: "text", nullable: false),
                rate_pct = table.Column<decimal>(type: "numeric", nullable: true),
                debit_account = table.Column<string>(type: "text", nullable: false),
                credit_account = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_earning_deduction_codes", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "employments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                full_name = table.Column<string>(type: "text", nullable: false),
                preferred_name = table.Column<string>(type: "text", nullable: true),
                national_id = table.Column<string>(type: "text", nullable: true),
                email = table.Column<string>(type: "text", nullable: true),
                phone = table.Column<string>(type: "text", nullable: true),
                worker_no = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                category_code = table.Column<string>(type: "text", nullable: false),
                manager_user_id = table.Column<string>(type: "text", nullable: true),
                location_id = table.Column<string>(type: "text", nullable: true),
                weekly_planned_hours = table.Column<decimal>(type: "numeric", nullable: false),
                hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                termination_reason = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_employments", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "pay_groups",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                frequency = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pay_groups", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "compensation_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                pay_basis = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                reason = table.Column<string>(type: "text", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_compensation_events", x => x.id);
                table.ForeignKey(
                    name: "FK_compensation_events_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "time_entries",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                work_date = table.Column<DateOnly>(type: "date", nullable: false),
                hours = table.Column<decimal>(type: "numeric", nullable: false),
                overtime_hours = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                idem_key = table.Column<string>(type: "text", nullable: true),
                note = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_time_entries", x => x.id);
                table.ForeignKey(
                    name: "FK_time_entries_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "pay_runs",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                pay_group_id = table.Column<string>(type: "text", nullable: false),
                period_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                gross_total = table.Column<decimal>(type: "numeric", nullable: false),
                deduction_total = table.Column<decimal>(type: "numeric", nullable: false),
                net_total = table.Column<decimal>(type: "numeric", nullable: false),
                calculated_by = table.Column<string>(type: "text", nullable: true),
                calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                journal_id = table.Column<string>(type: "text", nullable: true),
                posted_by = table.Column<string>(type: "text", nullable: true),
                posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reversal_of_run_id = table.Column<string>(type: "text", nullable: true),
                reversal_reason = table.Column<string>(type: "text", nullable: true),
                funds_released = table.Column<bool>(type: "boolean", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pay_runs", x => x.id);
                table.ForeignKey(
                    name: "FK_pay_runs_fiscal_periods_period_id",
                    column: x => x.period_id,
                    principalTable: "fiscal_periods",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_pay_runs_pay_groups_pay_group_id",
                    column: x => x.pay_group_id,
                    principalTable: "pay_groups",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_pay_runs_pay_runs_reversal_of_run_id",
                    column: x => x.reversal_of_run_id,
                    principalTable: "pay_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "worker_pay_groups",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                pay_group_id = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_worker_pay_groups", x => x.id);
                table.ForeignKey(
                    name: "FK_worker_pay_groups_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_worker_pay_groups_pay_groups_pay_group_id",
                    column: x => x.pay_group_id,
                    principalTable: "pay_groups",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "time_corrections",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                time_entry_id = table.Column<string>(type: "text", nullable: false),
                previous_hours = table.Column<decimal>(type: "numeric", nullable: false),
                previous_overtime_hours = table.Column<decimal>(type: "numeric", nullable: false),
                proposed_hours = table.Column<decimal>(type: "numeric", nullable: false),
                proposed_overtime_hours = table.Column<decimal>(type: "numeric", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                decision_reason = table.Column<string>(type: "text", nullable: true),
                requested_by = table.Column<string>(type: "text", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_time_corrections", x => x.id);
                table.ForeignKey(
                    name: "FK_time_corrections_time_entries_time_entry_id",
                    column: x => x.time_entry_id,
                    principalTable: "time_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "pay_run_lines",
            columns: table => new
            {
                pay_run_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                employment_id = table.Column<string>(type: "text", nullable: false),
                pay_code_id = table.Column<string>(type: "text", nullable: false),
                pay_code = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                hours = table.Column<decimal>(type: "numeric", nullable: false),
                rate = table.Column<decimal>(type: "numeric", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                source_entry_id = table.Column<string>(type: "text", nullable: true),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pay_run_lines", x => new { x.pay_run_id, x.line_no });
                table.ForeignKey(
                    name: "FK_pay_run_lines_pay_runs_pay_run_id",
                    column: x => x.pay_run_id,
                    principalTable: "pay_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "accounts",
            columns: new[] { "id", "code", "created_at", "created_by", "direct_posting_allowed", "legal_entity_id", "name", "normal_balance", "tenant_id", "type", "version" },
            values: new object[,]
            {
                { "ACC-2300", "2300", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, "LE-001", "Salaries Payable", 1, "T-001", 1, 1 },
                { "ACC-2310", "2310", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, "LE-001", "Payroll Deductions Payable", 1, "T-001", 1, 1 }
            });

        migrationBuilder.InsertData(
            table: "finance_settings",
            columns: new[] { "id", "key", "legal_entity_id", "tenant_id", "value" },
            values: new object[] { "FS-payroll-ot", "payroll.ot_multiplier", "LE-001", "T-001", "1.5" });

        migrationBuilder.InsertData(
            table: "pay_groups",
            columns: new[] { "id", "active", "code", "frequency", "legal_entity_id", "name", "tenant_id" },
            values: new object[] { "PG-001", true, "PG-001", "Monthly", "LE-001", "Monthly Staff", "T-001" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write" });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "legal_entity_id", "name", "permissions", "tenant_id" },
            values: new object[] { "R-HR", "LE-001", "hr_officer", new[] { "workers:read", "workers:write", "time:read", "time:write", "time:approve", "payroll:read", "payroll:calculate" }, "T-001" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-007", true, "HR Officer", "hr@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-007", "R-HR" });

        migrationBuilder.CreateIndex(
            name: "IX_compensation_events_employment_id",
            table: "compensation_events",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_compensation_events_tenant_id_legal_entity_id",
            table: "compensation_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_compensation_events_tenant_id_legal_entity_id_employment_id~",
            table: "compensation_events",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "effective_from" });

        migrationBuilder.CreateIndex(
            name: "IX_earning_deduction_codes_tenant_id_legal_entity_id",
            table: "earning_deduction_codes",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_earning_deduction_codes_tenant_id_legal_entity_id_code",
            table: "earning_deduction_codes",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_earning_deduction_codes_tenant_id_legal_entity_id_kind",
            table: "earning_deduction_codes",
            columns: new[] { "tenant_id", "legal_entity_id", "kind" });

        migrationBuilder.CreateIndex(
            name: "IX_employments_tenant_id_legal_entity_id",
            table: "employments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_employments_tenant_id_legal_entity_id_national_id",
            table: "employments",
            columns: new[] { "tenant_id", "legal_entity_id", "national_id" });

        migrationBuilder.CreateIndex(
            name: "IX_employments_tenant_id_legal_entity_id_status",
            table: "employments",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_employments_tenant_id_legal_entity_id_worker_no",
            table: "employments",
            columns: new[] { "tenant_id", "legal_entity_id", "worker_no" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_pay_groups_tenant_id_legal_entity_id",
            table: "pay_groups",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_pay_groups_tenant_id_legal_entity_id_code",
            table: "pay_groups",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_pay_run_lines_pay_run_id",
            table: "pay_run_lines",
            column: "pay_run_id");

        migrationBuilder.CreateIndex(
            name: "IX_pay_run_lines_tenant_id_legal_entity_id",
            table: "pay_run_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_pay_runs_pay_group_id",
            table: "pay_runs",
            column: "pay_group_id");

        migrationBuilder.CreateIndex(
            name: "IX_pay_runs_period_id",
            table: "pay_runs",
            column: "period_id");

        migrationBuilder.CreateIndex(
            name: "IX_pay_runs_reversal_of_run_id",
            table: "pay_runs",
            column: "reversal_of_run_id");

        migrationBuilder.CreateIndex(
            name: "IX_pay_runs_tenant_id_legal_entity_id",
            table: "pay_runs",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_pay_runs_tenant_id_legal_entity_id_pay_group_id_period_id",
            table: "pay_runs",
            columns: new[] { "tenant_id", "legal_entity_id", "pay_group_id", "period_id" },
            unique: true,
            filter: "status <> 4");

        migrationBuilder.CreateIndex(
            name: "IX_pay_runs_tenant_id_legal_entity_id_status",
            table: "pay_runs",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_time_corrections_tenant_id_legal_entity_id",
            table: "time_corrections",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_time_corrections_time_entry_id",
            table: "time_corrections",
            column: "time_entry_id");

        migrationBuilder.CreateIndex(
            name: "IX_time_entries_employment_id",
            table: "time_entries",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_time_entries_tenant_id_legal_entity_id",
            table: "time_entries",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_time_entries_tenant_id_legal_entity_id_employment_id_work_d~",
            table: "time_entries",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "work_date", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_time_entries_tenant_id_legal_entity_id_idem_key",
            table: "time_entries",
            columns: new[] { "tenant_id", "legal_entity_id", "idem_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_worker_pay_groups_employment_id",
            table: "worker_pay_groups",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_worker_pay_groups_pay_group_id",
            table: "worker_pay_groups",
            column: "pay_group_id");

        migrationBuilder.CreateIndex(
            name: "IX_worker_pay_groups_tenant_id_legal_entity_id",
            table: "worker_pay_groups",
            columns: new[] { "tenant_id", "legal_entity_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "compensation_events");

        migrationBuilder.DropTable(
            name: "earning_deduction_codes");

        migrationBuilder.DropTable(
            name: "pay_run_lines");

        migrationBuilder.DropTable(
            name: "time_corrections");

        migrationBuilder.DropTable(
            name: "worker_pay_groups");

        migrationBuilder.DropTable(
            name: "pay_runs");

        migrationBuilder.DropTable(
            name: "time_entries");

        migrationBuilder.DropTable(
            name: "pay_groups");

        migrationBuilder.DropTable(
            name: "employments");

        migrationBuilder.DeleteData(
            table: "accounts",
            keyColumn: "id",
            keyValue: "ACC-2300");

        migrationBuilder.DeleteData(
            table: "accounts",
            keyColumn: "id",
            keyValue: "ACC-2310");

        migrationBuilder.DeleteData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-payroll-ot");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-007" });

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-HR");

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-007");

        migrationBuilder.DropSequence(
            name: "doc_cmp");

        migrationBuilder.DropSequence(
            name: "doc_per");

        migrationBuilder.DropSequence(
            name: "doc_prn");

        migrationBuilder.DropSequence(
            name: "doc_tmc");

        migrationBuilder.DropSequence(
            name: "doc_tme");

        migrationBuilder.DropSequence(
            name: "doc_wkr");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:approve" });
    }
}
