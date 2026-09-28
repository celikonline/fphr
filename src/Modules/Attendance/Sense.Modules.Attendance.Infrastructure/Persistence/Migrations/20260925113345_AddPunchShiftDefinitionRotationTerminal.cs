using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPunchShiftDefinitionRotationTerminal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "day_type",
                schema: "attendance",
                table: "shift_assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "shift_definition_id",
                schema: "attendance",
                table: "shift_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_terminal_credentials",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_terminal_credentials", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "punch_requests",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_punch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "punches",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    source_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delete_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rotation_assignments",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rotation_pattern_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    offset_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rotation_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rotation_patterns",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    preset = table.Column<string>(type: "text", nullable: false),
                    cycle_length_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rotation_patterns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_definitions",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    start_minute = table.Column<int>(type: "integer", nullable: false),
                    end_minute = table.Column<int>(type: "integer", nullable: false),
                    starts_previous_day = table.Column<bool>(type: "boolean", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    flex_start_earliest_minute = table.Column<int>(type: "integer", nullable: true),
                    flex_start_latest_minute = table.Column<int>(type: "integer", nullable: true),
                    late_tolerance_minutes = table.Column<int>(type: "integer", nullable: false),
                    early_leave_tolerance_minutes = table.Column<int>(type: "integer", nullable: false),
                    early_arrival_overtime_min_minutes = table.Column<int>(type: "integer", nullable: true),
                    early_arrival_overtime_max_minutes = table.Column<int>(type: "integer", nullable: true),
                    late_departure_overtime_min_minutes = table.Column<int>(type: "integer", nullable: true),
                    late_departure_overtime_max_minutes = table.Column<int>(type: "integer", nullable: true),
                    bonus_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    breaks = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_plan_templates",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    period_length_days = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entries = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_plan_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminal_groups",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminal_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminals",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    terminal_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    direction_mode = table.Column<string>(type: "text", nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    api_key_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_tenant_id_shift_definition_id",
                schema: "attendance",
                table: "shift_assignments",
                columns: new[] { "tenant_id", "shift_definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_terminal_credentials_tenant_id",
                schema: "attendance",
                table: "employee_terminal_credentials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_terminal_credentials_tenant_id_employee_id",
                schema: "attendance",
                table: "employee_terminal_credentials",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_terminal_credentials_tenant_id_kind_value",
                schema: "attendance",
                table: "employee_terminal_credentials",
                columns: new[] { "tenant_id", "kind", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_punch_requests_tenant_id",
                schema: "attendance",
                table: "punch_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_punch_requests_tenant_id_approval_request_id",
                schema: "attendance",
                table: "punch_requests",
                columns: new[] { "tenant_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_punch_requests_tenant_id_employee_id_work_date",
                schema: "attendance",
                table: "punch_requests",
                columns: new[] { "tenant_id", "employee_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ix_punches_tenant_id",
                schema: "attendance",
                table: "punches",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_punches_tenant_id_employee_id_work_date",
                schema: "attendance",
                table: "punches",
                columns: new[] { "tenant_id", "employee_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ux_punches_tenant_employee_occurred_direction_source",
                schema: "attendance",
                table: "punches",
                columns: new[] { "tenant_id", "employee_id", "occurred_at_utc", "direction", "source" },
                unique: true,
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rotation_assignments_tenant_id",
                schema: "attendance",
                table: "rotation_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rotation_assignments_tenant_id_employee_id_start_date",
                schema: "attendance",
                table: "rotation_assignments",
                columns: new[] { "tenant_id", "employee_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_rotation_assignments_tenant_id_rotation_pattern_id",
                schema: "attendance",
                table: "rotation_assignments",
                columns: new[] { "tenant_id", "rotation_pattern_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rotation_patterns_tenant_id",
                schema: "attendance",
                table: "rotation_patterns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rotation_patterns_tenant_id_code",
                schema: "attendance",
                table: "rotation_patterns",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_definitions_tenant_id",
                schema: "attendance",
                table: "shift_definitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_definitions_tenant_id_code",
                schema: "attendance",
                table: "shift_definitions",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_plan_templates_tenant_id",
                schema: "attendance",
                table: "shift_plan_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminal_groups_tenant_id",
                schema: "attendance",
                table: "terminal_groups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminals_api_key_prefix",
                schema: "attendance",
                table: "terminals",
                column: "api_key_prefix");

            migrationBuilder.CreateIndex(
                name: "ix_terminals_tenant_id",
                schema: "attendance",
                table: "terminals",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminals_tenant_id_code",
                schema: "attendance",
                table: "terminals",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_terminal_credentials",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "punch_requests",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "punches",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "rotation_assignments",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "rotation_patterns",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "shift_definitions",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "shift_plan_templates",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "terminal_groups",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "terminals",
                schema: "attendance");

            migrationBuilder.DropIndex(
                name: "ix_shift_assignments_tenant_id_shift_definition_id",
                schema: "attendance",
                table: "shift_assignments");

            migrationBuilder.DropColumn(
                name: "day_type",
                schema: "attendance",
                table: "shift_assignments");

            migrationBuilder.DropColumn(
                name: "shift_definition_id",
                schema: "attendance",
                table: "shift_assignments");
        }
    }
}
