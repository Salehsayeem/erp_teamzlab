using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261127182542_AddPoReadSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write", "refunds:approve", "rebates:settle", "forecasts:read", "leave:adjust", "payroll:release", "payroll:close", "people:analytics:read", "requisitions:approve", "requisitions:read", "requisitions:override", "suppliers:review", "analytics:read", "fx:read", "fx:write", "close:write", "close:approve", "tax:publish", "ap:write", "ap:approve", "ap:override", "ar:write", "gl:write" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-PROC",
            column: "permissions",
            value: new[] { "suppliers:write", "po:read", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read", "requisitions:write", "requisitions:read", "catalog:write", "receipts:inspect", "analytics:read" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write", "refunds:approve", "rebates:settle", "forecasts:read", "leave:adjust", "payroll:release", "payroll:close", "people:analytics:read", "requisitions:approve", "requisitions:read", "requisitions:override", "suppliers:review", "analytics:read", "fx:read", "fx:write", "close:write", "close:approve", "tax:publish", "ap:write", "ap:approve", "ap:override", "ar:write", "gl:write" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-PROC",
            column: "permissions",
            value: new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read", "requisitions:write", "requisitions:read", "catalog:write", "receipts:inspect", "analytics:read" });
    }
}
