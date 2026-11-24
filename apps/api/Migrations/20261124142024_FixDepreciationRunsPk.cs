using Erp.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

// T4 review finding 3 (R-T4l): depreciation_runs.id was the raw Idempotency-Key, so the PK was a
// GLOBAL key — a second legal entity reusing a period id ("2026-11") hit a raw unique violation
// and a 500 on month-end. AddAssetsSeed was hand-edited to the composite PK, but it had ALREADY
// been applied to the dev database before the edit, so the fix never reached it ("already up to
// date"). This forward-only migration rebuilds the PK where it is still single-column; on fresh
// databases it drops and re-adds the composite PK AddAssetsSeed already created (harmless no-op
// in effect). Context attributes live on the class — no Designer file needed for a hand-authored
// migration whose post-state equals the current model snapshot.
[DbContext(typeof(ErpDbContext))]
[Migration("20261124142024_FixDepreciationRunsPk")]
public partial class FixDepreciationRunsPk : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_depreciation_runs", table: "depreciation_runs");
        migrationBuilder.AddPrimaryKey(
            name: "PK_depreciation_runs",
            table: "depreciation_runs",
            columns: new[] { "id", "tenant_id", "legal_entity_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_depreciation_runs", table: "depreciation_runs");
        migrationBuilder.AddPrimaryKey(name: "PK_depreciation_runs", table: "depreciation_runs", column: "id");
    }
}
