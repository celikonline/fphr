using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShiftPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_assignments",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_night = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_plan_locks",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    unlocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unlocked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unlock_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_plan_locks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_settings",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_daily_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_weekly_days = table.Column<int>(type: "integer", nullable: false),
                    max_weekly_minutes = table.Column<int>(type: "integer", nullable: false),
                    night_work_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    night_work_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    night_work_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    night_work_max_minutes = table.Column<int>(type: "integer", nullable: true),
                    break_min_minutes = table.Column<int>(type: "integer", nullable: false),
                    break_is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    auto_lock_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    auto_lock_day_of_month = table.Column<int>(type: "integer", nullable: false),
                    auto_lock_notify_days_before = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_tenant_id",
                schema: "attendance",
                table: "shift_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_tenant_id_employee_id_date",
                schema: "attendance",
                table: "shift_assignments",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_plan_locks_tenant_id",
                schema: "attendance",
                table: "shift_plan_locks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_plan_locks_tenant_id_period_month",
                schema: "attendance",
                table: "shift_plan_locks",
                columns: new[] { "tenant_id", "period_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_settings_tenant_id",
                schema: "attendance",
                table: "shift_settings",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_assignments",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "shift_plan_locks",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "shift_settings",
                schema: "attendance");
        }
    }
}
