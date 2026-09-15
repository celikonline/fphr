using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_days",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    check_in = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    check_out = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    planned_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    planned_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    planned_minutes = table.Column<int>(type: "integer", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    worked_minutes = table.Column<int>(type: "integer", nullable: false),
                    anomalies = table.Column<string>(type: "text", nullable: false),
                    resolution_state = table.Column<string>(type: "text", nullable: false),
                    override_worked_minutes = table.Column<int>(type: "integer", nullable: true),
                    override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_days", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_days_tenant_id",
                schema: "attendance",
                table: "attendance_days",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_days_tenant_id_employee_id_date",
                schema: "attendance",
                table: "attendance_days",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_days",
                schema: "attendance");
        }
    }
}
