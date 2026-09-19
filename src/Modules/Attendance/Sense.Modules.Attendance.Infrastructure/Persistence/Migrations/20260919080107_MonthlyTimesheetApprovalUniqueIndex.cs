using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Attendance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyTimesheetApprovalUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_monthly_timesheet_approvals_tenant_id_employee_id_period_mo",
                schema: "attendance",
                table: "monthly_timesheet_approvals",
                columns: new[] { "tenant_id", "employee_id", "period_month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monthly_timesheet_approvals_tenant_id_employee_id_period_mo",
                schema: "attendance",
                table: "monthly_timesheet_approvals");
        }
    }
}
