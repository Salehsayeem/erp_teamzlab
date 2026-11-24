using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124114854_FixContractAmendmentGuards : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_contracts_award_id",
            table: "contracts");

        migrationBuilder.CreateIndex(
            name: "IX_contracts_award_id",
            table: "contracts",
            column: "award_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_contract_amendments_contract_id_version",
            table: "contract_amendments",
            columns: new[] { "contract_id", "version" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_contracts_award_id",
            table: "contracts");

        migrationBuilder.DropIndex(
            name: "IX_contract_amendments_contract_id_version",
            table: "contract_amendments");

        migrationBuilder.CreateIndex(
            name: "IX_contracts_award_id",
            table: "contracts",
            column: "award_id");
    }
}
