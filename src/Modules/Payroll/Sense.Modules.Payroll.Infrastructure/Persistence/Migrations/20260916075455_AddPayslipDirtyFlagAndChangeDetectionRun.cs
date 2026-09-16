using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayslipDirtyFlagAndChangeDetectionRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "change_detection_runs",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    scanned_count = table.Column<int>(type: "integer", nullable: false),
                    dirty_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_detection_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payslip_dirty_flags",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reasons = table.Column<string>(type: "jsonb", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cleared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cleared_by_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslip_dirty_flags", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_change_detection_runs_tenant_id",
                schema: "payroll",
                table: "change_detection_runs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_detection_runs_tenant_id_period_id_started_at",
                schema: "payroll",
                table: "change_detection_runs",
                columns: new[] { "tenant_id", "period_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payslip_dirty_flags_tenant_id",
                schema: "payroll",
                table: "payslip_dirty_flags",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_dirty_flags_tenant_id_payroll_period_id_employee_id",
                schema: "payroll",
                table: "payslip_dirty_flags",
                columns: new[] { "tenant_id", "payroll_period_id", "employee_id", "cleared_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_detection_runs",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "payslip_dirty_flags",
                schema: "payroll");
        }
    }
}
