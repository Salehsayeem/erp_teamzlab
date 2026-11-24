using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261124110133_AddProcurementDeepSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_amd");

        migrationBuilder.CreateSequence(
            name: "doc_awd");

        migrationBuilder.CreateSequence(
            name: "doc_bid");

        migrationBuilder.CreateSequence(
            name: "doc_ctr");

        migrationBuilder.CreateSequence(
            name: "doc_ev");

        migrationBuilder.CreateSequence(
            name: "doc_lot");

        migrationBuilder.CreateSequence(
            name: "doc_obl");

        migrationBuilder.CreateSequence(
            name: "doc_qa");

        migrationBuilder.AddColumn<int>(
            name: "status",
            table: "suppliers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "status_reason",
            table: "suppliers",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "sourcing_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                event_type = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                time_zone_name = table.Column<string>(type: "text", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sourcing_events", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "supplier_gates",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                gate_code = table.Column<string>(type: "text", nullable: false),
                severity = table.Column<int>(type: "integer", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                evidence_ref = table.Column<string>(type: "text", nullable: true),
                exception_by = table.Column<string>(type: "text", nullable: true),
                exception_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_gates", x => x.id);
                table.ForeignKey(
                    name: "FK_supplier_gates_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "bids",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                event_version = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                content_digest = table.Column<string>(type: "text", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                opened_by = table.Column<string>(type: "text", nullable: true),
                opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bids", x => x.id);
                table.ForeignKey(
                    name: "FK_bids_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_bids_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "eval_criteria",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                weight_pct = table.Column<decimal>(type: "numeric", nullable: false),
                scale_max = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_eval_criteria", x => x.id);
                table.ForeignKey(
                    name: "FK_eval_criteria_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "evaluator_assignments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_evaluator_assignments", x => x.id);
                table.ForeignKey(
                    name: "FK_evaluator_assignments_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "event_invitations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                channel = table.Column<string>(type: "text", nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                failure_reason = table.Column<string>(type: "text", nullable: true),
                warning_gates = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_event_invitations", x => x.id);
                table.ForeignKey(
                    name: "FK_event_invitations_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_event_invitations_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "event_lots",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                lot_no = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: true),
                target_qty = table.Column<decimal>(type: "numeric", nullable: true),
                ceiling_value = table.Column<decimal>(type: "numeric", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_event_lots", x => x.id);
                table.ForeignKey(
                    name: "FK_event_lots_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "event_qas",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: true),
                question = table.Column<string>(type: "text", nullable: false),
                answer = table.Column<string>(type: "text", nullable: true),
                scope = table.Column<int>(type: "integer", nullable: false),
                published = table.Column<bool>(type: "boolean", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                answered_by = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_event_qas", x => x.id);
                table.ForeignKey(
                    name: "FK_event_qas_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "score_cards",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                bid_id = table.Column<string>(type: "text", nullable: false),
                criterion_id = table.Column<string>(type: "text", nullable: false),
                evaluator_user_id = table.Column<string>(type: "text", nullable: false),
                raw_score = table.Column<decimal>(type: "numeric", nullable: true),
                pass_fail = table.Column<bool>(type: "boolean", nullable: true),
                comment = table.Column<string>(type: "text", nullable: true),
                criteria_version = table.Column<int>(type: "integer", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_score_cards", x => x.id);
                table.ForeignKey(
                    name: "FK_score_cards_bids_bid_id",
                    column: x => x.bid_id,
                    principalTable: "bids",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_score_cards_eval_criteria_criterion_id",
                    column: x => x.criterion_id,
                    principalTable: "eval_criteria",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_score_cards_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "awards",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                event_id = table.Column<string>(type: "text", nullable: false),
                lot_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                basis_event_version = table.Column<int>(type: "integer", nullable: false),
                recommendation = table.Column<string>(type: "text", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_awards", x => x.id);
                table.ForeignKey(
                    name: "FK_awards_event_lots_lot_id",
                    column: x => x.lot_id,
                    principalTable: "event_lots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_awards_sourcing_events_event_id",
                    column: x => x.event_id,
                    principalTable: "sourcing_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_awards_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "bid_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                bid_id = table.Column<string>(type: "text", nullable: false),
                lot_id = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                discount_pct = table.Column<decimal>(type: "numeric", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bid_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_bid_lines_bids_bid_id",
                    column: x => x.bid_id,
                    principalTable: "bids",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_bid_lines_event_lots_lot_id",
                    column: x => x.lot_id,
                    principalTable: "event_lots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "contracts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                award_id = table.Column<string>(type: "text", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: false),
                renewal_notice_days = table.Column<int>(type: "integer", nullable: true),
                option_periods_total = table.Column<int>(type: "integer", nullable: true),
                options_exercised = table.Column<int>(type: "integer", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                activated_by = table.Column<string>(type: "text", nullable: true),
                activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contracts", x => x.id);
                table.ForeignKey(
                    name: "FK_contracts_awards_award_id",
                    column: x => x.award_id,
                    principalTable: "awards",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_contracts_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "contract_amendments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                contract_id = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                summary = table.Column<string>(type: "text", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: false),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contract_amendments", x => x.id);
                table.ForeignKey(
                    name: "FK_contract_amendments_contracts_contract_id",
                    column: x => x.contract_id,
                    principalTable: "contracts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "contract_obligations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                contract_id = table.Column<string>(type: "text", nullable: false),
                obligation_type = table.Column<string>(type: "text", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: false),
                source_clause = table.Column<string>(type: "text", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                evidence_ref = table.Column<string>(type: "text", nullable: true),
                verified_by = table.Column<string>(type: "text", nullable: true),
                verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                waiver_reason = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contract_obligations", x => x.id);
                table.ForeignKey(
                    name: "FK_contract_obligations_contracts_contract_id",
                    column: x => x.contract_id,
                    principalTable: "contracts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "contract_price_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                contract_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contract_price_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_contract_price_lines_contracts_contract_id",
                    column: x => x.contract_id,
                    principalTable: "contracts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:approve" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-PROC",
            column: "permissions",
            value: new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read" });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "legal_entity_id", "name", "permissions", "tenant_id" },
            values: new object[] { "R-EVAL", "LE-001", "evaluator", new[] { "sourcing:read", "sourcing:evaluate" }, "T-001" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-006", true, "Independent Evaluator", "evaluator@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-006", "R-EVAL" });

        migrationBuilder.CreateIndex(
            name: "IX_awards_event_id",
            table: "awards",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_awards_lot_id",
            table: "awards",
            column: "lot_id");

        migrationBuilder.CreateIndex(
            name: "IX_awards_supplier_id",
            table: "awards",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_awards_tenant_id_legal_entity_id",
            table: "awards",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_bid_lines_bid_id",
            table: "bid_lines",
            column: "bid_id");

        migrationBuilder.CreateIndex(
            name: "IX_bid_lines_lot_id",
            table: "bid_lines",
            column: "lot_id");

        migrationBuilder.CreateIndex(
            name: "IX_bid_lines_tenant_id_legal_entity_id",
            table: "bid_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_bids_event_id",
            table: "bids",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_bids_supplier_id",
            table: "bids",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_bids_tenant_id_legal_entity_id",
            table: "bids",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_contract_amendments_contract_id",
            table: "contract_amendments",
            column: "contract_id");

        migrationBuilder.CreateIndex(
            name: "IX_contract_amendments_tenant_id_legal_entity_id",
            table: "contract_amendments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_contract_obligations_contract_id",
            table: "contract_obligations",
            column: "contract_id");

        migrationBuilder.CreateIndex(
            name: "IX_contract_obligations_tenant_id_legal_entity_id",
            table: "contract_obligations",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_contract_obligations_tenant_id_legal_entity_id_due_date",
            table: "contract_obligations",
            columns: new[] { "tenant_id", "legal_entity_id", "due_date" });

        migrationBuilder.CreateIndex(
            name: "IX_contract_price_lines_contract_id",
            table: "contract_price_lines",
            column: "contract_id");

        migrationBuilder.CreateIndex(
            name: "IX_contract_price_lines_tenant_id_legal_entity_id",
            table: "contract_price_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_contracts_award_id",
            table: "contracts",
            column: "award_id");

        migrationBuilder.CreateIndex(
            name: "IX_contracts_supplier_id",
            table: "contracts",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_contracts_tenant_id_legal_entity_id",
            table: "contracts",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_contracts_tenant_id_legal_entity_id_status",
            table: "contracts",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_eval_criteria_event_id",
            table: "eval_criteria",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_eval_criteria_tenant_id_legal_entity_id",
            table: "eval_criteria",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_evaluator_assignments_event_id",
            table: "evaluator_assignments",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_evaluator_assignments_tenant_id_legal_entity_id",
            table: "evaluator_assignments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_event_invitations_event_id",
            table: "event_invitations",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_event_invitations_supplier_id",
            table: "event_invitations",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_event_invitations_tenant_id_legal_entity_id",
            table: "event_invitations",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_event_lots_event_id",
            table: "event_lots",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_event_lots_tenant_id_legal_entity_id",
            table: "event_lots",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_event_qas_event_id",
            table: "event_qas",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_event_qas_tenant_id_legal_entity_id",
            table: "event_qas",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_score_cards_bid_id",
            table: "score_cards",
            column: "bid_id");

        migrationBuilder.CreateIndex(
            name: "IX_score_cards_criterion_id",
            table: "score_cards",
            column: "criterion_id");

        migrationBuilder.CreateIndex(
            name: "IX_score_cards_event_id",
            table: "score_cards",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "IX_score_cards_tenant_id_legal_entity_id",
            table: "score_cards",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sourcing_events_tenant_id_legal_entity_id",
            table: "sourcing_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sourcing_events_tenant_id_legal_entity_id_event_type",
            table: "sourcing_events",
            columns: new[] { "tenant_id", "legal_entity_id", "event_type" });

        migrationBuilder.CreateIndex(
            name: "IX_sourcing_events_tenant_id_legal_entity_id_status",
            table: "sourcing_events",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_gates_supplier_id",
            table: "supplier_gates",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_gates_tenant_id_legal_entity_id",
            table: "supplier_gates",
            columns: new[] { "tenant_id", "legal_entity_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "bid_lines");

        migrationBuilder.DropTable(
            name: "contract_amendments");

        migrationBuilder.DropTable(
            name: "contract_obligations");

        migrationBuilder.DropTable(
            name: "contract_price_lines");

        migrationBuilder.DropTable(
            name: "evaluator_assignments");

        migrationBuilder.DropTable(
            name: "event_invitations");

        migrationBuilder.DropTable(
            name: "event_qas");

        migrationBuilder.DropTable(
            name: "score_cards");

        migrationBuilder.DropTable(
            name: "supplier_gates");

        migrationBuilder.DropTable(
            name: "contracts");

        migrationBuilder.DropTable(
            name: "bids");

        migrationBuilder.DropTable(
            name: "eval_criteria");

        migrationBuilder.DropTable(
            name: "awards");

        migrationBuilder.DropTable(
            name: "event_lots");

        migrationBuilder.DropTable(
            name: "sourcing_events");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-006" });

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-EVAL");

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-006");

        migrationBuilder.DropColumn(
            name: "status",
            table: "suppliers");

        migrationBuilder.DropColumn(
            name: "status_reason",
            table: "suppliers");

        migrationBuilder.DropSequence(
            name: "doc_amd");

        migrationBuilder.DropSequence(
            name: "doc_awd");

        migrationBuilder.DropSequence(
            name: "doc_bid");

        migrationBuilder.DropSequence(
            name: "doc_ctr");

        migrationBuilder.DropSequence(
            name: "doc_ev");

        migrationBuilder.DropSequence(
            name: "doc_lot");

        migrationBuilder.DropSequence(
            name: "doc_obl");

        migrationBuilder.DropSequence(
            name: "doc_qa");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-PROC",
            column: "permissions",
            value: new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer" });
    }
}
