using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sense.Modules.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollPeriodReopen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reopen_reason",
                schema: "payroll",
                table: "payroll_periods",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reopened_at",
                schema: "payroll",
                table: "payroll_periods",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reopen_reason",
                schema: "payroll",
                table: "payroll_periods");

            migrationBuilder.DropColumn(
                name: "reopened_at",
                schema: "payroll",
                table: "payroll_periods");
        }
    }
}
