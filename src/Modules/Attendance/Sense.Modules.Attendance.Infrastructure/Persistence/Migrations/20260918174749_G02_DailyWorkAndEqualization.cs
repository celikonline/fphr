using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class G02_DailyWorkAndEqualization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "equalization_anchor_month",
                schema: "attendance",
                table: "shift_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "equalization_enabled",
                schema: "attendance",
                table: "shift_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "equalization_period_months",
                schema: "attendance",
                table: "shift_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "has_written_equalization_agreement",
                schema: "attendance",
                table: "shift_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "actual_break_minutes",
                schema: "attendance",
                table: "attendance_days",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "break_minutes_applied",
                schema: "attendance",
                table: "attendance_days",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "daily_limit_exceeded",
                schema: "attendance",
                table: "attendance_days",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "net_worked_minutes",
                schema: "attendance",
                table: "attendance_days",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "night_limit_exceeded",
                schema: "attendance",
                table: "attendance_days",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "night_minutes",
                schema: "attendance",
                table: "attendance_days",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "overtime_minutes",
                schema: "attendance",
                table: "attendance_days",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "equalization_periods",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    total_worked_minutes = table.Column<int>(type: "integer", nullable: false),
                    budget_minutes = table.Column<int>(type: "integer", nullable: false),
                    true_up_overtime_minutes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equalization_periods", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_equalization_periods_tenant_id",
                schema: "attendance",
                table: "equalization_periods",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_equalization_periods_tenant_id_employee_id_period_start",
                schema: "attendance",
                table: "equalization_periods",
                columns: new[] { "tenant_id", "employee_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_equalization_periods_tenant_id_employee_id_status_period_end",
                schema: "attendance",
                table: "equalization_periods",
                columns: new[] { "tenant_id", "employee_id", "status", "period_end" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equalization_periods",
                schema: "attendance");

            migrationBuilder.DropColumn(
                name: "equalization_anchor_month",
                schema: "attendance",
                table: "shift_settings");

            migrationBuilder.DropColumn(
                name: "equalization_enabled",
                schema: "attendance",
                table: "shift_settings");

            migrationBuilder.DropColumn(
                name: "equalization_period_months",
                schema: "attendance",
                table: "shift_settings");

            migrationBuilder.DropColumn(
                name: "has_written_equalization_agreement",
                schema: "attendance",
                table: "shift_settings");

            migrationBuilder.DropColumn(
                name: "actual_break_minutes",
                schema: "attendance",
                table: "attendance_days");

            migrationBuilder.DropColumn(
                name: "break_minutes_applied",
                schema: "attendance",
                table: "attendance_days");

            migrationBuilder.DropColumn(
                name: "daily_limit_exceeded",
                schema: "attendance",
                table: "attendance_days");

            migrationBuilder.DropColumn(
                name: "net_worked_minutes",
                schema: "attendance",
                table: "attendance_days");

            migrationBuilder.DropColumn(
                name: "night_limit_exceeded",
                schema: "attendance",
                table: "attendance_days");

            migrationBuilder.DropColumn(
                name: "night_minutes",
                schema: "attendance",
                table: "attendance_days");

            migrationBuilder.DropColumn(
                name: "overtime_minutes",
                schema: "attendance",
                table: "attendance_days");
        }
    }
}
