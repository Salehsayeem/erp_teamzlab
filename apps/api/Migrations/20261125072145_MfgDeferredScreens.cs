using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261125072145_MfgDeferredScreens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_cfl");

        migrationBuilder.CreateSequence(
            name: "doc_cme");

        migrationBuilder.CreateSequence(
            name: "doc_cpa");

        migrationBuilder.CreateSequence(
            name: "doc_dwn");

        migrationBuilder.CreateSequence(
            name: "doc_lbt");

        migrationBuilder.CreateSequence(
            name: "doc_mav");

        migrationBuilder.CreateSequence(
            name: "doc_mcr");

        migrationBuilder.CreateSequence(
            name: "doc_mrp");

        migrationBuilder.CreateSequence(
            name: "doc_ncr");

        migrationBuilder.CreateSequence(
            name: "doc_qhd");

        migrationBuilder.CreateSequence(
            name: "doc_qin");

        migrationBuilder.CreateSequence(
            name: "doc_qpl");

        migrationBuilder.CreateSequence(
            name: "doc_rot");

        migrationBuilder.CreateSequence(
            name: "doc_rwk");

        migrationBuilder.CreateSequence(
            name: "doc_scn");

        migrationBuilder.CreateSequence(
            name: "doc_srn");

        migrationBuilder.CreateSequence(
            name: "doc_sub");

        migrationBuilder.CreateSequence(
            name: "doc_txp");

        migrationBuilder.CreateSequence(
            name: "doc_vln");

        migrationBuilder.CreateSequence(
            name: "doc_wkc");

        migrationBuilder.CreateSequence(
            name: "doc_wtr");

        migrationBuilder.AddColumn<string>(
            name: "planned_routing_revision_id",
            table: "production_orders",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "released_routing_revision_id",
            table: "production_orders",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "capa_cases",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                nonconformance_id = table.Column<string>(type: "text", nullable: true),
                effectiveness_criteria = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                closed_by = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_capa_cases", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "correction_records",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                target_type = table.Column<string>(type: "text", nullable: false),
                target_id = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                compensating_json = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                proposed_by = table.Column<string>(type: "text", nullable: false),
                proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_correction_records", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "counter_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                source_event_id = table.Column<string>(type: "text", nullable: false),
                counter_value = table.Column<decimal>(type: "numeric", nullable: false),
                delta_qty = table.Column<decimal>(type: "numeric", nullable: false),
                rollover = table.Column<bool>(type: "boolean", nullable: false),
                correction_of_id = table.Column<string>(type: "text", nullable: true),
                reported_by = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_counter_events", x => x.id);
                table.ForeignKey(
                    name: "FK_counter_events_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "downtime_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                work_center_id = table.Column<string>(type: "text", nullable: false),
                start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason_code = table.Column<string>(type: "text", nullable: false),
                correction_of_id = table.Column<string>(type: "text", nullable: true),
                reported_by = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_downtime_events", x => x.id);
                table.ForeignKey(
                    name: "FK_downtime_events_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inspections",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                quality_plan_id = table.Column<string>(type: "text", nullable: false),
                plan_revision = table.Column<int>(type: "integer", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                lot_serial = table.Column<string>(type: "text", nullable: true),
                sample_size = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                inspector_id = table.Column<string>(type: "text", nullable: false),
                finalized_by = table.Column<string>(type: "text", nullable: true),
                finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inspections", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "labor_time_entries",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                worker_employment_id = table.Column<string>(type: "text", nullable: false),
                start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                minutes = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                correction_of_id = table.Column<string>(type: "text", nullable: true),
                reported_by = table.Column<string>(type: "text", nullable: false),
                reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_labor_time_entries", x => x.id);
                table.ForeignKey(
                    name: "FK_labor_time_entries_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "maintenance_availability_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                source_event_id = table.Column<string>(type: "text", nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                resource_code = table.Column<string>(type: "text", nullable: false),
                asset_id = table.Column<string>(type: "text", nullable: true),
                window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason = table.Column<string>(type: "text", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                exception_reason = table.Column<string>(type: "text", nullable: true),
                ingested_by = table.Column<string>(type: "text", nullable: false),
                ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_maintenance_availability_events", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "mrp_runs",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                horizon_from = table.Column<DateOnly>(type: "date", nullable: false),
                horizon_to = table.Column<DateOnly>(type: "date", nullable: false),
                parameters_json = table.Column<string>(type: "text", nullable: false),
                snapshot_json = table.Column<string>(type: "text", nullable: false),
                failure_detail = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mrp_runs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "nonconformances",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                source_event_id = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: true),
                order_id = table.Column<string>(type: "text", nullable: true),
                affected_qty = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                closed_by = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                raised_by = table.Column<string>(type: "text", nullable: false),
                raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_nonconformances", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "quality_holds",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                lot_serial = table.Column<string>(type: "text", nullable: true),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: true),
                inspection_id = table.Column<string>(type: "text", nullable: true),
                nonconformance_id = table.Column<string>(type: "text", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                disposition = table.Column<int>(type: "integer", nullable: true),
                dispositioned_qty = table.Column<decimal>(type: "numeric", nullable: true),
                journal_id = table.Column<string>(type: "text", nullable: true),
                held_by = table.Column<string>(type: "text", nullable: false),
                held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                dispositioned_by = table.Column<string>(type: "text", nullable: true),
                dispositioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_holds", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "quality_plans",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: true),
                process_stage = table.Column<string>(type: "text", nullable: true),
                revision = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_plans", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "rework_orders",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                nonconformance_id = table.Column<string>(type: "text", nullable: true),
                hold_id = table.Column<string>(type: "text", nullable: true),
                source_order_id = table.Column<string>(type: "text", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                adjustment_reason = table.Column<string>(type: "text", nullable: true),
                adjustment_approved_by = table.Column<string>(type: "text", nullable: true),
                reinspection_inspection_id = table.Column<string>(type: "text", nullable: true),
                completed_by = table.Column<string>(type: "text", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rework_orders", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "routing_revisions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                output_sku = table.Column<string>(type: "text", nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                approved_by = table.Column<string>(type: "text", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_routing_revisions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "schedule_runs",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                mode = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                horizon_from = table.Column<DateOnly>(type: "date", nullable: false),
                horizon_to = table.Column<DateOnly>(type: "date", nullable: false),
                calendar_version = table.Column<string>(type: "text", nullable: false),
                algorithm_version = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_runs", x => x.id);
                table.ForeignKey(
                    name: "FK_schedule_runs_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "subcontract_custodies",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                routing_operation_seq = table.Column<int>(type: "integer", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                inventory_movement_id = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subcontract_custodies", x => x.id);
                table.ForeignKey(
                    name: "FK_subcontract_custodies_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "subcontract_receipts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                routing_operation_seq = table.Column<int>(type: "integer", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                accepted_qty = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                quality_hold_id = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subcontract_receipts", x => x.id);
                table.ForeignKey(
                    name: "FK_subcontract_receipts_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "transformation_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                completion_id = table.Column<string>(type: "text", nullable: false),
                output_sku = table.Column<string>(type: "text", nullable: false),
                output_qty = table.Column<decimal>(type: "numeric", nullable: false),
                lot_serial = table.Column<string>(type: "text", nullable: true),
                inputs_json = table.Column<string>(type: "text", nullable: false),
                emitted_by = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transformation_events", x => x.id);
                table.ForeignKey(
                    name: "FK_transformation_events_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "variance_ledger",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                bucket_code = table.Column<string>(type: "text", nullable: false),
                qty_variance = table.Column<decimal>(type: "numeric", nullable: false),
                owner = table.Column<string>(type: "text", nullable: false),
                disposition = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: true),
                is_critical = table.Column<bool>(type: "boolean", nullable: false),
                decided_by = table.Column<string>(type: "text", nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_variance_ledger", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "wip_transfers",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                from_seq = table.Column<int>(type: "integer", nullable: false),
                to_seq = table.Column<int>(type: "integer", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                deviation_reason = table.Column<string>(type: "text", nullable: true),
                deviation_approved_by = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_wip_transfers", x => x.id);
                table.ForeignKey(
                    name: "FK_wip_transfers_production_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "production_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "work_centers",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                capabilities = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_centers", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "capa_actions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                capa_case_id = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                due_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                done = table.Column<bool>(type: "boolean", nullable: false),
                completed_by = table.Column<string>(type: "text", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_capa_actions", x => x.id);
                table.ForeignKey(
                    name: "FK_capa_actions_capa_cases_capa_case_id",
                    column: x => x.capa_case_id,
                    principalTable: "capa_cases",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "capa_verifications",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                capa_case_id = table.Column<string>(type: "text", nullable: false),
                criteria_result = table.Column<string>(type: "text", nullable: false),
                satisfied = table.Column<bool>(type: "boolean", nullable: false),
                verified_by = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_capa_verifications", x => x.id);
                table.ForeignKey(
                    name: "FK_capa_verifications_capa_cases_capa_case_id",
                    column: x => x.capa_case_id,
                    principalTable: "capa_cases",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inspection_results",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                inspection_id = table.Column<string>(type: "text", nullable: false),
                characteristic_code = table.Column<string>(type: "text", nullable: false),
                raw_value = table.Column<string>(type: "text", nullable: false),
                evaluated_pass = table.Column<bool>(type: "boolean", nullable: true),
                rule_version = table.Column<int>(type: "integer", nullable: false),
                amendment_no = table.Column<int>(type: "integer", nullable: false),
                amends_id = table.Column<string>(type: "text", nullable: true),
                entered_by = table.Column<string>(type: "text", nullable: false),
                entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inspection_results", x => x.id);
                table.ForeignKey(
                    name: "FK_inspection_results_inspections_inspection_id",
                    column: x => x.inspection_id,
                    principalTable: "inspections",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "maintenance_impacts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                outage_event_id = table.Column<string>(type: "text", nullable: false),
                schedule_run_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                work_center_id = table.Column<string>(type: "text", nullable: false),
                overlap_minutes = table.Column<int>(type: "integer", nullable: false),
                disposition = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: true),
                decided_by = table.Column<string>(type: "text", nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_maintenance_impacts", x => x.id);
                table.ForeignKey(
                    name: "FK_maintenance_impacts_maintenance_availability_events_outage_~",
                    column: x => x.outage_event_id,
                    principalTable: "maintenance_availability_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "action_messages",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                run_id = table.Column<string>(type: "text", nullable: false),
                mrp_line_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                message = table.Column<string>(type: "text", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                dismiss_reason = table.Column<string>(type: "text", nullable: true),
                acknowledged_by = table.Column<string>(type: "text", nullable: true),
                acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_action_messages", x => x.id);
                table.ForeignKey(
                    name: "FK_action_messages_mrp_runs_run_id",
                    column: x => x.run_id,
                    principalTable: "mrp_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "mrp_lines",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                run_id = table.Column<string>(type: "text", nullable: false),
                line_no = table.Column<int>(type: "integer", nullable: false),
                item_sku = table.Column<string>(type: "text", nullable: false),
                explosion_level = table.Column<int>(type: "integer", nullable: false),
                parent_path = table.Column<string>(type: "text", nullable: false),
                gross_qty = table.Column<decimal>(type: "numeric", nullable: false),
                supply_qty = table.Column<decimal>(type: "numeric", nullable: false),
                net_qty = table.Column<decimal>(type: "numeric", nullable: false),
                modifiers_json = table.Column<string>(type: "text", nullable: false),
                required_by = table.Column<DateOnly>(type: "date", nullable: true),
                lead_time_days = table.Column<int>(type: "integer", nullable: true),
                exception_code = table.Column<string>(type: "text", nullable: true),
                conversion_status = table.Column<string>(type: "text", nullable: false),
                converted_order_id = table.Column<string>(type: "text", nullable: true),
                converted_requisition_ref = table.Column<string>(type: "text", nullable: true),
                decision_reason = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mrp_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_mrp_lines_mrp_runs_run_id",
                    column: x => x.run_id,
                    principalTable: "mrp_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "mrp_publications",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                run_id = table.Column<string>(type: "text", nullable: false),
                reviewer_id = table.Column<string>(type: "text", nullable: false),
                overrode_stale_sources = table.Column<bool>(type: "boolean", nullable: false),
                override_reason = table.Column<string>(type: "text", nullable: true),
                exception_disposition = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mrp_publications", x => x.id);
                table.ForeignKey(
                    name: "FK_mrp_publications_mrp_runs_run_id",
                    column: x => x.run_id,
                    principalTable: "mrp_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "containment_actions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                nonconformance_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                opened_by = table.Column<string>(type: "text", nullable: false),
                released_by = table.Column<string>(type: "text", nullable: true),
                released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                release_reason = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_containment_actions", x => x.id);
                table.ForeignKey(
                    name: "FK_containment_actions_nonconformances_nonconformance_id",
                    column: x => x.nonconformance_id,
                    principalTable: "nonconformances",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "nc_objects",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                nonconformance_id = table.Column<string>(type: "text", nullable: false),
                object_type = table.Column<string>(type: "text", nullable: false),
                object_ref = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_nc_objects", x => x.id);
                table.ForeignKey(
                    name: "FK_nc_objects_nonconformances_nonconformance_id",
                    column: x => x.nonconformance_id,
                    principalTable: "nonconformances",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "quality_characteristics",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                plan_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                data_type = table.Column<int>(type: "integer", nullable: false),
                min_value = table.Column<decimal>(type: "numeric", nullable: true),
                max_value = table.Column<decimal>(type: "numeric", nullable: true),
                target_value = table.Column<decimal>(type: "numeric", nullable: true),
                unit = table.Column<string>(type: "text", nullable: true),
                mandatory = table.Column<bool>(type: "boolean", nullable: false),
                rule_version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_characteristics", x => x.id);
                table.ForeignKey(
                    name: "FK_quality_characteristics_quality_plans_plan_id",
                    column: x => x.plan_id,
                    principalTable: "quality_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sampling_plans",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                quality_plan_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                sample_size = table.Column<int>(type: "integer", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sampling_plans", x => x.id);
                table.ForeignKey(
                    name: "FK_sampling_plans_quality_plans_quality_plan_id",
                    column: x => x.quality_plan_id,
                    principalTable: "quality_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "conflict_reports",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                schedule_run_id = table.Column<string>(type: "text", nullable: false),
                severity = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                detail = table.Column<string>(type: "text", nullable: false),
                resolved = table.Column<bool>(type: "boolean", nullable: false),
                resolved_by = table.Column<string>(type: "text", nullable: true),
                resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_conflict_reports", x => x.id);
                table.ForeignKey(
                    name: "FK_conflict_reports_schedule_runs_schedule_run_id",
                    column: x => x.schedule_run_id,
                    principalTable: "schedule_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "operation_placements",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                schedule_run_id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                routing_revision_id = table.Column<string>(type: "text", nullable: false),
                seq = table.Column<int>(type: "integer", nullable: false),
                work_center_id = table.Column<string>(type: "text", nullable: false),
                operation_code = table.Column<string>(type: "text", nullable: false),
                start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                scheduled = table.Column<bool>(type: "boolean", nullable: false),
                unscheduled_reason = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_operation_placements", x => x.id);
                table.ForeignKey(
                    name: "FK_operation_placements_schedule_runs_schedule_run_id",
                    column: x => x.schedule_run_id,
                    principalTable: "schedule_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "schedule_publications",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                schedule_run_id = table.Column<string>(type: "text", nullable: false),
                reviewer_id = table.Column<string>(type: "text", nullable: false),
                override_reason = table.Column<string>(type: "text", nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_publications", x => x.id);
                table.ForeignKey(
                    name: "FK_schedule_publications_schedule_runs_schedule_run_id",
                    column: x => x.schedule_run_id,
                    principalTable: "schedule_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "schedule_scenarios",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                base_run_id = table.Column<string>(type: "text", nullable: false),
                frozen_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                frozen_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_scenarios", x => x.id);
                table.ForeignKey(
                    name: "FK_schedule_scenarios_schedule_runs_base_run_id",
                    column: x => x.base_run_id,
                    principalTable: "schedule_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "resource_calendars",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                work_center_id = table.Column<string>(type: "text", nullable: false),
                window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_available = table.Column<bool>(type: "boolean", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                source_event_id = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_resource_calendars", x => x.id);
                table.ForeignKey(
                    name: "FK_resource_calendars_work_centers_work_center_id",
                    column: x => x.work_center_id,
                    principalTable: "work_centers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "routing_operations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                routing_revision_id = table.Column<string>(type: "text", nullable: false),
                seq = table.Column<int>(type: "integer", nullable: false),
                operation_code = table.Column<string>(type: "text", nullable: false),
                work_center_id = table.Column<string>(type: "text", nullable: false),
                required_capabilities = table.Column<string>(type: "text", nullable: false),
                time_unit = table.Column<string>(type: "text", nullable: false),
                setup_min = table.Column<decimal>(type: "numeric", nullable: false),
                per_unit_min = table.Column<decimal>(type: "numeric", nullable: false),
                is_zero_duration = table.Column<bool>(type: "boolean", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                supplier_id = table.Column<string>(type: "text", nullable: true),
                service_description = table.Column<string>(type: "text", nullable: true),
                sub_routing_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_routing_operations", x => x.id);
                table.ForeignKey(
                    name: "FK_routing_operations_routing_revisions_routing_revision_id",
                    column: x => x.routing_revision_id,
                    principalTable: "routing_revisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_routing_operations_work_centers_work_center_id",
                    column: x => x.work_center_id,
                    principalTable: "work_centers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "schedule_overrides",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                scenario_id = table.Column<string>(type: "text", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                approved_by = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_overrides", x => x.id);
                table.ForeignKey(
                    name: "FK_schedule_overrides_schedule_scenarios_scenario_id",
                    column: x => x.scenario_id,
                    principalTable: "schedule_scenarios",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            table: "finance_settings",
            columns: new[] { "id", "key", "legal_entity_id", "tenant_id", "value" },
            values: new object[] { "FS-mfg-downtime", "mfg.downtime_reasons", "LE-001", "T-001", "SETUP,MACHINE_DOWN,TOOL_CHANGE,NO_OPERATOR,MATERIAL_SHORT,QUALITY_STOP" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-MFG",
            column: "permissions",
            value: new[] { "boms:read", "boms:write", "boms:approve", "mfg:read", "mfg:write", "mfg:release", "mfg:override", "mfg:close", "mfg:reopen", "mfg:schedule", "mfg:mrp", "mfg:shopfloor", "mfg:rework", "quality:plan" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-012", true, "Manufacturing Planner", "mfg.planner@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-012", "R-MFG" });

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_planned_routing_revision_id",
            table: "production_orders",
            column: "planned_routing_revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_released_routing_revision_id",
            table: "production_orders",
            column: "released_routing_revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id_planned_routing~",
            table: "production_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "planned_routing_revision_id" });

        migrationBuilder.CreateIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id_released_routin~",
            table: "production_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "released_routing_revision_id" });

        migrationBuilder.CreateIndex(
            name: "IX_action_messages_run_id",
            table: "action_messages",
            column: "run_id");

        migrationBuilder.CreateIndex(
            name: "IX_action_messages_tenant_id_legal_entity_id",
            table: "action_messages",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_action_messages_tenant_id_legal_entity_id_state",
            table: "action_messages",
            columns: new[] { "tenant_id", "legal_entity_id", "state" });

        migrationBuilder.CreateIndex(
            name: "IX_capa_actions_capa_case_id",
            table: "capa_actions",
            column: "capa_case_id");

        migrationBuilder.CreateIndex(
            name: "IX_capa_actions_tenant_id_legal_entity_id",
            table: "capa_actions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_capa_cases_tenant_id_legal_entity_id",
            table: "capa_cases",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_capa_cases_tenant_id_legal_entity_id_status",
            table: "capa_cases",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_capa_verifications_capa_case_id",
            table: "capa_verifications",
            column: "capa_case_id");

        migrationBuilder.CreateIndex(
            name: "IX_capa_verifications_tenant_id_legal_entity_id",
            table: "capa_verifications",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_conflict_reports_schedule_run_id",
            table: "conflict_reports",
            column: "schedule_run_id");

        migrationBuilder.CreateIndex(
            name: "IX_conflict_reports_tenant_id_legal_entity_id",
            table: "conflict_reports",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_containment_actions_nonconformance_id",
            table: "containment_actions",
            column: "nonconformance_id");

        migrationBuilder.CreateIndex(
            name: "IX_containment_actions_tenant_id_legal_entity_id",
            table: "containment_actions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_correction_records_tenant_id_legal_entity_id",
            table: "correction_records",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_correction_records_tenant_id_legal_entity_id_target_type_ta~",
            table: "correction_records",
            columns: new[] { "tenant_id", "legal_entity_id", "target_type", "target_id" });

        migrationBuilder.CreateIndex(
            name: "IX_counter_events_order_id",
            table: "counter_events",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_counter_events_tenant_id_legal_entity_id",
            table: "counter_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_counter_events_tenant_id_legal_entity_id_order_id",
            table: "counter_events",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_counter_events_tenant_id_legal_entity_id_source_event_id",
            table: "counter_events",
            columns: new[] { "tenant_id", "legal_entity_id", "source_event_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_downtime_events_order_id",
            table: "downtime_events",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_downtime_events_tenant_id_legal_entity_id",
            table: "downtime_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_downtime_events_tenant_id_legal_entity_id_order_id",
            table: "downtime_events",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_downtime_events_tenant_id_legal_entity_id_work_center_id",
            table: "downtime_events",
            columns: new[] { "tenant_id", "legal_entity_id", "work_center_id" });

        migrationBuilder.CreateIndex(
            name: "IX_inspection_results_inspection_id",
            table: "inspection_results",
            column: "inspection_id");

        migrationBuilder.CreateIndex(
            name: "IX_inspection_results_inspection_id_characteristic_code_amendm~",
            table: "inspection_results",
            columns: new[] { "inspection_id", "characteristic_code", "amendment_no" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_inspection_results_tenant_id_legal_entity_id",
            table: "inspection_results",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_tenant_id_legal_entity_id",
            table: "inspections",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_tenant_id_legal_entity_id_order_id",
            table: "inspections",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_tenant_id_legal_entity_id_quality_plan_id",
            table: "inspections",
            columns: new[] { "tenant_id", "legal_entity_id", "quality_plan_id" });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_tenant_id_legal_entity_id_status",
            table: "inspections",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_labor_time_entries_order_id",
            table: "labor_time_entries",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_labor_time_entries_tenant_id_legal_entity_id",
            table: "labor_time_entries",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_labor_time_entries_tenant_id_legal_entity_id_order_id",
            table: "labor_time_entries",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_labor_time_entries_tenant_id_legal_entity_id_worker_employm~",
            table: "labor_time_entries",
            columns: new[] { "tenant_id", "legal_entity_id", "worker_employment_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_availability_events_tenant_id_legal_entity_id",
            table: "maintenance_availability_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_availability_events_tenant_id_legal_entity_id_~1",
            table: "maintenance_availability_events",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_availability_events_tenant_id_legal_entity_id_r~",
            table: "maintenance_availability_events",
            columns: new[] { "tenant_id", "legal_entity_id", "resource_code" });

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_availability_events_tenant_id_legal_entity_id_s~",
            table: "maintenance_availability_events",
            columns: new[] { "tenant_id", "legal_entity_id", "source_event_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_impacts_outage_event_id",
            table: "maintenance_impacts",
            column: "outage_event_id");

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_impacts_tenant_id_legal_entity_id",
            table: "maintenance_impacts",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_maintenance_impacts_tenant_id_legal_entity_id_outage_event_~",
            table: "maintenance_impacts",
            columns: new[] { "tenant_id", "legal_entity_id", "outage_event_id" });

        migrationBuilder.CreateIndex(
            name: "IX_mrp_lines_run_id",
            table: "mrp_lines",
            column: "run_id");

        migrationBuilder.CreateIndex(
            name: "IX_mrp_lines_tenant_id_legal_entity_id",
            table: "mrp_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_mrp_lines_tenant_id_legal_entity_id_converted_order_id",
            table: "mrp_lines",
            columns: new[] { "tenant_id", "legal_entity_id", "converted_order_id" },
            unique: true,
            filter: "converted_order_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_mrp_lines_tenant_id_legal_entity_id_item_sku",
            table: "mrp_lines",
            columns: new[] { "tenant_id", "legal_entity_id", "item_sku" });

        migrationBuilder.CreateIndex(
            name: "IX_mrp_lines_tenant_id_legal_entity_id_run_id",
            table: "mrp_lines",
            columns: new[] { "tenant_id", "legal_entity_id", "run_id" });

        migrationBuilder.CreateIndex(
            name: "IX_mrp_lines_tenant_id_legal_entity_id_run_id_line_no",
            table: "mrp_lines",
            columns: new[] { "tenant_id", "legal_entity_id", "run_id", "line_no" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_mrp_publications_run_id",
            table: "mrp_publications",
            column: "run_id");

        migrationBuilder.CreateIndex(
            name: "IX_mrp_publications_tenant_id_legal_entity_id_run_id",
            table: "mrp_publications",
            columns: new[] { "tenant_id", "legal_entity_id", "run_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_mrp_runs_tenant_id_legal_entity_id",
            table: "mrp_runs",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_mrp_runs_tenant_id_legal_entity_id_status",
            table: "mrp_runs",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_nc_objects_nonconformance_id",
            table: "nc_objects",
            column: "nonconformance_id");

        migrationBuilder.CreateIndex(
            name: "IX_nc_objects_tenant_id_legal_entity_id",
            table: "nc_objects",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_nonconformances_tenant_id_legal_entity_id",
            table: "nonconformances",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_nonconformances_tenant_id_legal_entity_id_status",
            table: "nonconformances",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_operation_placements_schedule_run_id",
            table: "operation_placements",
            column: "schedule_run_id");

        migrationBuilder.CreateIndex(
            name: "IX_operation_placements_tenant_id_legal_entity_id",
            table: "operation_placements",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_operation_placements_tenant_id_legal_entity_id_order_id",
            table: "operation_placements",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_characteristics_plan_id",
            table: "quality_characteristics",
            column: "plan_id");

        migrationBuilder.CreateIndex(
            name: "IX_quality_characteristics_plan_id_code",
            table: "quality_characteristics",
            columns: new[] { "plan_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_quality_characteristics_tenant_id_legal_entity_id",
            table: "quality_characteristics",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_holds_tenant_id_legal_entity_id",
            table: "quality_holds",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_holds_tenant_id_legal_entity_id_item_sku_status",
            table: "quality_holds",
            columns: new[] { "tenant_id", "legal_entity_id", "item_sku", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_holds_tenant_id_legal_entity_id_order_id",
            table: "quality_holds",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_plans_tenant_id_legal_entity_id",
            table: "quality_plans",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_plans_tenant_id_legal_entity_id_item_sku_revision",
            table: "quality_plans",
            columns: new[] { "tenant_id", "legal_entity_id", "item_sku", "revision" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_quality_plans_tenant_id_legal_entity_id_status",
            table: "quality_plans",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_resource_calendars_tenant_id_legal_entity_id",
            table: "resource_calendars",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_resource_calendars_tenant_id_legal_entity_id_source_event_id",
            table: "resource_calendars",
            columns: new[] { "tenant_id", "legal_entity_id", "source_event_id" },
            unique: true,
            filter: "source_event_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_resource_calendars_tenant_id_legal_entity_id_work_center_id~",
            table: "resource_calendars",
            columns: new[] { "tenant_id", "legal_entity_id", "work_center_id", "window_start" });

        migrationBuilder.CreateIndex(
            name: "IX_resource_calendars_work_center_id",
            table: "resource_calendars",
            column: "work_center_id");

        migrationBuilder.CreateIndex(
            name: "IX_rework_orders_tenant_id_legal_entity_id",
            table: "rework_orders",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_rework_orders_tenant_id_legal_entity_id_source_order_id",
            table: "rework_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "source_order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_rework_orders_tenant_id_legal_entity_id_status",
            table: "rework_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_routing_operations_routing_revision_id",
            table: "routing_operations",
            column: "routing_revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_routing_operations_routing_revision_id_seq",
            table: "routing_operations",
            columns: new[] { "routing_revision_id", "seq" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_routing_operations_tenant_id_legal_entity_id",
            table: "routing_operations",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_routing_operations_work_center_id",
            table: "routing_operations",
            column: "work_center_id");

        migrationBuilder.CreateIndex(
            name: "IX_routing_revisions_tenant_id_legal_entity_id",
            table: "routing_revisions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_routing_revisions_tenant_id_legal_entity_id_output_sku_revi~",
            table: "routing_revisions",
            columns: new[] { "tenant_id", "legal_entity_id", "output_sku", "revision" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_routing_revisions_tenant_id_legal_entity_id_status",
            table: "routing_revisions",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_sampling_plans_quality_plan_id",
            table: "sampling_plans",
            column: "quality_plan_id");

        migrationBuilder.CreateIndex(
            name: "IX_sampling_plans_tenant_id_legal_entity_id",
            table: "sampling_plans",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_overrides_scenario_id",
            table: "schedule_overrides",
            column: "scenario_id");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_overrides_tenant_id_legal_entity_id",
            table: "schedule_overrides",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_publications_schedule_run_id",
            table: "schedule_publications",
            column: "schedule_run_id");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_publications_tenant_id_legal_entity_id_schedule_ru~",
            table: "schedule_publications",
            columns: new[] { "tenant_id", "legal_entity_id", "schedule_run_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_schedule_runs_order_id",
            table: "schedule_runs",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_runs_tenant_id_legal_entity_id",
            table: "schedule_runs",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_runs_tenant_id_legal_entity_id_order_id",
            table: "schedule_runs",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_scenarios_base_run_id",
            table: "schedule_scenarios",
            column: "base_run_id");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_scenarios_tenant_id_legal_entity_id",
            table: "schedule_scenarios",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_subcontract_custodies_order_id",
            table: "subcontract_custodies",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_subcontract_custodies_tenant_id_legal_entity_id",
            table: "subcontract_custodies",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_subcontract_custodies_tenant_id_legal_entity_id_inventory_m~",
            table: "subcontract_custodies",
            columns: new[] { "tenant_id", "legal_entity_id", "inventory_movement_id" });

        migrationBuilder.CreateIndex(
            name: "IX_subcontract_custodies_tenant_id_legal_entity_id_order_id",
            table: "subcontract_custodies",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_subcontract_receipts_order_id",
            table: "subcontract_receipts",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_subcontract_receipts_tenant_id_legal_entity_id",
            table: "subcontract_receipts",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_subcontract_receipts_tenant_id_legal_entity_id_order_id",
            table: "subcontract_receipts",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_transformation_events_order_id",
            table: "transformation_events",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_transformation_events_tenant_id_legal_entity_id",
            table: "transformation_events",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_transformation_events_tenant_id_legal_entity_id_completion_~",
            table: "transformation_events",
            columns: new[] { "tenant_id", "legal_entity_id", "completion_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_transformation_events_tenant_id_legal_entity_id_order_id",
            table: "transformation_events",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_transformation_events_tenant_id_legal_entity_id_output_sku_~",
            table: "transformation_events",
            columns: new[] { "tenant_id", "legal_entity_id", "output_sku", "lot_serial" });

        migrationBuilder.CreateIndex(
            name: "IX_variance_ledger_tenant_id_legal_entity_id",
            table: "variance_ledger",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_variance_ledger_tenant_id_legal_entity_id_order_id",
            table: "variance_ledger",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_variance_ledger_tenant_id_legal_entity_id_order_id_disposit~",
            table: "variance_ledger",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id", "disposition" });

        migrationBuilder.CreateIndex(
            name: "IX_wip_transfers_order_id",
            table: "wip_transfers",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_wip_transfers_tenant_id_legal_entity_id",
            table: "wip_transfers",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_wip_transfers_tenant_id_legal_entity_id_order_id",
            table: "wip_transfers",
            columns: new[] { "tenant_id", "legal_entity_id", "order_id" });

        migrationBuilder.CreateIndex(
            name: "IX_work_centers_tenant_id_legal_entity_id",
            table: "work_centers",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_work_centers_tenant_id_legal_entity_id_code",
            table: "work_centers",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_production_orders_routing_revisions_planned_routing_revisio~",
            table: "production_orders",
            column: "planned_routing_revision_id",
            principalTable: "routing_revisions",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_production_orders_routing_revisions_released_routing_revisi~",
            table: "production_orders",
            column: "released_routing_revision_id",
            principalTable: "routing_revisions",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_production_orders_routing_revisions_planned_routing_revisio~",
            table: "production_orders");

        migrationBuilder.DropForeignKey(
            name: "FK_production_orders_routing_revisions_released_routing_revisi~",
            table: "production_orders");

        migrationBuilder.DropTable(
            name: "action_messages");

        migrationBuilder.DropTable(
            name: "capa_actions");

        migrationBuilder.DropTable(
            name: "capa_verifications");

        migrationBuilder.DropTable(
            name: "conflict_reports");

        migrationBuilder.DropTable(
            name: "containment_actions");

        migrationBuilder.DropTable(
            name: "correction_records");

        migrationBuilder.DropTable(
            name: "counter_events");

        migrationBuilder.DropTable(
            name: "downtime_events");

        migrationBuilder.DropTable(
            name: "inspection_results");

        migrationBuilder.DropTable(
            name: "labor_time_entries");

        migrationBuilder.DropTable(
            name: "maintenance_impacts");

        migrationBuilder.DropTable(
            name: "mrp_lines");

        migrationBuilder.DropTable(
            name: "mrp_publications");

        migrationBuilder.DropTable(
            name: "nc_objects");

        migrationBuilder.DropTable(
            name: "operation_placements");

        migrationBuilder.DropTable(
            name: "quality_characteristics");

        migrationBuilder.DropTable(
            name: "quality_holds");

        migrationBuilder.DropTable(
            name: "resource_calendars");

        migrationBuilder.DropTable(
            name: "rework_orders");

        migrationBuilder.DropTable(
            name: "routing_operations");

        migrationBuilder.DropTable(
            name: "sampling_plans");

        migrationBuilder.DropTable(
            name: "schedule_overrides");

        migrationBuilder.DropTable(
            name: "schedule_publications");

        migrationBuilder.DropTable(
            name: "subcontract_custodies");

        migrationBuilder.DropTable(
            name: "subcontract_receipts");

        migrationBuilder.DropTable(
            name: "transformation_events");

        migrationBuilder.DropTable(
            name: "variance_ledger");

        migrationBuilder.DropTable(
            name: "wip_transfers");

        migrationBuilder.DropTable(
            name: "capa_cases");

        migrationBuilder.DropTable(
            name: "inspections");

        migrationBuilder.DropTable(
            name: "maintenance_availability_events");

        migrationBuilder.DropTable(
            name: "mrp_runs");

        migrationBuilder.DropTable(
            name: "nonconformances");

        migrationBuilder.DropTable(
            name: "routing_revisions");

        migrationBuilder.DropTable(
            name: "work_centers");

        migrationBuilder.DropTable(
            name: "quality_plans");

        migrationBuilder.DropTable(
            name: "schedule_scenarios");

        migrationBuilder.DropTable(
            name: "schedule_runs");

        migrationBuilder.DropIndex(
            name: "IX_production_orders_planned_routing_revision_id",
            table: "production_orders");

        migrationBuilder.DropIndex(
            name: "IX_production_orders_released_routing_revision_id",
            table: "production_orders");

        migrationBuilder.DropIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id_planned_routing~",
            table: "production_orders");

        migrationBuilder.DropIndex(
            name: "IX_production_orders_tenant_id_legal_entity_id_released_routin~",
            table: "production_orders");

        migrationBuilder.DeleteData(
            table: "finance_settings",
            keyColumn: "id",
            keyValue: "FS-mfg-downtime");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-012" });

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-012");

        migrationBuilder.DropColumn(
            name: "planned_routing_revision_id",
            table: "production_orders");

        migrationBuilder.DropColumn(
            name: "released_routing_revision_id",
            table: "production_orders");

        migrationBuilder.DropSequence(
            name: "doc_cfl");

        migrationBuilder.DropSequence(
            name: "doc_cme");

        migrationBuilder.DropSequence(
            name: "doc_cpa");

        migrationBuilder.DropSequence(
            name: "doc_dwn");

        migrationBuilder.DropSequence(
            name: "doc_lbt");

        migrationBuilder.DropSequence(
            name: "doc_mav");

        migrationBuilder.DropSequence(
            name: "doc_mcr");

        migrationBuilder.DropSequence(
            name: "doc_mrp");

        migrationBuilder.DropSequence(
            name: "doc_ncr");

        migrationBuilder.DropSequence(
            name: "doc_qhd");

        migrationBuilder.DropSequence(
            name: "doc_qin");

        migrationBuilder.DropSequence(
            name: "doc_qpl");

        migrationBuilder.DropSequence(
            name: "doc_rot");

        migrationBuilder.DropSequence(
            name: "doc_rwk");

        migrationBuilder.DropSequence(
            name: "doc_scn");

        migrationBuilder.DropSequence(
            name: "doc_srn");

        migrationBuilder.DropSequence(
            name: "doc_sub");

        migrationBuilder.DropSequence(
            name: "doc_txp");

        migrationBuilder.DropSequence(
            name: "doc_vln");

        migrationBuilder.DropSequence(
            name: "doc_wkc");

        migrationBuilder.DropSequence(
            name: "doc_wtr");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-MFG",
            column: "permissions",
            value: new[] { "boms:read", "boms:write", "boms:approve", "mfg:read", "mfg:write", "mfg:release", "mfg:override", "mfg:close", "mfg:reopen" });
    }
}
