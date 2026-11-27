using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261127151812_AddFinanceDeepP5 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_fxr",
            startValue: 3L);

        migrationBuilder.AddColumn<decimal>(
            name: "amount_in_currency",
            table: "journal_lines",
            type: "numeric",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "currency",
            table: "journal_lines",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "dimensions",
            table: "journal_lines",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "effective_from",
            table: "finance_settings",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "updated_at",
            table: "finance_settings",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "updated_by",
            table: "finance_settings",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "currencies",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                decimal_scale = table.Column<int>(type: "integer", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_currencies", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "dimension_sets",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                required = table.Column<bool>(type: "boolean", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dimension_sets", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "dimension_values",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                set_code = table.Column<string>(type: "text", nullable: false),
                value = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dimension_values", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "fx_rates",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                base_currency = table.Column<string>(type: "text", nullable: false),
                quote_currency = table.Column<string>(type: "text", nullable: false),
                rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                rate_type = table.Column<int>(type: "integer", nullable: false),
                rate = table.Column<decimal>(type: "numeric", nullable: false),
                precision = table.Column<int>(type: "integer", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fx_rates", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "number_series_voids",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                prefix = table.Column<string>(type: "text", nullable: false),
                number = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                voided_by = table.Column<string>(type: "text", nullable: false),
                voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_number_series_voids", x => x.id);
            });

        migrationBuilder.InsertData(
            table: "currencies",
            columns: new[] { "id", "active", "code", "decimal_scale", "legal_entity_id", "name", "tenant_id" },
            values: new object[,]
            {
                { "CUR-BDT", true, "BDT", 2, "LE-001", "Bangladeshi Taka", "T-001" },
                { "CUR-JPY", true, "JPY", 0, "LE-001", "Japanese Yen", "T-001" },
                { "CUR-USD", true, "USD", 2, "LE-001", "US Dollar", "T-001" }
            });

        migrationBuilder.InsertData(
            table: "dimension_sets",
            columns: new[] { "id", "active", "code", "legal_entity_id", "name", "required", "tenant_id" },
            values: new object[] { "DS-CostCenter", true, "CostCenter", "LE-001", "Cost Center", false, "T-001" });

        migrationBuilder.InsertData(
            table: "dimension_values",
            columns: new[] { "id", "active", "effective_from", "effective_to", "legal_entity_id", "set_code", "tenant_id", "value" },
            values: new object[,]
            {
                { "DV-CostCenter-CC-001", true, new DateOnly(2026, 1, 1), null, "LE-001", "CostCenter", "T-001", "CC-001" },
                { "DV-CostCenter-CC-002", false, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "LE-001", "CostCenter", "T-001", "CC-002" }
            });

        migrationBuilder.UpdateData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-budget-mode",
            columns: new[] { "effective_from", "updated_at", "updated_by" },
            values: new object[] { null, null, null });

        migrationBuilder.UpdateData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-discount-cap",
            columns: new[] { "effective_from", "updated_at", "updated_by" },
            values: new object[] { null, null, null });

        migrationBuilder.UpdateData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-mfg-downtime",
            columns: new[] { "effective_from", "updated_at", "updated_by" },
            values: new object[] { null, null, null });

        migrationBuilder.UpdateData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-payroll-ot",
            columns: new[] { "effective_from", "updated_at", "updated_by" },
            values: new object[] { null, null, null });

        migrationBuilder.UpdateData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-quotes-threshold",
            columns: new[] { "effective_from", "updated_at", "updated_by" },
            values: new object[] { null, null, null });

        migrationBuilder.UpdateData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-stage-weights",
            columns: new[] { "effective_from", "updated_at", "updated_by" },
            values: new object[] { null, null, null });

        migrationBuilder.InsertData(
            table: "finance_settings",
            columns: new[] { "id", "effective_from", "key", "legal_entity_id", "tenant_id", "updated_at", "updated_by", "value" },
            values: new object[] { "FS-functional-currency", new DateOnly(2026, 1, 1), "finance.functional_currency", "LE-001", "T-001", null, null, "BDT" });

        migrationBuilder.InsertData(
            table: "fx_rates",
            columns: new[] { "id", "approved_at", "approved_by", "base_currency", "created_by", "legal_entity_id", "precision", "quote_currency", "rate", "rate_date", "rate_type", "retrieved_at", "source", "status", "tenant_id" },
            values: new object[,]
            {
                { "FXR-0001", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "USD", "system", "LE-001", 4, "BDT", 122.5000m, new DateOnly(2026, 1, 1), 0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed:bn-bank-indicator", 1, "T-001" },
                { "FXR-0002", null, null, "JPY", "system", "LE-001", 4, "BDT", 0.8200m, new DateOnly(2026, 1, 1), 0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed:bn-bank-indicator", 0, "T-001" }
            });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write", "refunds:approve", "rebates:settle", "forecasts:read", "leave:adjust", "payroll:release", "payroll:close", "people:analytics:read", "requisitions:approve", "requisitions:read", "requisitions:override", "suppliers:review", "analytics:read", "fx:read", "fx:write", "close:write", "close:approve", "tax:publish", "ap:write", "ap:approve", "ap:override", "ar:write", "gl:write" });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "legal_entity_id", "name", "permissions", "tenant_id" },
            values: new object[] { "R-FIN2", "LE-001", "finance_maker", new[] { "journals:write", "periods:read", "tax:read", "tax:write", "ap:read", "ap:write", "ar:write" }, "T-001" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-015", true, "Finance Checker", "fin-checker@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-015", "R-FIN2" });

        migrationBuilder.CreateIndex(
            name: "IX_currencies_tenant_id_legal_entity_id",
            table: "currencies",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_currencies_tenant_id_legal_entity_id_code",
            table: "currencies",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_dimension_sets_tenant_id_legal_entity_id",
            table: "dimension_sets",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_dimension_sets_tenant_id_legal_entity_id_code",
            table: "dimension_sets",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_dimension_values_tenant_id_legal_entity_id",
            table: "dimension_values",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_dimension_values_tenant_id_legal_entity_id_set_code",
            table: "dimension_values",
            columns: new[] { "tenant_id", "legal_entity_id", "set_code" });

        migrationBuilder.CreateIndex(
            name: "IX_dimension_values_tenant_id_legal_entity_id_set_code_value",
            table: "dimension_values",
            columns: new[] { "tenant_id", "legal_entity_id", "set_code", "value" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_fx_rates_tenant_id_legal_entity_id",
            table: "fx_rates",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_fx_rates_tenant_id_legal_entity_id_base_currency_quote_curr~",
            table: "fx_rates",
            columns: new[] { "tenant_id", "legal_entity_id", "base_currency", "quote_currency", "rate_date", "rate_type", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_number_series_voids_tenant_id_legal_entity_id",
            table: "number_series_voids",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_number_series_voids_tenant_id_legal_entity_id_prefix_number",
            table: "number_series_voids",
            columns: new[] { "tenant_id", "legal_entity_id", "prefix", "number" },
            unique: true);

        // 008 DB backstop (fix round): at most ONE Approved rate per pair/date/type — the
        // service-level pre-check is read-then-write, so racing approvals are settled here.
        // status is an int column: Approved = 1 (Pending 0, Withdrawn 2). Hand-edited in place
        // while this migration is unapplied (standing ruling); the Designer is left as-is —
        // runtime applies Up(), and this raw-SQL index is scaffolding-invisible by design.
        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX fx_rates_one_approved_per_key ON fx_rates (tenant_id, legal_entity_id, base_currency, quote_currency, rate_date, rate_type) WHERE status = 1;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX fx_rates_one_approved_per_key;");

        migrationBuilder.DropTable(
            name: "currencies");

        migrationBuilder.DropTable(
            name: "dimension_sets");

        migrationBuilder.DropTable(
            name: "dimension_values");

        migrationBuilder.DropTable(
            name: "fx_rates");

        migrationBuilder.DropTable(
            name: "number_series_voids");

        migrationBuilder.DeleteData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-functional-currency");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-015" });

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN2");

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-015");

        migrationBuilder.DropColumn(
            name: "amount_in_currency",
            table: "journal_lines");

        migrationBuilder.DropColumn(
            name: "currency",
            table: "journal_lines");

        migrationBuilder.DropColumn(
            name: "dimensions",
            table: "journal_lines");

        migrationBuilder.DropColumn(
            name: "effective_from",
            table: "finance_settings");

        migrationBuilder.DropColumn(
            name: "updated_at",
            table: "finance_settings");

        migrationBuilder.DropColumn(
            name: "updated_by",
            table: "finance_settings");

        migrationBuilder.DropSequence(
            name: "doc_fxr");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write", "refunds:approve", "rebates:settle", "forecasts:read", "leave:adjust", "payroll:release", "payroll:close", "people:analytics:read", "requisitions:approve", "requisitions:read", "requisitions:override", "suppliers:review", "analytics:read" });
    }
}
