using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261127150951_AddProcurementTaxonomySeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "procurement_categories",
            columns: new[] { "id", "created_at", "created_by", "effective_from", "effective_to", "external_code", "legal_entity_id", "name", "parent_id", "status", "tenant_id" },
            values: new object[] { "CAT-SEED-001", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", new DateOnly(2026, 1, 1), null, null, "LE-001", "General Office Supplies", null, 0, "T-001" });

        migrationBuilder.InsertData(
            table: "unit_of_measures",
            columns: new[] { "id", "code", "created_at", "created_by", "dimension", "is_base", "legal_entity_id", "name", "precision", "tenant_id" },
            values: new object[] { "UOM-SEED-EA", "EA", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", 0, true, "LE-001", "Each", 0, "T-001" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "procurement_categories",
            keyColumn: "id",
            keyValue: "CAT-SEED-001");

        migrationBuilder.DeleteData(
            table: "unit_of_measures",
            keyColumn: "id",
            keyValue: "UOM-SEED-EA");
    }
}
