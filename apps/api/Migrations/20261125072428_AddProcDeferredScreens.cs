using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261125072428_AddProcDeferredScreens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_asn");

        migrationBuilder.CreateSequence(
            name: "doc_bko");

        migrationBuilder.CreateSequence(
            name: "doc_blr");

        migrationBuilder.CreateSequence(
            name: "doc_cat");

        migrationBuilder.CreateSequence(
            name: "doc_clm");

        migrationBuilder.CreateSequence(
            name: "doc_evd");

        migrationBuilder.CreateSequence(
            name: "doc_ins");

        migrationBuilder.CreateSequence(
            name: "doc_obs");

        migrationBuilder.CreateSequence(
            name: "doc_qnr");

        migrationBuilder.CreateSequence(
            name: "doc_req");

        migrationBuilder.CreateSequence(
            name: "doc_rql");

        migrationBuilder.CreateSequence(
            name: "doc_rtn");

        migrationBuilder.CreateSequence(
            name: "doc_uom");

        migrationBuilder.AddColumn<int>(
            name: "version",
            table: "purchase_orders",
            type: "integer",
            nullable: false,
            defaultValue: 1); // ruling R: model default is 1 — pre-migration PO rows at v0 would fail ack validation (1..Version)

        migrationBuilder.CreateTable(
            name: "advance_ship_notices",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                po_id = table.Column<string>(type: "text", nullable: false),
                order_version = table.Column<int>(type: "integer", nullable: false),
                shipment_ref = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                received_by = table.Column<string>(type: "text", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_advance_ship_notices", x => x.id);
                table.ForeignKey(
                    name: "FK_advance_ship_notices_purchase_orders_po_id",
                    column: x => x.po_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "approval_delegations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                delegator_id = table.Column<string>(type: "text", nullable: false),
                delegate_id = table.Column<string>(type: "text", nullable: false),
                from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                max_amount = table.Column<decimal>(type: "numeric", nullable: true),
                category_id = table.Column<string>(type: "text", nullable: true),
                reason = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_approval_delegations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "approval_rules",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                approver_role = table.Column<string>(type: "text", nullable: false),
                max_amount = table.Column<decimal>(type: "numeric", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_approval_rules", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "approval_steps",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                requisition_id = table.Column<string>(type: "text", nullable: false),
                step_no = table.Column<int>(type: "integer", nullable: false),
                approver_role = table.Column<string>(type: "text", nullable: false),
                decision = table.Column<int>(type: "integer", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false),
                decided_by = table.Column<string>(type: "text", nullable: true),
                on_behalf_of_delegator_id = table.Column<string>(type: "text", nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_approval_steps", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "blanket_orders",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                ceiling_amount = table.Column<decimal>(type: "numeric", nullable: false),
                released_amount = table.Column<decimal>(type: "numeric", nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_blanket_orders", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "blanket_releases",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                blanket_order_id = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                po_id = table.Column<string>(type: "text", nullable: true),
                released_by = table.Column<string>(type: "text", nullable: false),
                released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_blanket_releases", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "budget_reservations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                txn_id = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_budget_reservations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "corrective_actions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                scorecard_id = table.Column<string>(type: "text", nullable: false),
                finding = table.Column<string>(type: "text", nullable: false),
                action = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                outcome = table.Column<string>(type: "text", nullable: true),
                closed_by = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_corrective_actions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "dispatches",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                po_id = table.Column<string>(type: "text", nullable: false),
                order_version = table.Column<int>(type: "integer", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                detail = table.Column<string>(type: "text", nullable: true),
                sent_by = table.Column<string>(type: "text", nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dispatches", x => x.id);
                table.ForeignKey(
                    name: "FK_dispatches_purchase_orders_po_id",
                    column: x => x.po_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "procurement_categories",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                parent_id = table.Column<string>(type: "text", nullable: true),
                name = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                external_code = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_procurement_categories", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "purchase_order_acknowledgements",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                po_id = table.Column<string>(type: "text", nullable: false),
                order_version = table.Column<int>(type: "integer", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                lines_json = table.Column<string>(type: "text", nullable: false),
                has_discrepancy = table.Column<bool>(type: "boolean", nullable: false),
                received_by = table.Column<string>(type: "text", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_order_acknowledgements", x => x.id);
                table.ForeignKey(
                    name: "FK_purchase_order_acknowledgements_purchase_orders_po_id",
                    column: x => x.po_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_order_changes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                po_id = table.Column<string>(type: "text", nullable: false),
                change_no = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                before_json = table.Column<string>(type: "text", nullable: false),
                after_json = table.Column<string>(type: "text", nullable: false),
                rationale = table.Column<string>(type: "text", nullable: false),
                is_material = table.Column<bool>(type: "boolean", nullable: false),
                base_order_version = table.Column<int>(type: "integer", nullable: false),
                requested_by = table.Column<string>(type: "text", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_purchase_order_changes", x => x.id);
                table.ForeignKey(
                    name: "FK_purchase_order_changes_purchase_orders_po_id",
                    column: x => x.po_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "questionnaire_responses",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                questionnaire_id = table.Column<string>(type: "text", nullable: false),
                questionnaire_version = table.Column<int>(type: "integer", nullable: false),
                answers_json = table.Column<string>(type: "text", nullable: false),
                field_comments_json = table.Column<string>(type: "text", nullable: true),
                review_decision = table.Column<string>(type: "text", nullable: true),
                reviewed_by = table.Column<string>(type: "text", nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_questionnaire_responses", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "receipt_inspections",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                receipt_id = table.Column<string>(type: "text", nullable: false),
                po_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty_received = table.Column<decimal>(type: "numeric", nullable: false),
                qty_accepted = table.Column<decimal>(type: "numeric", nullable: false),
                qty_rejected = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                defect_code = table.Column<string>(type: "text", nullable: true),
                evidence_ref = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                dispositioned_by = table.Column<string>(type: "text", nullable: true),
                dispositioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_receipt_inspections", x => x.id);
                table.ForeignKey(
                    name: "FK_receipt_inspections_receipts_receipt_id",
                    column: x => x.receipt_id,
                    principalTable: "receipts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "requisition_decisions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                requisition_id = table.Column<string>(type: "text", nullable: false),
                step_id = table.Column<string>(type: "text", nullable: false),
                decision = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "text", nullable: true),
                actor_id = table.Column<string>(type: "text", nullable: false),
                on_behalf_of_delegator_id = table.Column<string>(type: "text", nullable: true),
                step_version = table.Column<int>(type: "integer", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_requisition_decisions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "requisitions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                purpose = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                submitted_by = table.Column<string>(type: "text", nullable: true),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rule_id = table.Column<string>(type: "text", nullable: true),
                rule_version = table.Column<int>(type: "integer", nullable: true),
                approver_role = table.Column<string>(type: "text", nullable: true),
                currency = table.Column<string>(type: "text", nullable: true),
                exception_code = table.Column<string>(type: "text", nullable: true),
                exception_narrative = table.Column<string>(type: "text", nullable: true),
                exception_evidence_ref = table.Column<string>(type: "text", nullable: true),
                exception_flagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_requisitions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "risk_profiles",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                score = table.Column<decimal>(type: "numeric", nullable: false),
                band = table.Column<string>(type: "text", nullable: false),
                sources_json = table.Column<string>(type: "text", nullable: false),
                rule_version = table.Column<int>(type: "integer", nullable: false),
                computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                disposition = table.Column<string>(type: "text", nullable: false),
                stale = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_risk_profiles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "screening_results",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                outcome = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                source_ref = table.Column<string>(type: "text", nullable: true),
                disposition = table.Column<string>(type: "text", nullable: true),
                disposition_by = table.Column<string>(type: "text", nullable: true),
                disposition_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_screening_results", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "supplier_claims",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                receipt_id = table.Column<string>(type: "text", nullable: true),
                po_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                outcome_code = table.Column<string>(type: "text", nullable: true),
                reconciliation_status = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                closed_by = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_claims", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "supplier_evidences",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                evidence_type = table.Column<string>(type: "text", nullable: false),
                metadata_json = table.Column<string>(type: "text", nullable: false),
                expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                supersedes_id = table.Column<string>(type: "text", nullable: true),
                verified_by = table.Column<string>(type: "text", nullable: true),
                verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_evidences", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "supplier_observations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                metric = table.Column<string>(type: "text", nullable: false),
                value = table.Column<decimal>(type: "numeric", nullable: false),
                period = table.Column<string>(type: "text", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                dispute_reason = table.Column<string>(type: "text", nullable: true),
                amended_value = table.Column<decimal>(type: "numeric", nullable: true),
                disputed_by = table.Column<string>(type: "text", nullable: true),
                disputed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_observations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "supplier_questionnaires",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                questions_json = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_questionnaires", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "supplier_returns",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                receipt_id = table.Column<string>(type: "text", nullable: false),
                po_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_returns", x => x.id);
                table.ForeignKey(
                    name: "FK_supplier_returns_receipts_receipt_id",
                    column: x => x.receipt_id,
                    principalTable: "receipts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_scorecards",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: false),
                period_from = table.Column<string>(type: "text", nullable: false),
                period_to = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                metric_summary_json = table.Column<string>(type: "text", nullable: true),
                calc_version = table.Column<int>(type: "integer", nullable: false),
                supplier_response = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_scorecards", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "unit_of_measures",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                dimension = table.Column<int>(type: "integer", nullable: false),
                precision = table.Column<int>(type: "integer", nullable: false),
                is_base = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_unit_of_measures", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "uom_conversions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                from_code = table.Column<string>(type: "text", nullable: false),
                to_code = table.Column<string>(type: "text", nullable: false),
                factor = table.Column<decimal>(type: "numeric", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_uom_conversions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "advance_ship_notice_lines",
            columns: table => new
            {
                asn_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                id = table.Column<string>(type: "text", nullable: true),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                quarantined = table.Column<bool>(type: "boolean", nullable: false),
                quarantine_reason = table.Column<string>(type: "text", nullable: true),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_advance_ship_notice_lines", x => new { x.asn_id, x.line_no });
                table.ForeignKey(
                    name: "FK_advance_ship_notice_lines_advance_ship_notices_asn_id",
                    column: x => x.asn_id,
                    principalTable: "advance_ship_notices",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "requisition_lines",
            columns: table => new
            {
                requisition_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                id = table.Column<string>(type: "text", nullable: true),
                category_id = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
                unit = table.Column<string>(type: "text", nullable: false),
                need_by = table.Column<DateOnly>(type: "date", nullable: false),
                base_qty = table.Column<decimal>(type: "numeric", nullable: false),
                base_unit = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_requisition_lines", x => new { x.requisition_id, x.line_no });
                table.ForeignKey(
                    name: "FK_requisition_lines_requisitions_requisition_id",
                    column: x => x.requisition_id,
                    principalTable: "requisitions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "approval_rules",
            columns: new[] { "id", "approver_role", "created_at", "created_by", "is_active", "legal_entity_id", "max_amount", "name", "tenant_id", "version" },
            values: new object[] { "AR-REQ-DEFAULT", "R-FIN", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, "LE-001", null, "requisition_default", "T-001", 1 });

        migrationBuilder.InsertData(
            table: "finance_settings",
            columns: new[] { "id", "key", "legal_entity_id", "tenant_id", "value" },
            values: new object[] { "FS-budget-mode", "budget.check_mode", "LE-001", "T-001", "Pass" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write", "requisitions:approve", "requisitions:read", "requisitions:override", "suppliers:review", "analytics:read" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-PROC",
            column: "permissions",
            value: new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read", "requisitions:write", "requisitions:read", "catalog:write", "receipts:inspect", "analytics:read" });

        migrationBuilder.InsertData(
            table: "roles",
            columns: new[] { "id", "legal_entity_id", "name", "permissions", "tenant_id" },
            values: new object[,]
            {
                { "R-PROC2", "LE-001", "procurement_officer_2", new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read", "requisitions:write", "requisitions:read", "catalog:write", "receipts:inspect", "analytics:read" }, "T-001" },
                { "R-QC", "LE-001", "quality_officer", new[] { "receipts:read", "receipts:inspect", "receipts:dispose", "suppliers:review", "analytics:read" }, "T-001" }
            });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[,]
            {
                { "U-009", true, "Procure Officer 2", "procurve2@teamzlab.dev", "LE-001", "T-001" },
                { "U-010", true, "Quality Officer", "quality@teamzlab.dev", "LE-001", "T-001" }
            });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[,]
            {
                { "LE-001", "T-001", "U-009", "R-PROC2" },
                { "LE-001", "T-001", "U-010", "R-QC" }
            });

        migrationBuilder.CreateIndex(
            name: "IX_advance_ship_notice_lines_asn_id",
            table: "advance_ship_notice_lines",
            column: "asn_id");

        migrationBuilder.CreateIndex(
            name: "IX_advance_ship_notice_lines_tenant_id_legal_entity_id",
            table: "advance_ship_notice_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_advance_ship_notices_po_id",
            table: "advance_ship_notices",
            column: "po_id");

        migrationBuilder.CreateIndex(
            name: "IX_advance_ship_notices_tenant_id_legal_entity_id_po_id",
            table: "advance_ship_notices",
            columns: new[] { "tenant_id", "legal_entity_id", "po_id" });

        migrationBuilder.CreateIndex(
            name: "IX_advance_ship_notices_tenant_id_legal_entity_id_status",
            table: "advance_ship_notices",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_delegations_tenant_id_legal_entity_id_delegate_id",
            table: "approval_delegations",
            columns: new[] { "tenant_id", "legal_entity_id", "delegate_id" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_delegations_tenant_id_legal_entity_id_delegator_id",
            table: "approval_delegations",
            columns: new[] { "tenant_id", "legal_entity_id", "delegator_id" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_rules_tenant_id_legal_entity_id_is_active",
            table: "approval_rules",
            columns: new[] { "tenant_id", "legal_entity_id", "is_active" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_steps_requisition_id",
            table: "approval_steps",
            column: "requisition_id");

        migrationBuilder.CreateIndex(
            name: "IX_approval_steps_tenant_id_legal_entity_id_requisition_id",
            table: "approval_steps",
            columns: new[] { "tenant_id", "legal_entity_id", "requisition_id" });

        migrationBuilder.CreateIndex(
            name: "IX_blanket_orders_tenant_id_legal_entity_id_status",
            table: "blanket_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_blanket_orders_tenant_id_legal_entity_id_supplier_id",
            table: "blanket_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "IX_blanket_releases_blanket_order_id",
            table: "blanket_releases",
            column: "blanket_order_id");

        migrationBuilder.CreateIndex(
            name: "IX_blanket_releases_tenant_id_legal_entity_id_blanket_order_id",
            table: "blanket_releases",
            columns: new[] { "tenant_id", "legal_entity_id", "blanket_order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_budget_reservations_tenant_id_legal_entity_id_txn_id",
            table: "budget_reservations",
            columns: new[] { "tenant_id", "legal_entity_id", "txn_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_corrective_actions_scorecard_id",
            table: "corrective_actions",
            column: "scorecard_id");

        migrationBuilder.CreateIndex(
            name: "IX_corrective_actions_tenant_id_legal_entity_id_scorecard_id",
            table: "corrective_actions",
            columns: new[] { "tenant_id", "legal_entity_id", "scorecard_id" });

        migrationBuilder.CreateIndex(
            name: "IX_dispatches_po_id",
            table: "dispatches",
            column: "po_id");

        migrationBuilder.CreateIndex(
            name: "IX_dispatches_tenant_id_legal_entity_id_po_id",
            table: "dispatches",
            columns: new[] { "tenant_id", "legal_entity_id", "po_id" });

        migrationBuilder.CreateIndex(
            name: "IX_procurement_categories_tenant_id_legal_entity_id_parent_id",
            table: "procurement_categories",
            columns: new[] { "tenant_id", "legal_entity_id", "parent_id" });

        migrationBuilder.CreateIndex(
            name: "IX_procurement_categories_tenant_id_legal_entity_id_status",
            table: "procurement_categories",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_purchase_order_acknowledgements_po_id",
            table: "purchase_order_acknowledgements",
            column: "po_id");

        migrationBuilder.CreateIndex(
            name: "IX_purchase_order_acknowledgements_tenant_id_legal_entity_id_p~",
            table: "purchase_order_acknowledgements",
            columns: new[] { "tenant_id", "legal_entity_id", "po_id" });

        migrationBuilder.CreateIndex(
            name: "IX_purchase_order_changes_po_id",
            table: "purchase_order_changes",
            column: "po_id");

        migrationBuilder.CreateIndex(
            name: "IX_purchase_order_changes_tenant_id_legal_entity_id_po_id",
            table: "purchase_order_changes",
            columns: new[] { "tenant_id", "legal_entity_id", "po_id" });

        migrationBuilder.CreateIndex(
            name: "IX_questionnaire_responses_questionnaire_id",
            table: "questionnaire_responses",
            column: "questionnaire_id");

        migrationBuilder.CreateIndex(
            name: "IX_questionnaire_responses_supplier_id",
            table: "questionnaire_responses",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_questionnaire_responses_tenant_id_legal_entity_id_supplier_~",
            table: "questionnaire_responses",
            columns: new[] { "tenant_id", "legal_entity_id", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "IX_receipt_inspections_po_id",
            table: "receipt_inspections",
            column: "po_id");

        migrationBuilder.CreateIndex(
            name: "IX_receipt_inspections_receipt_id",
            table: "receipt_inspections",
            column: "receipt_id");

        migrationBuilder.CreateIndex(
            name: "IX_receipt_inspections_tenant_id_legal_entity_id_receipt_id",
            table: "receipt_inspections",
            columns: new[] { "tenant_id", "legal_entity_id", "receipt_id" });

        migrationBuilder.CreateIndex(
            name: "IX_requisition_decisions_tenant_id_legal_entity_id_requisition~",
            table: "requisition_decisions",
            columns: new[] { "tenant_id", "legal_entity_id", "requisition_id" });

        migrationBuilder.CreateIndex(
            name: "IX_requisition_lines_category_id",
            table: "requisition_lines",
            column: "category_id");

        migrationBuilder.CreateIndex(
            name: "IX_requisition_lines_requisition_id",
            table: "requisition_lines",
            column: "requisition_id");

        migrationBuilder.CreateIndex(
            name: "IX_requisition_lines_tenant_id_legal_entity_id",
            table: "requisition_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_requisitions_created_by",
            table: "requisitions",
            column: "created_by");

        migrationBuilder.CreateIndex(
            name: "IX_requisitions_tenant_id_legal_entity_id",
            table: "requisitions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_requisitions_tenant_id_legal_entity_id_status",
            table: "requisitions",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_risk_profiles_tenant_id_legal_entity_id_supplier_id",
            table: "risk_profiles",
            columns: new[] { "tenant_id", "legal_entity_id", "supplier_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_screening_results_supplier_id",
            table: "screening_results",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_screening_results_tenant_id_legal_entity_id_status",
            table: "screening_results",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_screening_results_tenant_id_legal_entity_id_supplier_id",
            table: "screening_results",
            columns: new[] { "tenant_id", "legal_entity_id", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_claims_tenant_id_legal_entity_id_po_id",
            table: "supplier_claims",
            columns: new[] { "tenant_id", "legal_entity_id", "po_id" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_claims_tenant_id_legal_entity_id_status",
            table: "supplier_claims",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_evidences_supplier_id",
            table: "supplier_evidences",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_evidences_tenant_id_legal_entity_id_supplier_id",
            table: "supplier_evidences",
            columns: new[] { "tenant_id", "legal_entity_id", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_observations_supplier_id",
            table: "supplier_observations",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_observations_tenant_id_legal_entity_id_supplier_id",
            table: "supplier_observations",
            columns: new[] { "tenant_id", "legal_entity_id", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_questionnaires_tenant_id_legal_entity_id_name",
            table: "supplier_questionnaires",
            columns: new[] { "tenant_id", "legal_entity_id", "name" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_returns_receipt_id",
            table: "supplier_returns",
            column: "receipt_id");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_returns_tenant_id_legal_entity_id_po_id",
            table: "supplier_returns",
            columns: new[] { "tenant_id", "legal_entity_id", "po_id" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_returns_tenant_id_legal_entity_id_receipt_id",
            table: "supplier_returns",
            columns: new[] { "tenant_id", "legal_entity_id", "receipt_id" });

        migrationBuilder.CreateIndex(
            name: "IX_supplier_scorecards_supplier_id",
            table: "supplier_scorecards",
            column: "supplier_id");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_scorecards_tenant_id_legal_entity_id_supplier_id",
            table: "supplier_scorecards",
            columns: new[] { "tenant_id", "legal_entity_id", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "IX_unit_of_measures_tenant_id_legal_entity_id_code",
            table: "unit_of_measures",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_uom_conversions_tenant_id_legal_entity_id_from_code",
            table: "uom_conversions",
            columns: new[] { "tenant_id", "legal_entity_id", "from_code" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "advance_ship_notice_lines");

        migrationBuilder.DropTable(
            name: "approval_delegations");

        migrationBuilder.DropTable(
            name: "approval_rules");

        migrationBuilder.DropTable(
            name: "approval_steps");

        migrationBuilder.DropTable(
            name: "blanket_orders");

        migrationBuilder.DropTable(
            name: "blanket_releases");

        migrationBuilder.DropTable(
            name: "budget_reservations");

        migrationBuilder.DropTable(
            name: "corrective_actions");

        migrationBuilder.DropTable(
            name: "dispatches");

        migrationBuilder.DropTable(
            name: "procurement_categories");

        migrationBuilder.DropTable(
            name: "purchase_order_acknowledgements");

        migrationBuilder.DropTable(
            name: "purchase_order_changes");

        migrationBuilder.DropTable(
            name: "questionnaire_responses");

        migrationBuilder.DropTable(
            name: "receipt_inspections");

        migrationBuilder.DropTable(
            name: "requisition_decisions");

        migrationBuilder.DropTable(
            name: "requisition_lines");

        migrationBuilder.DropTable(
            name: "risk_profiles");

        migrationBuilder.DropTable(
            name: "screening_results");

        migrationBuilder.DropTable(
            name: "supplier_claims");

        migrationBuilder.DropTable(
            name: "supplier_evidences");

        migrationBuilder.DropTable(
            name: "supplier_observations");

        migrationBuilder.DropTable(
            name: "supplier_questionnaires");

        migrationBuilder.DropTable(
            name: "supplier_returns");

        migrationBuilder.DropTable(
            name: "supplier_scorecards");

        migrationBuilder.DropTable(
            name: "unit_of_measures");

        migrationBuilder.DropTable(
            name: "uom_conversions");

        migrationBuilder.DropTable(
            name: "advance_ship_notices");

        migrationBuilder.DropTable(
            name: "requisitions");

        migrationBuilder.DeleteData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-budget-mode");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-009" });

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-010" });

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-PROC2");

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-QC");

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-009");

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-010");

        migrationBuilder.DropColumn(
            name: "version",
            table: "purchase_orders");

        migrationBuilder.DropSequence(
            name: "doc_asn");

        migrationBuilder.DropSequence(
            name: "doc_bko");

        migrationBuilder.DropSequence(
            name: "doc_blr");

        migrationBuilder.DropSequence(
            name: "doc_cat");

        migrationBuilder.DropSequence(
            name: "doc_clm");

        migrationBuilder.DropSequence(
            name: "doc_evd");

        migrationBuilder.DropSequence(
            name: "doc_ins");

        migrationBuilder.DropSequence(
            name: "doc_obs");

        migrationBuilder.DropSequence(
            name: "doc_qnr");

        migrationBuilder.DropSequence(
            name: "doc_req");

        migrationBuilder.DropSequence(
            name: "doc_rql");

        migrationBuilder.DropSequence(
            name: "doc_rtn");

        migrationBuilder.DropSequence(
            name: "doc_uom");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-PROC",
            column: "permissions",
            value: new[] { "suppliers:write", "po:write", "receipts:write", "inventory:write", "inventory:transfer", "sourcing:read", "sourcing:write", "contracts:read", "contracts:write", "suppliers:read" });
    }
}
