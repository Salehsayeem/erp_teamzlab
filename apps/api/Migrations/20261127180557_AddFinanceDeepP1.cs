using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261127180557_AddFinanceDeepP1 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_ctk",
            startValue: 2L);

        migrationBuilder.CreateSequence(
            name: "doc_fcal",
            startValue: 2L);

        migrationBuilder.AddColumn<bool>(
            name: "is_adjustment",
            table: "fiscal_periods",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "close_tasks",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                period_id = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                mandatory = table.Column<bool>(type: "boolean", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: true),
                depends_on_task_id = table.Column<string>(type: "text", nullable: true),
                evidence_ref = table.Column<string>(type: "text", nullable: true),
                waiver_reason = table.Column<string>(type: "text", nullable: true),
                na_reason = table.Column<string>(type: "text", nullable: true),
                completed_by = table.Column<string>(type: "text", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_close_tasks", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "fiscal_calendars",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                pattern = table.Column<int>(type: "integer", nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                end_date = table.Column<DateOnly>(type: "date", nullable: false),
                activated = table.Column<bool>(type: "boolean", nullable: false),
                activated_by = table.Column<string>(type: "text", nullable: true),
                activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fiscal_calendars", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "fiscal_calendar_periods",
            columns: table => new
            {
                calendar_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "text", nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                end_date = table.Column<DateOnly>(type: "date", nullable: false),
                is_adjustment = table.Column<bool>(type: "boolean", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fiscal_calendar_periods", x => new { x.calendar_id, x.line_no });
                table.ForeignKey(
                    name: "FK_fiscal_calendar_periods_fiscal_calendars_calendar_id",
                    column: x => x.calendar_id,
                    principalTable: "fiscal_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_close_tasks_tenant_id_legal_entity_id",
            table: "close_tasks",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_close_tasks_tenant_id_legal_entity_id_period_id",
            table: "close_tasks",
            columns: new[] { "tenant_id", "legal_entity_id", "period_id" });

        migrationBuilder.CreateIndex(
            name: "IX_close_tasks_tenant_id_legal_entity_id_status",
            table: "close_tasks",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_calendar_periods_calendar_id",
            table: "fiscal_calendar_periods",
            column: "calendar_id");

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_calendar_periods_tenant_id_legal_entity_id",
            table: "fiscal_calendar_periods",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_calendars_tenant_id_legal_entity_id",
            table: "fiscal_calendars",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_calendars_tenant_id_legal_entity_id_start_date",
            table: "fiscal_calendars",
            columns: new[] { "tenant_id", "legal_entity_id", "start_date" });

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_calendars_tenant_id_legal_entity_id_year_pattern",
            table: "fiscal_calendars",
            columns: new[] { "tenant_id", "legal_entity_id", "year", "pattern" },
            unique: true);

        // -------- demo seeds (hand-appended; same apply-time pattern as AddFinanceSeed) --------
        // FCAL-0001: a DRAFT 4-4-5 FY2027 calendar with its 13 period rows — the periods screen
        // has a layout to show and the activation flow (004 AC2) is demonstrable via the API.
        // Unactivated ON PURPOSE: an activated 2027 chain would collide with fixtures that create
        // their own probe periods via POST /fiscal-periods.
        migrationBuilder.InsertData(
            table: "fiscal_calendars",
            columns: new[] { "id", "tenant_id", "legal_entity_id", "name", "year", "pattern", "start_date", "end_date", "activated", "activated_by", "activated_at" },
            values: new object[] { "FCAL-0001", "T-001", "LE-001", "FY2027 4-4-5 (demo)", 2027, 1, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), false, null, null });

        // the 4-4-5 walk: quarters of 4-4-5 weeks — 12 periods cover 364 days, the year remainder
        // is the ADJUSTMENT period (a 53-week year pushes its extra week there, never into a month)
        migrationBuilder.InsertData(
            table: "fiscal_calendar_periods",
            columns: new[] { "calendar_id", "line_no", "tenant_id", "legal_entity_id", "name", "start_date", "end_date", "is_adjustment" },
            values: new object[,]
            {
                { "FCAL-0001", 1, "T-001", "LE-001", "2027-P01", new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 28), false },
                { "FCAL-0001", 2, "T-001", "LE-001", "2027-P02", new DateOnly(2027, 1, 29), new DateOnly(2027, 2, 25), false },
                { "FCAL-0001", 3, "T-001", "LE-001", "2027-P03", new DateOnly(2027, 2, 26), new DateOnly(2027, 4, 1), false },
                { "FCAL-0001", 4, "T-001", "LE-001", "2027-P04", new DateOnly(2027, 4, 2), new DateOnly(2027, 4, 29), false },
                { "FCAL-0001", 5, "T-001", "LE-001", "2027-P05", new DateOnly(2027, 4, 30), new DateOnly(2027, 5, 27), false },
                { "FCAL-0001", 6, "T-001", "LE-001", "2027-P06", new DateOnly(2027, 5, 28), new DateOnly(2027, 7, 1), false },
                { "FCAL-0001", 7, "T-001", "LE-001", "2027-P07", new DateOnly(2027, 7, 2), new DateOnly(2027, 7, 29), false },
                { "FCAL-0001", 8, "T-001", "LE-001", "2027-P08", new DateOnly(2027, 7, 30), new DateOnly(2027, 8, 26), false },
                { "FCAL-0001", 9, "T-001", "LE-001", "2027-P09", new DateOnly(2027, 8, 27), new DateOnly(2027, 9, 30), false },
                { "FCAL-0001", 10, "T-001", "LE-001", "2027-P10", new DateOnly(2027, 10, 1), new DateOnly(2027, 10, 28), false },
                { "FCAL-0001", 11, "T-001", "LE-001", "2027-P11", new DateOnly(2027, 10, 29), new DateOnly(2027, 11, 25), false },
                { "FCAL-0001", 12, "T-001", "LE-001", "2027-P12", new DateOnly(2027, 11, 26), new DateOnly(2027, 12, 30), false },
                { "FCAL-0001", 13, "T-001", "LE-001", "2027-ADJ", new DateOnly(2027, 12, 31), new DateOnly(2027, 12, 31), true },
            });

        // CTK-0001: one mandatory reconciliation task on the seeded NEXT-month period (computed at
        // APPLY time like AddFinanceSeed's rolling months) so the 063 gate is demonstrable from a
        // fresh database: close that period → 409 CLOSE_GATE_BLOCKED naming CTK-0001. Chosen on
        // the next month deliberately: the test suites close only dedicated probes or the CURRENT
        // month, so the demo task never blocks another suite's lock fixtures.
        var demoPeriod = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1);
        migrationBuilder.InsertData(
            table: "close_tasks",
            columns: new[] { "id", "tenant_id", "legal_entity_id", "period_id", "title", "mandatory", "status" },
            values: new object[] { "CTK-0001", "T-001", "LE-001", $"FP-{demoPeriod:yyyy-MM}", "Bank reconciliation", true, 0 });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var demoPeriod = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1);
        migrationBuilder.DeleteData(
            table: "close_tasks",
            keyColumn: "id",
            keyValue: "CTK-0001");

        migrationBuilder.DeleteData(
            table: "fiscal_calendar_periods",
            keyColumn: "calendar_id",
            keyValue: "FCAL-0001");

        migrationBuilder.DeleteData(
            table: "fiscal_calendars",
            keyColumn: "id",
            keyValue: "FCAL-0001");

        migrationBuilder.DropTable(
            name: "close_tasks");

        migrationBuilder.DropTable(
            name: "fiscal_calendar_periods");

        migrationBuilder.DropTable(
            name: "fiscal_calendars");

        migrationBuilder.DropColumn(
            name: "is_adjustment",
            table: "fiscal_periods");

        migrationBuilder.DropSequence(
            name: "doc_ctk");

        migrationBuilder.DropSequence(
            name: "doc_fcal");
    }
}
