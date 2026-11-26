using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Api.Migrations;

/// <inheritdoc />
public partial class _20261125080511_AddPeopleDeferredScreens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "doc_als");

        migrationBuilder.CreateSequence(
            name: "doc_atx");

        migrationBuilder.CreateSequence(
            name: "doc_bpl");

        migrationBuilder.CreateSequence(
            name: "doc_cgr");

        migrationBuilder.CreateSequence(
            name: "doc_cre");

        migrationBuilder.CreateSequence(
            name: "doc_enr");

        migrationBuilder.CreateSequence(
            name: "doc_grd");

        migrationBuilder.CreateSequence(
            name: "doc_gro");

        migrationBuilder.CreateSequence(
            name: "doc_job");

        migrationBuilder.CreateSequence(
            name: "doc_lag");

        migrationBuilder.CreateSequence(
            name: "doc_lle");

        migrationBuilder.CreateSequence(
            name: "doc_lrq");

        migrationBuilder.CreateSequence(
            name: "doc_lty");

        migrationBuilder.CreateSequence(
            name: "doc_obt");

        migrationBuilder.CreateSequence(
            name: "doc_org");

        migrationBuilder.CreateSequence(
            name: "doc_pay");

        migrationBuilder.CreateSequence(
            name: "doc_pos");

        migrationBuilder.CreateSequence(
            name: "doc_ppc");

        migrationBuilder.CreateSequence(
            name: "doc_sch");

        migrationBuilder.CreateSequence(
            name: "doc_txe");

        migrationBuilder.CreateSequence(
            name: "doc_wfp");

        migrationBuilder.CreateTable(
            name: "allocation_sets",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                unit = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_allocation_sets", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "attendance_exceptions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                work_date = table.Column<DateOnly>(type: "date", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                source_fact = table.Column<string>(type: "text", nullable: false),
                disposition = table.Column<string>(type: "text", nullable: true),
                disposition_reason = table.Column<string>(type: "text", nullable: true),
                dispositioned_by = table.Column<string>(type: "text", nullable: true),
                dispositioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_attendance_exceptions", x => x.id);
                table.ForeignKey(
                    name: "FK_attendance_exceptions_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "benefit_plans",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                provider_code = table.Column<string>(type: "text", nullable: true),
                employee_pct = table.Column<decimal>(type: "numeric", nullable: false),
                pay_code_id = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_benefit_plans", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "contribution_rules",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                party = table.Column<string>(type: "text", nullable: false),
                rate_pct = table.Column<decimal>(type: "numeric", nullable: false),
                threshold_amount = table.Column<decimal>(type: "numeric", nullable: true),
                ceiling_amount = table.Column<decimal>(type: "numeric", nullable: true),
                pay_code_id = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contribution_rules", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "credentials",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                issued_by = table.Column<string>(type: "text", nullable: true),
                issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                evidence_ref = table.Column<string>(type: "text", nullable: true),
                waiver_reason = table.Column<string>(type: "text", nullable: true),
                waived_by = table.Column<string>(type: "text", nullable: true),
                waived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_credentials", x => x.id);
                table.ForeignKey(
                    name: "FK_credentials_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "garnishment_orders",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                amount_per_period = table.Column<decimal>(type: "numeric", nullable: false),
                protected_minimum = table.Column<decimal>(type: "numeric", nullable: true),
                court_ref = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                release_evidence = table.Column<string>(type: "text", nullable: true),
                released_by = table.Column<string>(type: "text", nullable: true),
                released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_garnishment_orders", x => x.id);
                table.ForeignKey(
                    name: "FK_garnishment_orders_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "grade_ranges",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                grade_code = table.Column<string>(type: "text", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                frequency = table.Column<string>(type: "text", nullable: false),
                min_amount = table.Column<decimal>(type: "numeric", nullable: false),
                mid_amount = table.Column<decimal>(type: "numeric", nullable: false),
                max_amount = table.Column<decimal>(type: "numeric", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_grade_ranges", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "holiday_calendars",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_holiday_calendars", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "job_profiles",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                department = table.Column<string>(type: "text", nullable: true),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_job_profiles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "leave_types",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                units = table.Column<string>(type: "text", nullable: false),
                accrual_method = table.Column<string>(type: "text", nullable: false),
                accrual_rate_per_month = table.Column<decimal>(type: "numeric", nullable: true),
                annual_entitlement_days = table.Column<decimal>(type: "numeric", nullable: true),
                carryover_limit_days = table.Column<decimal>(type: "numeric", nullable: true),
                requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_leave_types", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "onboarding_templates",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                category_code = table.Column<string>(type: "text", nullable: false),
                task_key = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                due_offset_days = table.Column<int>(type: "integer", nullable: false),
                owner_role = table.Column<string>(type: "text", nullable: false),
                required = table.Column<bool>(type: "boolean", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_onboarding_templates", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "org_units",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                parent_org_unit_id = table.Column<string>(type: "text", nullable: true),
                location_id = table.Column<string>(type: "text", nullable: true),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_org_units", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "payment_files",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                run_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                provider = table.Column<string>(type: "text", nullable: false),
                format = table.Column<string>(type: "text", nullable: false),
                format_version = table.Column<string>(type: "text", nullable: false),
                content_hash = table.Column<string>(type: "text", nullable: false),
                item_count = table.Column<int>(type: "integer", nullable: false),
                total_net = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                released_by = table.Column<string>(type: "text", nullable: true),
                released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_files", x => x.id);
                table.ForeignKey(
                    name: "FK_payment_files_pay_runs_run_id",
                    column: x => x.run_id,
                    principalTable: "pay_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "payroll_account_mappings",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                element = table.Column<string>(type: "text", nullable: false),
                account_code = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payroll_account_mappings", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "payroll_period_closes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                period_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                summary_json = table.Column<string>(type: "text", nullable: true),
                summary_hash = table.Column<string>(type: "text", nullable: true),
                closed_by = table.Column<string>(type: "text", nullable: false),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reopened_by = table.Column<string>(type: "text", nullable: true),
                reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payroll_period_closes", x => x.id);
                table.ForeignKey(
                    name: "FK_payroll_period_closes_fiscal_periods_period_id",
                    column: x => x.period_id,
                    principalTable: "fiscal_periods",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "positions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                org_unit_id = table.Column<string>(type: "text", nullable: true),
                grade_code = table.Column<string>(type: "text", nullable: true),
                required_credential_code = table.Column<string>(type: "text", nullable: true),
                budgeted_fte = table.Column<decimal>(type: "numeric", nullable: false),
                budgeted_headcount = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                source_plan_id = table.Column<string>(type: "text", nullable: true),
                source_plan_line_code = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_positions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "tax_elections",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                value = table.Column<string>(type: "text", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                verified_by = table.Column<string>(type: "text", nullable: true),
                verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tax_elections", x => x.id);
                table.ForeignKey(
                    name: "FK_tax_elections_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "work_locations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                timezone = table.Column<string>(type: "text", nullable: false),
                address = table.Column<string>(type: "text", nullable: true),
                active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_locations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "work_schedules",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                timezone = table.Column<string>(type: "text", nullable: false),
                start_time_local = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_schedules", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "workforce_plans",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                horizon_start = table.Column<DateOnly>(type: "date", nullable: false),
                horizon_end = table.Column<DateOnly>(type: "date", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                assumptions = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workforce_plans", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "allocation_lines",
            columns: table => new
            {
                cost_center = table.Column<string>(type: "text", nullable: false),
                allocation_set_id = table.Column<string>(type: "text", nullable: false),
                pct = table.Column<decimal>(type: "numeric", nullable: false),
                held_for_review = table.Column<bool>(type: "boolean", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_allocation_lines", x => new { x.allocation_set_id, x.cost_center });
                table.ForeignKey(
                    name: "FK_allocation_lines_allocation_sets_allocation_set_id",
                    column: x => x.allocation_set_id,
                    principalTable: "allocation_sets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "benefit_enrollments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                plan_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_benefit_enrollments", x => x.id);
                table.ForeignKey(
                    name: "FK_benefit_enrollments_benefit_plans_plan_id",
                    column: x => x.plan_id,
                    principalTable: "benefit_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_benefit_enrollments_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "holiday_dates",
            columns: table => new
            {
                date = table.Column<DateOnly>(type: "date", nullable: false),
                calendar_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_holiday_dates", x => new { x.calendar_id, x.date });
                table.ForeignKey(
                    name: "FK_holiday_dates_holiday_calendars_calendar_id",
                    column: x => x.calendar_id,
                    principalTable: "holiday_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "job_profile_versions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                profile_id = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_job_profile_versions", x => x.id);
                table.ForeignKey(
                    name: "FK_job_profile_versions_job_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "job_profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "leave_adjustments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                leave_type_id = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                decision_reason = table.Column<string>(type: "text", nullable: true),
                requested_by = table.Column<string>(type: "text", nullable: false),
                decided_by = table.Column<string>(type: "text", nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_leave_adjustments", x => x.id);
                table.ForeignKey(
                    name: "FK_leave_adjustments_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_leave_adjustments_leave_types_leave_type_id",
                    column: x => x.leave_type_id,
                    principalTable: "leave_types",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "leave_ledger_entries",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                leave_type_id = table.Column<string>(type: "text", nullable: false),
                period = table.Column<string>(type: "text", nullable: false),
                source_key = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                qty = table.Column<decimal>(type: "numeric", nullable: false),
                entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                reference_id = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_leave_ledger_entries", x => x.id);
                table.ForeignKey(
                    name: "FK_leave_ledger_entries_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_leave_ledger_entries_leave_types_leave_type_id",
                    column: x => x.leave_type_id,
                    principalTable: "leave_types",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "leave_requests",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                leave_type_id = table.Column<string>(type: "text", nullable: false),
                from_date = table.Column<DateOnly>(type: "date", nullable: false),
                to_date = table.Column<DateOnly>(type: "date", nullable: false),
                days = table.Column<decimal>(type: "numeric", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                decision_reason = table.Column<string>(type: "text", nullable: true),
                requested_by = table.Column<string>(type: "text", nullable: false),
                decided_by = table.Column<string>(type: "text", nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_leave_requests", x => x.id);
                table.ForeignKey(
                    name: "FK_leave_requests_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_leave_requests_leave_types_leave_type_id",
                    column: x => x.leave_type_id,
                    principalTable: "leave_types",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "onboarding_tasks",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                template_id = table.Column<string>(type: "text", nullable: false),
                task_key = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: false),
                owner_role = table.Column<string>(type: "text", nullable: false),
                required = table.Column<bool>(type: "boolean", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                decision_reason = table.Column<string>(type: "text", nullable: true),
                decided_by = table.Column<string>(type: "text", nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_onboarding_tasks", x => x.id);
                table.ForeignKey(
                    name: "FK_onboarding_tasks_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_onboarding_tasks_onboarding_templates_template_id",
                    column: x => x.template_id,
                    principalTable: "onboarding_templates",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "payment_file_items",
            columns: table => new
            {
                employment_id = table.Column<string>(type: "text", nullable: false),
                payment_file_id = table.Column<string>(type: "text", nullable: false),
                net_amount = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                reject_reason = table.Column<string>(type: "text", nullable: true),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_file_items", x => new { x.payment_file_id, x.employment_id });
                table.ForeignKey(
                    name: "FK_payment_file_items_payment_files_payment_file_id",
                    column: x => x.payment_file_id,
                    principalTable: "payment_files",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "position_assignments",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                position_id = table.Column<string>(type: "text", nullable: false),
                employment_id = table.Column<string>(type: "text", nullable: false),
                fte = table.Column<decimal>(type: "numeric", nullable: false),
                cost_center = table.Column<string>(type: "text", nullable: true),
                manager_user_id = table.Column<string>(type: "text", nullable: true),
                location_id = table.Column<string>(type: "text", nullable: true),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                exception_approved_by = table.Column<string>(type: "text", nullable: true),
                approved_variance = table.Column<decimal>(type: "numeric", nullable: true),
                exception_reason = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_position_assignments", x => x.id);
                table.ForeignKey(
                    name: "FK_position_assignments_employments_employment_id",
                    column: x => x.employment_id,
                    principalTable: "employments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_position_assignments_positions_position_id",
                    column: x => x.position_id,
                    principalTable: "positions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "schedule_versions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                schedule_id = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                weekly_planned_hours = table.Column<decimal>(type: "numeric", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                holiday_calendar_id = table.Column<string>(type: "text", nullable: true),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_versions", x => x.id);
                table.ForeignKey(
                    name: "FK_schedule_versions_work_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "work_schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "workforce_plan_lines",
            columns: table => new
            {
                position_code = table.Column<string>(type: "text", nullable: false),
                workforce_plan_id = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                fte = table.Column<decimal>(type: "numeric", nullable: false),
                org_unit_id = table.Column<string>(type: "text", nullable: true),
                grade_code = table.Column<string>(type: "text", nullable: true),
                promoted = table.Column<bool>(type: "boolean", nullable: false),
                promoted_position_id = table.Column<string>(type: "text", nullable: true),
                legal_entity_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workforce_plan_lines", x => new { x.workforce_plan_id, x.position_code });
                table.ForeignKey(
                    name: "FK_workforce_plan_lines_workforce_plans_workforce_plan_id",
                    column: x => x.workforce_plan_id,
                    principalTable: "workforce_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "deduction_instructions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                enrollment_id = table.Column<string>(type: "text", nullable: false),
                period = table.Column<string>(type: "text", nullable: false),
                amount = table.Column<decimal>(type: "numeric", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deduction_instructions", x => x.id);
                table.ForeignKey(
                    name: "FK_deduction_instructions_benefit_enrollments_enrollment_id",
                    column: x => x.enrollment_id,
                    principalTable: "benefit_enrollments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "competencies",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<string>(type: "text", nullable: false),
                legal_entity_id = table.Column<string>(type: "text", nullable: false),
                profile_version_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                level = table.Column<string>(type: "text", nullable: false),
                required = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_competencies", x => x.id);
                table.ForeignKey(
                    name: "FK_competencies_job_profile_versions_profile_version_id",
                    column: x => x.profile_version_id,
                    principalTable: "job_profile_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write", "leave:adjust", "payroll:release", "payroll:close", "people:analytics:read" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-HR",
            column: "permissions",
            value: new[] { "workers:read", "workers:write", "time:read", "time:write", "time:approve", "payroll:read", "payroll:calculate", "org:write", "onboarding:write", "compmaster:write", "schedules:write", "leave:write", "leave:approve", "credentials:write", "payinputs:write", "workforce:write", "people:analytics:read" });

        migrationBuilder.InsertData(
            table: "users",
            columns: new[] { "id", "active", "display_name", "email", "legal_entity_id", "tenant_id" },
            values: new object[] { "U-010", true, "HR Checker", "hr-checker@teamzlab.dev", "LE-001", "T-001" });

        migrationBuilder.InsertData(
            table: "memberships",
            columns: new[] { "legal_entity_id", "tenant_id", "user_id", "role_id" },
            values: new object[] { "LE-001", "T-001", "U-010", "R-HR" });

        migrationBuilder.CreateIndex(
            name: "IX_allocation_lines_allocation_set_id",
            table: "allocation_lines",
            column: "allocation_set_id");

        migrationBuilder.CreateIndex(
            name: "IX_allocation_lines_tenant_id_legal_entity_id",
            table: "allocation_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_allocation_sets_tenant_id_legal_entity_id",
            table: "allocation_sets",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_allocation_sets_tenant_id_legal_entity_id_code",
            table: "allocation_sets",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_attendance_exceptions_employment_id",
            table: "attendance_exceptions",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_attendance_exceptions_tenant_id_legal_entity_id",
            table: "attendance_exceptions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_attendance_exceptions_tenant_id_legal_entity_id_work_date",
            table: "attendance_exceptions",
            columns: new[] { "tenant_id", "legal_entity_id", "work_date" });

        migrationBuilder.CreateIndex(
            name: "IX_benefit_enrollments_employment_id",
            table: "benefit_enrollments",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_benefit_enrollments_plan_id",
            table: "benefit_enrollments",
            column: "plan_id");

        migrationBuilder.CreateIndex(
            name: "IX_benefit_enrollments_tenant_id_legal_entity_id",
            table: "benefit_enrollments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_benefit_enrollments_tenant_id_legal_entity_id_employment_id~",
            table: "benefit_enrollments",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_benefit_plans_pay_code_id",
            table: "benefit_plans",
            column: "pay_code_id");

        migrationBuilder.CreateIndex(
            name: "IX_benefit_plans_tenant_id_legal_entity_id",
            table: "benefit_plans",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_benefit_plans_tenant_id_legal_entity_id_code",
            table: "benefit_plans",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_competencies_profile_version_id",
            table: "competencies",
            column: "profile_version_id");

        migrationBuilder.CreateIndex(
            name: "IX_competencies_tenant_id_legal_entity_id",
            table: "competencies",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_contribution_rules_pay_code_id",
            table: "contribution_rules",
            column: "pay_code_id");

        migrationBuilder.CreateIndex(
            name: "IX_contribution_rules_tenant_id_legal_entity_id",
            table: "contribution_rules",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_contribution_rules_tenant_id_legal_entity_id_code",
            table: "contribution_rules",
            columns: new[] { "tenant_id", "legal_entity_id", "code" });

        migrationBuilder.CreateIndex(
            name: "IX_credentials_employment_id",
            table: "credentials",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_credentials_tenant_id_legal_entity_id",
            table: "credentials",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_credentials_tenant_id_legal_entity_id_employment_id_code",
            table: "credentials",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "code" });

        migrationBuilder.CreateIndex(
            name: "IX_deduction_instructions_enrollment_id",
            table: "deduction_instructions",
            column: "enrollment_id");

        migrationBuilder.CreateIndex(
            name: "IX_deduction_instructions_tenant_id_legal_entity_id",
            table: "deduction_instructions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_deduction_instructions_tenant_id_legal_entity_id_enrollment~",
            table: "deduction_instructions",
            columns: new[] { "tenant_id", "legal_entity_id", "enrollment_id", "period" });

        migrationBuilder.CreateIndex(
            name: "IX_garnishment_orders_employment_id",
            table: "garnishment_orders",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_garnishment_orders_tenant_id_legal_entity_id",
            table: "garnishment_orders",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_garnishment_orders_tenant_id_legal_entity_id_employment_id_~",
            table: "garnishment_orders",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_grade_ranges_tenant_id_legal_entity_id",
            table: "grade_ranges",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_grade_ranges_tenant_id_legal_entity_id_grade_code_effective~",
            table: "grade_ranges",
            columns: new[] { "tenant_id", "legal_entity_id", "grade_code", "effective_from" });

        migrationBuilder.CreateIndex(
            name: "IX_holiday_calendars_tenant_id_legal_entity_id",
            table: "holiday_calendars",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_holiday_calendars_tenant_id_legal_entity_id_code",
            table: "holiday_calendars",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_holiday_dates_calendar_id",
            table: "holiday_dates",
            column: "calendar_id");

        migrationBuilder.CreateIndex(
            name: "IX_holiday_dates_tenant_id_legal_entity_id",
            table: "holiday_dates",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_job_profile_versions_profile_id",
            table: "job_profile_versions",
            column: "profile_id");

        migrationBuilder.CreateIndex(
            name: "IX_job_profile_versions_profile_id_version",
            table: "job_profile_versions",
            columns: new[] { "profile_id", "version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_job_profile_versions_tenant_id_legal_entity_id",
            table: "job_profile_versions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_job_profiles_tenant_id_legal_entity_id",
            table: "job_profiles",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_job_profiles_tenant_id_legal_entity_id_code",
            table: "job_profiles",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_leave_adjustments_employment_id",
            table: "leave_adjustments",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_leave_adjustments_leave_type_id",
            table: "leave_adjustments",
            column: "leave_type_id");

        migrationBuilder.CreateIndex(
            name: "IX_leave_adjustments_tenant_id_legal_entity_id",
            table: "leave_adjustments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_leave_adjustments_tenant_id_legal_entity_id_employment_id",
            table: "leave_adjustments",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id" });

        migrationBuilder.CreateIndex(
            name: "IX_leave_ledger_entries_employment_id",
            table: "leave_ledger_entries",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_leave_ledger_entries_leave_type_id",
            table: "leave_ledger_entries",
            column: "leave_type_id");

        migrationBuilder.CreateIndex(
            name: "IX_leave_ledger_entries_tenant_id_legal_entity_id_employment_~1",
            table: "leave_ledger_entries",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "leave_type_id", "period", "source_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_leave_ledger_entries_tenant_id_legal_entity_id_employment_i~",
            table: "leave_ledger_entries",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "leave_type_id", "entry_date" });

        migrationBuilder.CreateIndex(
            name: "IX_leave_requests_employment_id",
            table: "leave_requests",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_leave_requests_leave_type_id",
            table: "leave_requests",
            column: "leave_type_id");

        migrationBuilder.CreateIndex(
            name: "IX_leave_requests_tenant_id_legal_entity_id",
            table: "leave_requests",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_leave_requests_tenant_id_legal_entity_id_employment_id_stat~",
            table: "leave_requests",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_leave_types_tenant_id_legal_entity_id",
            table: "leave_types",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_leave_types_tenant_id_legal_entity_id_code",
            table: "leave_types",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_onboarding_tasks_employment_id",
            table: "onboarding_tasks",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_onboarding_tasks_template_id",
            table: "onboarding_tasks",
            column: "template_id");

        migrationBuilder.CreateIndex(
            name: "IX_onboarding_tasks_tenant_id_legal_entity_id",
            table: "onboarding_tasks",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_onboarding_tasks_tenant_id_legal_entity_id_employment_id_ta~",
            table: "onboarding_tasks",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "task_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_onboarding_templates_tenant_id_legal_entity_id",
            table: "onboarding_templates",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_onboarding_templates_tenant_id_legal_entity_id_category_cod~",
            table: "onboarding_templates",
            columns: new[] { "tenant_id", "legal_entity_id", "category_code", "task_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_org_units_parent_org_unit_id",
            table: "org_units",
            column: "parent_org_unit_id");

        migrationBuilder.CreateIndex(
            name: "IX_org_units_tenant_id_legal_entity_id",
            table: "org_units",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_org_units_tenant_id_legal_entity_id_code",
            table: "org_units",
            columns: new[] { "tenant_id", "legal_entity_id", "code" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_file_items_payment_file_id",
            table: "payment_file_items",
            column: "payment_file_id");

        migrationBuilder.CreateIndex(
            name: "IX_payment_file_items_tenant_id_legal_entity_id",
            table: "payment_file_items",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_files_run_id",
            table: "payment_files",
            column: "run_id");

        migrationBuilder.CreateIndex(
            name: "IX_payment_files_tenant_id_legal_entity_id_run_id",
            table: "payment_files",
            columns: new[] { "tenant_id", "legal_entity_id", "run_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payment_files_tenant_id_legal_entity_id_status",
            table: "payment_files",
            columns: new[] { "tenant_id", "legal_entity_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_payroll_account_mappings_tenant_id_legal_entity_id",
            table: "payroll_account_mappings",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_payroll_account_mappings_tenant_id_legal_entity_id_element_~",
            table: "payroll_account_mappings",
            columns: new[] { "tenant_id", "legal_entity_id", "element", "effective_from" });

        migrationBuilder.CreateIndex(
            name: "IX_payroll_period_closes_period_id",
            table: "payroll_period_closes",
            column: "period_id");

        migrationBuilder.CreateIndex(
            name: "IX_payroll_period_closes_tenant_id_legal_entity_id",
            table: "payroll_period_closes",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_position_assignments_employment_id",
            table: "position_assignments",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_position_assignments_position_id",
            table: "position_assignments",
            column: "position_id");

        migrationBuilder.CreateIndex(
            name: "IX_position_assignments_tenant_id_legal_entity_id",
            table: "position_assignments",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_position_assignments_tenant_id_legal_entity_id_employment_id",
            table: "position_assignments",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id" });

        migrationBuilder.CreateIndex(
            name: "IX_position_assignments_tenant_id_legal_entity_id_position_id_~",
            table: "position_assignments",
            columns: new[] { "tenant_id", "legal_entity_id", "position_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_positions_org_unit_id",
            table: "positions",
            column: "org_unit_id");

        migrationBuilder.CreateIndex(
            name: "IX_positions_tenant_id_legal_entity_id",
            table: "positions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_positions_tenant_id_legal_entity_id_code",
            table: "positions",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_positions_tenant_id_legal_entity_id_org_unit_id",
            table: "positions",
            columns: new[] { "tenant_id", "legal_entity_id", "org_unit_id" });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_versions_schedule_id",
            table: "schedule_versions",
            column: "schedule_id");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_versions_schedule_id_effective_from",
            table: "schedule_versions",
            columns: new[] { "schedule_id", "effective_from" });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_versions_tenant_id_legal_entity_id",
            table: "schedule_versions",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_tax_elections_employment_id",
            table: "tax_elections",
            column: "employment_id");

        migrationBuilder.CreateIndex(
            name: "IX_tax_elections_tenant_id_legal_entity_id",
            table: "tax_elections",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_tax_elections_tenant_id_legal_entity_id_employment_id_effec~",
            table: "tax_elections",
            columns: new[] { "tenant_id", "legal_entity_id", "employment_id", "effective_from" });

        migrationBuilder.CreateIndex(
            name: "IX_work_locations_tenant_id_legal_entity_id",
            table: "work_locations",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_work_locations_tenant_id_legal_entity_id_code",
            table: "work_locations",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_work_schedules_tenant_id_legal_entity_id",
            table: "work_schedules",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_work_schedules_tenant_id_legal_entity_id_code",
            table: "work_schedules",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_workforce_plan_lines_tenant_id_legal_entity_id",
            table: "workforce_plan_lines",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_workforce_plan_lines_workforce_plan_id",
            table: "workforce_plan_lines",
            column: "workforce_plan_id");

        migrationBuilder.CreateIndex(
            name: "IX_workforce_plans_tenant_id_legal_entity_id",
            table: "workforce_plans",
            columns: new[] { "tenant_id", "legal_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_workforce_plans_tenant_id_legal_entity_id_code",
            table: "workforce_plans",
            columns: new[] { "tenant_id", "legal_entity_id", "code" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "allocation_lines");

        migrationBuilder.DropTable(
            name: "attendance_exceptions");

        migrationBuilder.DropTable(
            name: "competencies");

        migrationBuilder.DropTable(
            name: "contribution_rules");

        migrationBuilder.DropTable(
            name: "credentials");

        migrationBuilder.DropTable(
            name: "deduction_instructions");

        migrationBuilder.DropTable(
            name: "garnishment_orders");

        migrationBuilder.DropTable(
            name: "grade_ranges");

        migrationBuilder.DropTable(
            name: "holiday_dates");

        migrationBuilder.DropTable(
            name: "leave_adjustments");

        migrationBuilder.DropTable(
            name: "leave_ledger_entries");

        migrationBuilder.DropTable(
            name: "leave_requests");

        migrationBuilder.DropTable(
            name: "onboarding_tasks");

        migrationBuilder.DropTable(
            name: "org_units");

        migrationBuilder.DropTable(
            name: "payment_file_items");

        migrationBuilder.DropTable(
            name: "payroll_account_mappings");

        migrationBuilder.DropTable(
            name: "payroll_period_closes");

        migrationBuilder.DropTable(
            name: "position_assignments");

        migrationBuilder.DropTable(
            name: "schedule_versions");

        migrationBuilder.DropTable(
            name: "tax_elections");

        migrationBuilder.DropTable(
            name: "work_locations");

        migrationBuilder.DropTable(
            name: "workforce_plan_lines");

        migrationBuilder.DropTable(
            name: "allocation_sets");

        migrationBuilder.DropTable(
            name: "job_profile_versions");

        migrationBuilder.DropTable(
            name: "benefit_enrollments");

        migrationBuilder.DropTable(
            name: "holiday_calendars");

        migrationBuilder.DropTable(
            name: "leave_types");

        migrationBuilder.DropTable(
            name: "onboarding_templates");

        migrationBuilder.DropTable(
            name: "payment_files");

        migrationBuilder.DropTable(
            name: "positions");

        migrationBuilder.DropTable(
            name: "work_schedules");

        migrationBuilder.DropTable(
            name: "workforce_plans");

        migrationBuilder.DropTable(
            name: "job_profiles");

        migrationBuilder.DropTable(
            name: "benefit_plans");

        migrationBuilder.DeleteData(
            table: "memberships",
            keyColumns: new[] { "legal_entity_id", "tenant_id", "user_id" },
            keyValues: new object[] { "LE-001", "T-001", "U-010" });

        migrationBuilder.DeleteData(
            table: "users",
            keyColumn: "id",
            keyValue: "U-010");

        migrationBuilder.DropSequence(
            name: "doc_als");

        migrationBuilder.DropSequence(
            name: "doc_atx");

        migrationBuilder.DropSequence(
            name: "doc_bpl");

        migrationBuilder.DropSequence(
            name: "doc_cgr");

        migrationBuilder.DropSequence(
            name: "doc_cre");

        migrationBuilder.DropSequence(
            name: "doc_enr");

        migrationBuilder.DropSequence(
            name: "doc_grd");

        migrationBuilder.DropSequence(
            name: "doc_gro");

        migrationBuilder.DropSequence(
            name: "doc_job");

        migrationBuilder.DropSequence(
            name: "doc_lag");

        migrationBuilder.DropSequence(
            name: "doc_lle");

        migrationBuilder.DropSequence(
            name: "doc_lrq");

        migrationBuilder.DropSequence(
            name: "doc_lty");

        migrationBuilder.DropSequence(
            name: "doc_obt");

        migrationBuilder.DropSequence(
            name: "doc_org");

        migrationBuilder.DropSequence(
            name: "doc_pay");

        migrationBuilder.DropSequence(
            name: "doc_pos");

        migrationBuilder.DropSequence(
            name: "doc_ppc");

        migrationBuilder.DropSequence(
            name: "doc_sch");

        migrationBuilder.DropSequence(
            name: "doc_txe");

        migrationBuilder.DropSequence(
            name: "doc_wfp");

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-FIN",
            column: "permissions",
            value: new[] { "journals:read", "journals:approve", "journals:write", "ap:read", "po:approve", "periods:read", "periods:write", "tax:read", "tax:write", "accounts:read", "accounts:write", "ar:read", "invoices:read", "so:approve", "invoices:approve", "credit:read", "credit:override", "sourcing:read", "sourcing:approve", "contracts:read", "contracts:approve", "payroll:read", "payroll:approve", "paycodes:read", "paycodes:write" });

        migrationBuilder.UpdateData(
            table: "roles",
            keyColumn: "id",
            keyValue: "R-HR",
            column: "permissions",
            value: new[] { "workers:read", "workers:write", "time:read", "time:write", "time:approve", "payroll:read", "payroll:calculate" });
    }
}
