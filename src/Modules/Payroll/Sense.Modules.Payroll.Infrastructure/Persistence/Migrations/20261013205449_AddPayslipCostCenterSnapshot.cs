using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayslipCostCenterSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cost_center_id",
                schema: "payroll",
                table: "payslips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "cost_center_snapshot_captured",
                schema: "payroll",
                table: "payslips",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_center_id",
                schema: "payroll",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "cost_center_snapshot_captured",
                schema: "payroll",
                table: "payslips");
        }
    }
}
