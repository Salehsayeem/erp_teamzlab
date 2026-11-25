using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261125063502_AddDepreciationPeriodUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_depreciation_runs_tenant_id_legal_entity_id_period_id",
            table: "depreciation_runs");

        migrationBuilder.CreateIndex(
            name: "IX_depreciation_runs_tenant_id_legal_entity_id_period_id",
            table: "depreciation_runs",
            columns: new[] { "tenant_id", "legal_entity_id", "period_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_depreciation_runs_tenant_id_legal_entity_id_period_id",
            table: "depreciation_runs");

        migrationBuilder.CreateIndex(
            name: "IX_depreciation_runs_tenant_id_legal_entity_id_period_id",
            table: "depreciation_runs",
            columns: new[] { "tenant_id", "legal_entity_id", "period_id" });
    }
}
