using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124074722_AddFinanceSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "accounting_date",
            table: "journals",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "tax_amount",
            table: "journal_lines",
            type: "numeric",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "tax_code_id",
            table: "journal_lines",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "request_hash",
            table: "idempotency",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "accounts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                type = table.Column<int>(type: "integer", nullable: false),
                normal_balance = table.Column<int>(type: "integer", nullable: false),
                direct_posting_allowed = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "finance_settings",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                key = table.Column<string>(type: "text", nullable: false),
                value = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_settings", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "fiscal_periods",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                end_date = table.Column<DateOnly>(type: "date", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                closed_by = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reopen_reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fiscal_periods", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "tax_codes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                jurisdiction = table.Column<string>(type: "text", nullable: false),
                rate_pct = table.Column<decimal>(type: "numeric", nullable: false),
                basis = table.Column<int>(type: "integer", nullable: false),
                recoverable = table.Column<bool>(type: "boolean", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false),
                published_by = table.Column<string>(type: "text", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tax_codes", x => x.id);
            });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read" });

        // ---- task-5 seed (LE-001 pilot scope, mirrors the ErpDbContext.Seed scope) ----
        var seedTenant = "T-001";
        var seedEntity = "LE-001";
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // rolling fiscal calendar (R9d: plain calendar months) computed at APPLY time so every
        // database that runs this migration covers "today": 13 months, the deepest Closed (the
        // period-lock fixture), all others Open — periods, accounts, tax code and bucket settings
        // are seeded data, never code constants (R9b/R9c).
        var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        for (var back = 12; back >= 0; back--)
        {
            var start = currentMonth.AddMonths(-back);
            var isClosed = back == 12;
            migrationBuilder.InsertData(
                table: "fiscal_periods",
                columns: new[] { "id", "tenant_id", "legal_entity_id", "name", "start_date", "end_date", "status", "closed_by", "closed_at", "reopen_reason" },
                values: new object[]
                {
                    $"FP-{start:yyyy-MM}", seedTenant, seedEntity, start.ToString("yyyy-MM"),
                    start, start.AddMonths(1).AddDays(-1),
                    isClosed ? 2 : 0, // PeriodStatus.Closed : PeriodStatus.Open
                    isClosed ? "system" : null,
                    isClosed ? new DateTimeOffset(start.AddMonths(1).AddDays(-1).ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero) : null,
                    null,
                });
        }

        // chart of accounts (FINANCE-001/002) — all direct-postable; type/normal balance per BD CoA
        migrationBuilder.InsertData(
            table: "accounts",
            columns: new[] { "id", "tenant_id", "legal_entity_id", "code", "name", "type", "normal_balance", "direct_posting_allowed", "version", "created_by", "created_at" },
            values: new object[,]
            {
                { "ACC-1200", seedTenant, seedEntity, "1200", "Accounts Receivable", 0, 0, true, 1, "system", seedTime },
                { "ACC-1400", seedTenant, seedEntity, "1400", "Inventory", 0, 0, true, 1, "system", seedTime },
                { "ACC-1450", seedTenant, seedEntity, "1450", "Input VAT Recoverable", 0, 0, true, 1, "system", seedTime },
                { "ACC-1500", seedTenant, seedEntity, "1500", "Prepaid Expenses", 0, 0, true, 1, "system", seedTime },
                { "ACC-1600", seedTenant, seedEntity, "1600", "Equipment", 0, 0, true, 1, "system", seedTime },
                { "ACC-1610", seedTenant, seedEntity, "1610", "Accumulated Depreciation", 0, 1, true, 1, "system", seedTime },
                { "ACC-2100", seedTenant, seedEntity, "2100", "Accounts Payable", 1, 1, true, 1, "system", seedTime },
                { "ACC-2200", seedTenant, seedEntity, "2200", "VAT Payable", 1, 1, true, 1, "system", seedTime },
                { "ACC-4000", seedTenant, seedEntity, "4000", "Sales Revenue", 3, 1, true, 1, "system", seedTime },
                { "ACC-5100", seedTenant, seedEntity, "5100", "Cost of Goods Sold", 4, 0, true, 1, "system", seedTime },
                { "ACC-6100", seedTenant, seedEntity, "6100", "Salaries and Wages Expense", 4, 0, true, 1, "system", seedTime },
                { "ACC-6400", seedTenant, seedEntity, "6400", "Depreciation Expense", 4, 0, true, 1, "system", seedTime },
                { "ACC-6500", seedTenant, seedEntity, "6500", "Loss on Asset Disposal", 4, 0, true, 1, "system", seedTime },
                { "ACC-6600", seedTenant, seedEntity, "6600", "Gain on Asset Disposal", 3, 1, true, 1, "system", seedTime },
            });

        // VAT-STD-BD (R9c: 15% is SEED DATA, operator-configurable — logic never hardcodes the rate)
        migrationBuilder.InsertData(
            table: "tax_codes",
            columns: new[] { "id", "tenant_id", "legal_entity_id", "code", "jurisdiction", "rate_pct", "basis", "recoverable", "effective_from", "effective_to", "version", "published_by", "published_at" },
            values: new object[] { "TC-VAT-STD-BD-V1", seedTenant, seedEntity, "VAT-STD-BD", "BD", 15.00m, 0, true, new DateOnly(2020, 1, 1), null, 1, "system", seedTime });

        // aging buckets (R9b): boundaries 0-30/31-60/61-90/90+ + 30-day credit terms as data
        migrationBuilder.InsertData(
            table: "finance_settings",
            columns: new[] { "id", "tenant_id", "legal_entity_id", "key", "value" },
            values: new object[] { "FS-aging", seedTenant, seedEntity, "aging", "{\"bucketDays\":[30,60,90],\"creditTermsDays\":30}" });

        migrationBuilder.CreateIndex(
            name: "IX_accounts_tenant_id_legal_entity_id",
            table: "accounts",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_accounts_tenant_id_legal_entity_id_code",
            table: "accounts",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_settings_tenant_id_legal_entity_id_key",
            table: "finance_settings",
            columns: new[] { "tenant_id", "legal_entity_id", "key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_periods_tenant_id_legal_entity_id",
            table: "fiscal_periods",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_periods_tenant_id_legal_entity_id_name",
            table: "fiscal_periods",
            columns: new[] { "tenant_id", "legal_entity_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tax_codes_tenant_id_legal_entity_id",
            table: "tax_codes",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_tax_codes_tenant_id_legal_entity_id_code",
            table: "tax_codes",
            columns: new[] { "tenant_id", "legal_entity_id", "code" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "accounts");

        migrationBuilder.DropTable(
            name: "finance_settings");

        migrationBuilder.DropTable(
            name: "fiscal_periods");

        migrationBuilder.DropTable(
            name: "tax_codes");

        migrationBuilder.DropColumn(
            name: "accounting_date",
            table: "journals");

        migrationBuilder.DropColumn(
            name: "tax_amount",
            table: "journal_lines");

        migrationBuilder.DropColumn(
            name: "tax_code_id",
            table: "journal_lines");

        migrationBuilder.DropColumn(
            name: "request_hash",
            table: "idempotency");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "ap:read", "po:approve" });
    }
}
