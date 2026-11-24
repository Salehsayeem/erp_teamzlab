using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124113151_AddInvoiceDiscardIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_invoices_tenant_id_legal_entity_id_source_event_id",
            table: "invoices");

        migrationBuilder.CreateIndex(
            name: "IX_invoices_tenant_id_legal_entity_id_source_event_id",
            table: "invoices",
            columns: new[] { "tenant_id", "legal_entity_id", "source_event_id" },
            unique: true,
            filter: "status <> 2");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_invoices_tenant_id_legal_entity_id_source_event_id",
            table: "invoices");

        migrationBuilder.CreateIndex(
            name: "IX_invoices_tenant_id_legal_entity_id_source_event_id",
            table: "invoices",
            columns: new[] { "tenant_id", "legal_entity_id", "source_event_id" },
            unique: true);
    }
}
