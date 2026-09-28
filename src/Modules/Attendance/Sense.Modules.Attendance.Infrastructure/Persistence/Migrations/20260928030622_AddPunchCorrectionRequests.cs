using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPunchCorrectionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "punch_correction_requests",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_check_in = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    requested_check_out = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    current_check_in = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    current_check_out = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch_correction_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_punch_correction_requests_tenant_id",
                schema: "attendance",
                table: "punch_correction_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_punch_correction_requests_tenant_id_employee_id_attendance_",
                schema: "attendance",
                table: "punch_correction_requests",
                columns: new[] { "tenant_id", "employee_id", "attendance_date" },
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_punch_correction_requests_tenant_id_employee_id_submitted_at",
                schema: "attendance",
                table: "punch_correction_requests",
                columns: new[] { "tenant_id", "employee_id", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "punch_correction_requests",
                schema: "attendance");
        }
    }
}
